#!/usr/bin/env python3
"""Fine-tune the DeepLabV3+/ConvNeXt-Tiny roads model on commune-validated
drafts.

Loads the chip dataset produced by prepare_local_labels.py (1024px satellite
chips + road masks), reuses the exact serving architecture from
nars-segma/app/deeplabv3p.py (DeepLabV3Plus(num_classes=1) full-resolution
sigmoid logit), and trains the whole net from the timm ImageNet-pretrained
ConvNeXt-Tiny encoder. The saved checkpoint is the plain whole-net state_dict,
which the serving side loads with torch.load(weights_only=True) into a
pretrained=False copy — key names match because timm is pinned to 0.9.7 on
both sides (see nars-segma/requirements.txt).

Input normalization matches serving (`SegmentationModel.imagenet_norm`):
[0,1] pixels minus ImageNet mean/std.
"""

from __future__ import annotations

import argparse
import csv
import math
import os
import random
import sys
from pathlib import Path

import numpy as np
import torch
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SEGMA = ROOT.parent / "nars-segma"
sys.path.insert(0, str(SEGMA))

from app.deeplabv3p import DeepLabV3Plus  # noqa: E402

IMAGENET_MEAN = torch.tensor([0.485, 0.456, 0.406])
IMAGENET_STD = torch.tensor([0.229, 0.224, 0.225])


def _seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)


class RoadsDataset(torch.utils.data.Dataset):
    def __init__(self, chips_dir: Path, manifest: list[dict]):
        self.chips_dir = Path(chips_dir)
        self.items = manifest
        self.images: list[np.ndarray] = []
        self.masks: list[np.ndarray] = []
        for row in manifest:
            chip = self.chips_dir / f"{row['chip_id']}.png"
            msk = self.chips_dir / f"{row['chip_id']}_mask.png"
            img = np.asarray(Image.open(chip).convert("RGB"), dtype=np.float32) / 255.0
            m = np.asarray(Image.open(msk), dtype=np.float32) / 255.0
            m = (m > 0.5).astype(np.float32)
            self.images.append(img)
            self.masks.append(m)

    def __len__(self) -> int:
        return len(self.items)

    def __getitem__(self, idx: int) -> tuple[torch.Tensor, torch.Tensor]:
        img = self.images[idx]
        msk = self.masks[idx]
        if random.random() < 0.5:
            img = img[:, ::-1, :]
            msk = msk[:, ::-1]
        if random.random() < 0.5:
            img = img[::-1, :, :]
            msk = msk[::-1, :]
        k = random.choice([0, 1, 2, 3])
        if k:
            img = np.rot90(img, k, (0, 1)).copy()
            msk = np.rot90(msk, k, (0, 1)).copy()
        x = torch.from_numpy(np.ascontiguousarray(img)).permute(2, 0, 1).float()
        y = torch.from_numpy(np.ascontiguousarray(msk)).unsqueeze(0).float()
        return x, y


def soft_dice_loss(logits: torch.Tensor, target: torch.Tensor) -> torch.Tensor:
    prob = torch.sigmoid(logits)
    inter = (prob * target).sum(dim=(1, 2, 3))
    denom = prob.sum(dim=(1, 2, 3)) + target.sum(dim=(1, 2, 3)) + 1e-6
    return (1.0 - (2 * inter + 1e-6) / (denom + 1e-6)).mean()


def make_loader(
    dataset: RoadsDataset, batch_size: int, shuffle: bool
) -> torch.utils.data.DataLoader:
    return torch.utils.data.DataLoader(
        dataset, batch_size=batch_size, shuffle=shuffle, num_workers=0, pin_memory=True
    )


