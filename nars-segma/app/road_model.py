"""
Road segmentation model: the SpaceNet 3 winning architecture (Buslaev 2018).

This is a self-contained port of `pytorch_zoo.unet.Resnet34_upsample` (the
model behind the SpaceNet 3 Road Network Detection champion, checkpoints at
`spacenet-model-weights/spacenet-3/01-Albu/`). Buildings stays on
segmentation-models-pytorch; this module gives roads its own architecture
without dragging in a new dependency (the resnet34 backbone is vendored
below, torch-only, exactly matching the checkpoint's parameter names).

Weights: SpaceNet dataset (CC-BY-SA 4.0, see LICENSE.md at the S3 bucket
root). Model code ported from the SpaceNetChallenge/RoadDetector repo
`albu-solution/src/pytorch_zoo/`, Apache-2.0, Copyright 2018 CosmiQ Works,
An In-Q-Tel Lab. Ported to match the published checkpoints' state_dict keys
byte-for-byte; the only structural change is that encoder pretrained weights
are never auto-downloaded here (checkpoints carry the trained backbone).
"""

from __future__ import annotations

import math

import torch
from torch import nn

__all__ = ["Resnet34Upsample", "Resnet34_upsample", "register_pytorch_zoo_aliases"]

# Encoder stage filters, matching `abstract_model.encoder_params` so the
# decoder width, bottleneck layout and final classifier reproduce the
# checkpoint exactly.
_RESNET34_FILTERS = [64, 64, 128, 256, 512]


class EncoderLayerError(ValueError):
    """Raised for an out-of-range encoder stage index (expected <1 or >4)."""

    def __init__(self, layer: int) -> None:
        super().__init__(f"encoder layer index out of range: {layer}")


class ConvBottleneck(nn.Module):
    """Cat(decoder, encoder skip) fused with a 1x1-style 3x3 conv + ReLU."""

    def __init__(self, in_channels: int, out_channels: int) -> None:
        super().__init__()
        self.seq = nn.Sequential(
            nn.Conv2d(in_channels, out_channels, 3, padding=1),
            nn.ReLU(inplace=True),
        )

    def forward(self, dec: torch.Tensor, enc: torch.Tensor) -> torch.Tensor:
        x = torch.cat([dec, enc], dim=1)
        return self.seq(x)


