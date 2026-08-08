from typing import Any

from pydantic import BaseModel


class FeatureCollection(BaseModel):
    type: str = "FeatureCollection"
    features: list[dict[str, Any]]


class SegmentResponse(BaseModel):
    roads: FeatureCollection
    buildings: FeatureCollection
