// ─── ROAD MARKERS ─────────────────────────────────────────────────────────────
// Update endpoint source with start/end-of-road markers.
// Start marker (→) at coords[0], rotated to segment direction.
// End marker (X) at coords[last], rotated to segment direction.
// Start marker suppressed if road starts at city center location.

import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { ctx } from "../core/state"
import { PHASES } from "../../phases"
import { debugLog, debugWarn } from "../../utils/debug"
import type { Coord } from "./road-graph"

/** Update the endpoint source with start/end-of-road markers. */
export function updateEndpointMarkers(): void {
  const endpointsSource = ctx.endpointsSource
  if (!endpointsSource) {
    debugWarn("[NARS ENDPOINTS] ctx.endpointsSource is NOT set!")
    return
  }
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState
  const roads = state.roads || []

  const roadsPhase = PHASES.find((p) => p.key === "roads")
  const roadColor = roadsPhase?.color ?? "#3498db"

  const features: GeoJSON.Feature[] = []

  const ccLatLngs: Coord[] = (state.cityCenter || [])
    .filter((e) => e.data.lat != null && e.data.lng != null)
    .map((e) => ({ lat: e.data.lat!, lng: e.data.lng! }))

  for (const road of roads) {
    const coords = road.data.coordinates
    if (!coords || coords.length < 2) continue

    const startAngle = computeSegmentAngle(coords[0], coords[1])
    const endAngle = computeSegmentAngle(coords[coords.length - 2], coords[coords.length - 1])

    const startLL = coords[0]
    const onCityCenter = ccLatLngs.some((cc) => haversineMeters(startLL, cc) < 2)
    if (!onCityCenter) {
      features.push({
        type: "Feature",
        geometry: { type: "Point", coordinates: [startLL.lng, startLL.lat] },
        properties: {
          endpointType: "start",
          color: roadColor,
          angle: startAngle,
        },
      } as GeoJSON.Feature)
    }

    const endPt = coords[coords.length - 1]
    features.push({
      type: "Feature",
      geometry: { type: "Point", coordinates: [endPt.lng, endPt.lat] },
      properties: {
        endpointType: "end",
        color: roadColor,
        angle: endAngle,
      },
    } as GeoJSON.Feature)
  }

  endpointsSource.setData({
    type: "FeatureCollection",
    features,
  })
  debugLog("[NARS ENDPOINTS]", roads.length, "roads →", features.length, "endpoint markers")
  if (features.length > 0) {
    debugLog("[NARS ENDPOINTS] First marker:", features[0])
  }
}

/** Compute the bearing angle (in degrees) from point A to point B,
 * matching the Leaflet segmentAngle() formula used in nars-vite. */
function computeSegmentAngle(a: Coord, b: Coord): number {
  const fp = ctx.map.project([a.lng, a.lat])
  const tp = ctx.map.project([b.lng, b.lat])
  return Math.atan2(tp.y - fp.y, tp.x - fp.x) * (180 / Math.PI)
}

/** Haversine distance in meters between two Coord objects. */
function haversineMeters(a: Coord, b: Coord): number {
  const R = 6371000
  const dLat = ((b.lat - a.lat) * Math.PI) / 180
  const dLng = ((b.lng - a.lng) * Math.PI) / 180
  const x =
    Math.sin(dLat / 2) ** 2 +
    Math.cos((a.lat * Math.PI) / 180) * Math.cos((b.lat * Math.PI) / 180) * Math.sin(dLng / 2) ** 2
  return R * 2 * Math.atan2(Math.sqrt(x), Math.sqrt(1 - x))
}
