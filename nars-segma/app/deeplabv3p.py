"""
DeepLabV3+ road model with a ConvNeXt-Tiny encoder.

Migrated from the SpaceNet 3 champion `Resnet34Upsample` (app/road_model.py)
to a modern encoder-decoder so the network can be (re)trained on OpenEarthMap
and fine-tuned on commune-validated drafts. The decoder follows Chen et al.
(2018): an ASPP head over a 1/16-resolution feature map, a 1x1-projected
1/4-resolution low-level feature, a 4x upsample, concat and two 3x3 convs
down to a single foreground logit.

The encoder is `timm.create_model("convnext_tiny", features_only=True,
out_indices=(0, 2))`: stage 0 (1/4, 96 ch) is the DeepLabV3+ low-level
feature, stage 2 (1/16, 192 ch) feeds the ASPP. `pretrained` must be True at
training time only (ImageNet init); the serving image builds `pretrained=False`
and loads the exported plain state_dict, so inference never touches the
network. `timm` is pinned (requirements.txt) so encoder key names can never
drift between the training and serving processes.

State_dict contract: keys are `stem.*`, `stages.{0,1,2,3}.stages.*` (timm) plus
the decoder's own `low_level.*`, `aspp.*`, `reduce_*` and `classifier.*`. The
training export writes the whole net; serving loads it with
`torch.load(..., weights_only=True)` then `load_state_dict` like every other
checkpoint.
"""

from __future__ import annotations

import torch
import torch.nn.functional as F
from torch import nn

__all__ = ["ConvNeXtTinyBackbone", "DeepLabV3Plus"]

# ASPP output channel width; follows the DeepLabV3+ reference (256).
_ASPP_OUT_CH = 256
# Low-level feature projection width; reference value is 48.
_LOW_LEVEL_CH = 48
# Where in the encoder's stage list the two features come from.
_LOW_LEVEL_STAGE = 0
_HIGH_LEVEL_STAGE = 2


class ConvNeXtTinyBackbone(nn.Module):
    """timm ConvNeXt-Tiny feature extractor (stage 0 + stage 2 only)."""

    def __init__(self, pretrained: bool = False) -> None:
        super().__init__()
        import timm

        self.encoder = timm.create_model(
            "convnext_tiny",
            pretrained=pretrained,
            features_only=True,
            out_indices=(_LOW_LEVEL_STAGE, _HIGH_LEVEL_STAGE),
            in_chans=3,
        )

    def forward(self, x: torch.Tensor) -> list[torch.Tensor]:
        return self.encoder(x)


class _ASPP(nn.Module):
    """Atrous Spatial Pyramid Pooling: 1x1 + three 3x3 dilated at 6/12/18
    plus an image-level branch, fused back to `_ASPP_OUT_CH` channels."""

    def __init__(self, in_channels: int, out_channels: int = _ASPP_OUT_CH) -> None:
        super().__init__()
        self.branches = nn.ModuleList(
            [
                nn.Sequential(
                    nn.Conv2d(in_channels, out_channels, 1, bias=False),
                    nn.BatchNorm2d(out_channels),
                    nn.ReLU(inplace=True),
                ),
                nn.Sequential(
                    nn.Conv2d(
                        in_channels,
                        out_channels,
                        3,
                        padding=6,
                        dilation=6,
                        bias=False,
                    ),
                    nn.BatchNorm2d(out_channels),
                    nn.ReLU(inplace=True),
                ),
                nn.Sequential(
                    nn.Conv2d(
                        in_channels,
                        out_channels,
                        3,
                        padding=12,
                        dilation=12,
                        bias=False,
                    ),
                    nn.BatchNorm2d(out_channels),
                    nn.ReLU(inplace=True),
                ),
                nn.Sequential(
                    nn.Conv2d(
                        in_channels,
                        out_channels,
                        3,
                        padding=18,
                        dilation=18,
                        bias=False,
                    ),
                    nn.BatchNorm2d(out_channels),
                    nn.ReLU(inplace=True),
                ),
            ]
        )
        self.image_pool = nn.Sequential(
            nn.AdaptiveAvgPool2d(1),
            nn.Conv2d(in_channels, out_channels, 1, bias=False),
            nn.BatchNorm2d(out_channels),
            nn.ReLU(inplace=True),
        )
        self.project = nn.Sequential(
            nn.Conv2d(out_channels * 5, out_channels, 1, bias=False),
            nn.BatchNorm2d(out_channels),
            nn.ReLU(inplace=True),
            nn.Dropout(0.1),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        h, w = x.shape[-2:]
        pooled = F.interpolate(
            self.image_pool(x), size=(h, w), mode="bilinear", align_corners=False
        )
        return self.project(
            torch.cat([branch(x) for branch in self.branches] + [pooled], dim=1)
        )


class DeepLabV3Plus(nn.Module):
    """DeepLabV3+ (ConvNeXt-Tiny encoder) with a single foreground logit.

    Mirrors the serving contract of the SpaceNet roads model: 3-channel input,
    one sigmoid logit out (num_classes=1, see SegmentationModel.activation).
    """

    def __init__(self, num_classes: int = 1, pretrained: bool = False) -> None:
        super().__init__()
        self.backbone = ConvNeXtTinyBackbone(pretrained=pretrained)
        # ConvNeXt-Tiny stage dims: stem 96, then [96, 192, 384, 768] per
        # stage, with the downsampling folded into the start of each stage —
        # stage 0 (1/4) has 96 channels and stage 2 (1/16) has 384.
        encoder_ch = {
            _LOW_LEVEL_STAGE: 96,
            _HIGH_LEVEL_STAGE: 384,
        }
        self.low_level = nn.Sequential(
            nn.Conv2d(encoder_ch[_LOW_LEVEL_STAGE], _LOW_LEVEL_CH, 1, bias=False),
            nn.BatchNorm2d(_LOW_LEVEL_CH),
            nn.ReLU(inplace=True),
        )
        self.aspp = _ASPP(encoder_ch[_HIGH_LEVEL_STAGE])
        self.decoder = nn.Sequential(
            nn.Conv2d(_ASPP_OUT_CH + _LOW_LEVEL_CH, 256, 3, padding=1, bias=False),
            nn.BatchNorm2d(256),
            nn.ReLU(inplace=True),
            nn.Conv2d(256, 256, 3, padding=1, bias=False),
            nn.BatchNorm2d(256),
            nn.ReLU(inplace=True),
        )
        self.classifier = nn.Conv2d(256, num_classes, 1)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        low, high = self.backbone(x)
        low = self.low_level(low)
        high = self.aspp(high)
        high = F.interpolate(
            high, size=low.shape[-2:], mode="bilinear", align_corners=False
        )
        out = self.decoder(torch.cat([high, low], dim=1))
        out = self.classifier(out)
        # The serving contract is a full-resolution prob map: the decoder
        # leaves the logit at 1/4 scale, so upsampled to the input before the
        # sigmoid in SegmentationModel.activation.
        return F.interpolate(
            out, size=x.shape[-2:], mode="bilinear", align_corners=False
        )