@torch.no_grad()
def evaluate(model: torch.nn.Module, loader: torch.utils.data.DataLoader, device: torch.device) -> dict[str, float]:
    model.eval()
    ious, dices = [], []
    for x, y in loader:
        x, y = x.to(device), y.to(device)
        with torch.autocast("cuda", dtype=torch.float16):
            logits = model(x)
            prob = torch.sigmoid(logits.float())
        pred = (prob > 0.5).float()
        inter = (pred * y).sum(dim=(1, 2, 3))
        union = (pred + y - pred * y).sum(dim=(1, 2, 3))
        for j in range(inter.shape[0]):
            denom = min(union[j].item() + 1e-6, y[j].sum().item() * 2 + 1e-6)
            ious.append(inter[j].item() / max(union[j].item(), 1e-6))
            fp = max(pred[j].sum().item() - inter[j].item(), 0.0)
            fn = max(y[j].sum().item() - inter[j].item(), 0.0)
            dices.append((2 * inter[j].item()) / (2 * inter[j].item() + fp + fn + 1e-6))
    return {"iou": float(np.mean(ious)) if ious else 0.0, "dice": float(np.mean(dices)) if dices else 0.0}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=str(ROOT / "data"))
    ap.add_argument("--out", default=str(ROOT / "checkpoints"))
    ap.add_argument("--epochs", type=int, default=50)
    ap.add_argument("--batch-size", type=int, default=4)
    ap.add_argument("--lr", type=float, default=1e-4)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--quick", action="store_true", help="2-epoch smoke run on first chips")
    args = ap.parse_args()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    if device.type != "cuda":
        sys.exit("training requires CUDA (RTX 2060 host)")
    _seed_everything(args.seed)

    manifest = list(csv.DictReader(open(Path(args.data) / "chips" / "manifest.csv")))
    assert manifest, "no chips in manifest"
    road_chips = [r for r in manifest if r["has_road"] == "1"]
    if args.quick:
        road_chips = road_chips[:8]
    random.Random(args.seed).shuffle(road_chips)
    n_val = max(1, round(len(road_chips) * 0.2))
    val_rows, train_rows = road_chips[:n_val], road_chips[n_val:]
    print(f"train {len(train_rows)} chips, val {len(val_rows)} chips")

    train_ds = RoadsDataset(Path(args.data) / "chips", train_rows)
    val_ds = RoadsDataset(Path(args.data) / "chips", val_rows)
    fg = float(np.mean([(m > 0.5).mean() for m in train_ds.masks]))
    print(f"foreground pixel fraction: {fg:.4%}")
    pos_weight = torch.tensor([min(max((1.0 - fg) / max(fg, 1e-6), 1.0), 30.0)]).to(device)
    print(f"BCE pos_weight: {pos_weight.item():.1f}")

    print("building DeepLabV3+/ConvNeXt-Tiny (pretrained=True ...)")
    model = DeepLabV3Plus(num_classes=1, pretrained=True)
    model.to(device)
    bce = torch.nn.BCEWithLogitsLoss(pos_weight=pos_weight)
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    steps = len(train_ds) // args.batch_size
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
        optimizer, T_max=max(steps * args.epochs, 1)
    )
    scaler = torch.amp.GradScaler("cuda")

    Path(args.out).mkdir(parents=True, exist_ok=True)
    best_path = Path(args.out) / "roads_dlv3p_convnext_tiny.pth"
    best_iou, patience = 0.0, 0
    train_loader = make_loader(train_ds, args.batch_size, shuffle=True)
    val_loader = make_loader(val_ds, args.batch_size, shuffle=False)

    for epoch in range(1, args.epochs + 1):
        model.train()
        total_loss = 0.0
        for x, y in train_loader:
            x, y = x.to(device), y.to(device)
            optimizer.zero_grad()
            with torch.autocast("cuda", dtype=torch.float16):
                logits = model(x)
                loss = bce(logits, y) + soft_dice_loss(logits, y)
            scaler.scale(loss).backward()
            scaler_ok = scaler.step(optimizer)
            scaler.update()
            if scaler_ok:
                scheduler.step()
            total_loss += loss.item()
        avg = total_loss / max(len(train_loader), 1)
        metrics = evaluate(model, val_loader, device)
        flag = ""
        if metrics["iou"] > best_iou:
            best_iou, patience = metrics["iou"], 0
            torch.save(model.state_dict(), best_path)
            flag = " *"
        else:
            patience += 1
        print(
            f"epoch {epoch:02d}/{args.epochs} loss {avg:.4f} "
            f"valIoU {metrics['iou']:.4f} valDice {metrics['dice']:.4f}{flag}",
            flush=True,
        )
        if patience >= 8 and epoch > 15:
            print("early stop")
            break

    if not os.path.exists(best_path):
        model = model.eval()
    print(f"best valIoU {best_iou:.4f}  -> saved {best_path}")


if __name__ == "__main__":
    main()