class UnetDecoderBlock(nn.Module):
    """2x upsample + 3x3 conv + ReLU (mode-free default = nearest)."""

    def __init__(self, in_channels: int, out_channels: int) -> None:
        super().__init__()
        self.layer = nn.Sequential(
            nn.Upsample(scale_factor=2),
            nn.Conv2d(in_channels, out_channels, 3, padding=1),
            nn.ReLU(inplace=True),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.layer(x)


class _ResNet34Backbone(nn.Module):
    """Minimal torch-only resnet34 (BasicBlock). Structure is identical to
    torchvision so `layer1..4` / `conv1` / `bn1` key names match the SpaceNet
    checkpoints; no torchvision dependency is introduced."""

    def __init__(self, in_channels: int = 3) -> None:
        super().__init__()
        self.conv1 = nn.Conv2d(
            in_channels, 64, kernel_size=7, stride=2, padding=3, bias=False
        )
        self.bn1 = nn.BatchNorm2d(64)
        self.relu = nn.ReLU(inplace=True)
        self.maxpool = nn.MaxPool2d(kernel_size=3, stride=2, padding=1)
        self.layer1 = self._make_layer(64, 64, 3)
        self.layer2 = self._make_layer(64, 128, 4, stride=2)
        self.layer3 = self._make_layer(128, 256, 6, stride=2)
        self.layer4 = self._make_layer(256, 512, 3, stride=2)
        self._initialize_weights()

    def _make_layer(
        self, inplanes: int, planes: int, blocks: int, stride: int = 1
    ) -> nn.Sequential:
        downsample: nn.Module | None = None
        if stride != 1 or inplanes != planes:
            downsample = nn.Sequential(
                nn.Conv2d(inplanes, planes, kernel_size=1, stride=stride, bias=False),
                nn.BatchNorm2d(planes),
            )
        layers = [_BasicBlock(inplanes, planes, stride, downsample)]
        for _ in range(1, blocks):
            layers.append(_BasicBlock(planes, planes))
        return nn.Sequential(*layers)

    def _initialize_weights(self) -> None:
        for m in self.modules():
            if isinstance(m, nn.Conv2d):
                n = m.kernel_size[0] * m.kernel_size[1] * m.out_channels
                m.weight.data.normal_(0, math.sqrt(2.0 / n))
            elif isinstance(m, nn.BatchNorm2d):
                m.weight.data.fill_(1)
                m.bias.data.zero_()


class _BasicBlock(nn.Module):
    expansion = 1

    def __init__(
        self,
        inplanes: int,
        planes: int,
        stride: int = 1,
        downsample: nn.Module | None = None,
    ) -> None:
        super().__init__()
        self.conv1 = nn.Conv2d(
            inplanes, planes, kernel_size=3, stride=stride, padding=1, bias=False
        )
        self.bn1 = nn.BatchNorm2d(planes)
        self.relu = nn.ReLU(inplace=True)
        self.conv2 = nn.Conv2d(planes, planes, kernel_size=3, padding=1, bias=False)
        self.bn2 = nn.BatchNorm2d(planes)
        self.downsample = downsample
        self.stride = stride

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        residual = x
        out = self.relu(self.bn1(self.conv1(x)))
        out = self.bn2(self.conv2(out))
        if self.downsample is not None:
            residual = self.downsample(x)
        out += residual
        return self.relu(out)


class Resnet34Upsample(nn.Module):
    """Resnet34 encoder + U-Net decoder, the architecture of the SpaceNet 3
    champion road model. `num_classes=1` yields a single foreground logit
    (the published checkpoints), matching `predict`'s sigmoid path."""

    def __init__(self, num_classes: int = 1, num_channels: int = 3) -> None:
        super().__init__()
        filters = _RESNET34_FILTERS
        self.filters = filters
        self.num_channels = num_channels
        self.bottleneck_type = ConvBottleneck

        self.bottlenecks = nn.ModuleList(
            [self.bottleneck_type(f * 2, f) for f in reversed(filters[:-1])]
        )
        self.decoder_stages = nn.ModuleList(
            [
                UnetDecoderBlock(filters[i], filters[max(i - 1, 0)])
                for i in range(1, len(filters))
            ]
        )
        self.last_upsample = UnetDecoderBlock(filters[0], filters[0] // 2)
        self.final = nn.Sequential(
            nn.Conv2d(filters[0] // 2, num_classes, 3, padding=1)
        )
        self._initialize_weights()

        encoder = _ResNet34Backbone(in_channels=num_channels)
        self.encoder_stages = nn.ModuleList(
            [self._get_encoder(encoder, idx) for idx in range(len(filters))]
        )

    def _get_encoder(self, encoder: _ResNet34Backbone, layer: int) -> nn.Module:
        if layer == 0:
            return nn.Sequential(encoder.conv1, encoder.bn1, encoder.relu)
        if layer == 1:
            return nn.Sequential(encoder.maxpool, encoder.layer1)
        if layer == 2:
            return encoder.layer2
        if layer == 3:
            return encoder.layer3
        if layer == 4:
            return encoder.layer4
        raise EncoderLayerError(layer)

    def _initialize_weights(self) -> None:
        for m in self.modules():
            if isinstance(m, nn.Conv2d):
                n = m.kernel_size[0] * m.kernel_size[1] * m.out_channels
                m.weight.data.normal_(0, math.sqrt(2.0 / n))
                if m.bias is not None:
                    m.bias.data.zero_()
            elif isinstance(m, nn.BatchNorm2d):
                m.weight.data.fill_(1)
                m.bias.data.zero_()

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        enc_results = []
        for idx, stage in enumerate(self.encoder_stages):
            x = stage(x)
            if idx < len(self.encoder_stages) - 1:
                enc_results.append(x.clone())

        for idx, bottleneck in enumerate(self.bottlenecks):
            rev_idx = -(idx + 1)
            x = self.decoder_stages[rev_idx](x)
            x = bottleneck(x, enc_results[rev_idx])

        x = self.last_upsample(x)
        return self.final(x)


# The published checkpoints pickle the model as `pytorch_zoo.unet.Resnet34_upsample`.
# Alias kept so unpickling (via register_pytorch_zoo_aliases) resolves to this class.
Resnet34_upsample = Resnet34Upsample


def register_pytorch_zoo_aliases() -> None:
    """Make the `pytorch_zoo.*` names referenced by the legacy pickled SpaceNet
    checkpoints importable so they can be unpacked by the conversion script
    (see app/scripts/convert_roads.py). The pickles were written against qubvel's
    `pytorch_zoo` package; each referenced class is aliased to the functionally
    identical vendored class. Registers throwaway modules in sys.modules;
    harmless when unused."""
    import sys
    import types

    class _NoRestoreValue:
        """Sentinel for class attributes the checkpoint references but that are
        not part of the serving model (see `_pytorch_zoo_qualnames`)."""

    def _bind(module: types.ModuleType, name: str, value: object) -> None:
        # setattr (not attribute assignment) so mypy accepts dynamic attrs on a
        # synthetic ModuleType.
        setattr(module, name, value)

    pytorch_zoo = types.ModuleType("pytorch_zoo")
    sys.modules.setdefault("pytorch_zoo", pytorch_zoo)

    abstract_model = types.ModuleType("pytorch_zoo.abstract_model")
    _bind(abstract_model, "ConvBottleneck", ConvBottleneck)
    _bind(abstract_model, "PlusBottleneck", ConvBottleneck)
    _bind(abstract_model, "UnetDecoderBlock", UnetDecoderBlock)
    _bind(abstract_model, "EncoderDecoder", Resnet34Upsample)
    _bind(abstract_model, "AbstractModel", nn.Module)
    sys.modules.setdefault("pytorch_zoo.abstract_model", abstract_model)

    resnet = types.ModuleType("pytorch_zoo.resnet")
    _bind(resnet, "BasicBlock", _BasicBlock)
    _bind(resnet, "Bottleneck", _BasicBlock)
    _bind(resnet, "ResNet", _ResNet34Backbone)
    sys.modules.setdefault("pytorch_zoo.resnet", resnet)

    unet = types.ModuleType("pytorch_zoo.unet")
    _bind(unet, "Resnet", Resnet34Upsample)
    _bind(unet, "Resnet34_upsample", Resnet34_upsample)
    _bind(unet, "Resnet34Upsample", Resnet34Upsample)
    sys.modules.setdefault("pytorch_zoo.unet", unet)

    # Non-module globals the pickles may reference.
    _bind(abstract_model, "_NoRestoreValue", _NoRestoreValue)
    _bind(resnet, "model_urls", {"resnet34": None})
