// ─── EDIT IMPORT ──────────────────────────────────────────────────────────────
// Builds GeoJSON features for Geoman's importGeoJson API.
// Handles points, lines, polygons, and circle rings.

import { computeCircleRingForEdit } from "../rendering/geometry"
import type { LayerEntry } from "../../types"

// ─── GEOMAN IMPORT FEATURE ───────────────────────────────────────────────────

export function buildGeomanImportFeature(entry: LayerEntry): GeoJSON.Feature | null {
  const shape =
    entry.type === "line"
      ? "line"
      : entry.type === "marker"
        ? "marker"
        : entry.type === "circle"
          ? "line"
          : "polygon"

  const props = { shape, dbId: entry.dbId }

  if (
    entry.type === "circle" &&
    entry.data.lat != null &&
    entry.data.lng != null &&
    entry.data.radius
  ) {
    const ring = computeCircleRingForEdit(entry.data.lat, entry.data.lng, entry.data.radius)
    ring.push([ring[0][0], ring[0][1]])
    return {
      type: "Feature",
      geometry: { type: "LineString", coordinates: ring },
      properties: props,
    }
  }

  if (entry.data.lat != null && entry.data.lng != null) {
    return {
      type: "Feature",
      geometry: {
        type: "Point",
        coordinates: [entry.data.lng, entry.data.lat],
      },
      properties: props,
    }
  }

  if (entry.data.coordinates && entry.data.coordinates.length > 0) {
    const coords = entry.data.coordinates.map((c) => [c.lng, c.lat])
    if (entry.type === "line") {
      return {
        type: "Feature",
        geometry: { type: "LineString", coordinates: coords },
        properties: props,
      }
    }
    const first = coords[0],
      last = coords[coords.length - 1]
    const ring = first[0] === last[0] && first[1] === last[1] ? coords : [...coords, coords[0]]
    return {
      type: "Feature",
      geometry: { type: "Polygon", coordinates: [ring] },
      properties: props,
    }
  }

  return null
}
