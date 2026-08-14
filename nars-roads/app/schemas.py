"""Response schemas for the segmentation service.

These mirror the GeoJSON FeatureCollection shape that nars-api persists into
ai_draft_features.
"""

from typing import Any

from pydantic import BaseModel


class FeatureGeometry(BaseModel):
    """GeoJSON geometry object: a type name plus its coordinates."""

    type: str
    coordinates: Any


class Feature(BaseModel):
    """A single GeoJSON Feature with geometry and properties."""

    type: str = "Feature"
    geometry: FeatureGeometry
    properties: dict[str, Any]


class FeatureCollection(BaseModel):
    """A GeoJSON FeatureCollection wrapping a list of Features."""

    type: str = "FeatureCollection"
    features: list[Feature]


class SegmentResponse(BaseModel):
    """Segmentation result keyed by feature type. Buildings only today; roads
    will be added here when a road checkpoint exists in the model registry."""

    buildings: FeatureCollection
