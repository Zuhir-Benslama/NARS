"""Convert the legacy pickled SpaceNet road checkpoint into a plain state_dict.

The published SpaceNet 3 champion weights (`spacenet-3/01-Albu/weights/
fold{0..3}_best.pth`) are pickled model objects of the class
`pytorch_zoo.unet.Resnet34_upsample`. The serving code only ever loads plain
state_dicts with `weights_only=True` (a malicious pickle must not execute in
the pod), so this script re-packs a downloaded checkpoint into that safe
format. It is run by the `convert-roads-weights` initContainer and by
developers:

    python -m app.scripts.convert_roads --input fold0_best.pth --output weights/roads_best.pth

The pickle is loaded with weights_only=False because the checkpoint predates
weight-safe serialization; that is acceptable here because (1) the classes it
can instantiate are restricted to the vendored Resnet34Upsample via
`register_pytorch_zoo_aliases`, and (2) this script never runs with network
access or secrets in the serving pod.
"""

from __future__ import annotations

import argparse
import logging

from app.road_model import Resnet34Upsample, register_pytorch_zoo_aliases

logger = logging.getLogger("nars-segma.convert_roads")


class ArchitectureMismatchError(RuntimeError):
    """Raised when a checkpoint tensor does not fit the vendored Resnet34Upsample."""

    def __init__(
        self, key: str, found: tuple[int, ...], expected: tuple[int, ...]
    ) -> None:
        super().__init__(
            f"Architecture mismatch at '{key}': checkpoint has shape {found}, "
            f"vendored model expects {expected}"
        )


def convert(input_path: str, output_path: str) -> None:
    register_pytorch_zoo_aliases()

    import torch

    # The checkpoint predates weights_only=True; it can only reconstruct the
    # allowlisted Resnet34Upsample class (see register_pytorch_zoo_aliases), so
    # an unsafe load in this isolated conversion step is acceptable (see module
    # docstring).
    model = torch.load(input_path, map_location="cpu", weights_only=False)
    state_dict = model.state_dict()

    # The checkpoint was written by a pre-torch-1.0 runtime, whose BatchNorm
    # state lacks `num_batches_tracked`. Backfill defaults from a freshly
    # constructed instance so a strict load_state_dict succeeds at serving
    # time, and fail loudly on any genuine architecture mismatch.
    fresh = Resnet34Upsample(num_classes=1, num_channels=3).state_dict()
    for key, value in fresh.items():
        if key not in state_dict:
            state_dict[key] = value.clone()
        elif state_dict[key].shape != value.shape:
            raise ArchitectureMismatchError(
                key, tuple(state_dict[key].shape), tuple(value.shape)
            )

    torch.save(state_dict, output_path)
    param_count = sum(t.numel() for t in state_dict.values())
    logger.info(
        "Converted %s -> %s (%d tensors, %d parameters)",
        input_path,
        output_path,
        len(state_dict),
        param_count,
    )


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Path to the pickled checkpoint")
    parser.add_argument(
        "--output", required=True, help="Path where the plain state_dict is written"
    )
    args = parser.parse_args()
    convert(args.input, args.output)


if __name__ == "__main__":
    main()
