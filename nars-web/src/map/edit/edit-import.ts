// ─── EDIT IMPORT ──────────────────────────────────────────────────────────────
// Builds GeoJSON features for Geoman's importGeoJson API.
// Handles points, lines, polygons, and circle rings.

import type { LatLng } from "../../types"
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

  const d = entry.data as { lat?: number; lng?: number; radius?: number; coordinates?: LatLng[] }

  if (entry.type === "circle" && d.lat != null && d.lng != null && d.radius) {
    const ring = computeCircleRingForEdit(d.lat, d.lng, d.radius)
    ring.push([ring[0][0], ring[0][1]])
    return {
      type: "Feature",
      geometry: { type: "LineString", coordinates: ring },
      properties: props,
    }
  }

  if (d.lat != null && d.lng != null) {
    return {
      type: "Feature",
      geometry: {
        type: "Point",
        coordinates: [d.lng, d.lat],
      },
      properties: props,
    }
  }

  if (d.coordinates && d.coordinates.length > 0) {
    const coords = d.coordinates.map((c) => [c.lng, c.lat])
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
