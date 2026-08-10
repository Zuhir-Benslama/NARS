from typing import Any

from pydantic import BaseModel


class FeatureGeometry(BaseModel):
    type: str
    coordinates: Any


class Feature(BaseModel):
    type: str = "Feature"
    geometry: FeatureGeometry
    properties: dict[str, Any]


class FeatureCollection(BaseModel):
    type: str = "FeatureCollection"
    features: list[Feature]


class SegmentResponse(BaseModel):
    roads: FeatureCollection
    buildings: FeatureCollection
