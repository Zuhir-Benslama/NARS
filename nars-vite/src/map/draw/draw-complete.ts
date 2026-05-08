// ─── DRAW COMPLETE (BARREL) ───────────────────────────────────────────────────
// Re-exports from split modules for backward compatibility.
// Contains removeLastVertex (not moved — tightly coupled to Geoman internals).

import { ctx } from "../core/state"

// ─── RE-EXPORTS ───────────────────────────────────────────────────────────────

export {
  registerGeomanMarker,
  unpatchGeomanMarker,
  isSnappingEnabled,
  setSnappingEnabled,
  setRepatchMarkerPointer,
  repatchMarker,
  setDrawingPhase,
  getDrawingPhase,
  isSavingFeature,
  setSavingFeature,
} from "./draw-state"

export { normalizeGeometry, completeDrawingWithGeometry, getFeatureStyle } from "./draw-save"

// ─── REMOVE LAST VERTEX ───────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
export async function removeLastVertex(): Promise<void> {
  const gm = ctx.geoman as any
  if (!gm) return

  const polygonInst = gm.actionInstances?.["draw__polygon"]
  const lineInst = gm.actionInstances?.["draw__line"]
  const drawInstance = polygonInst ?? lineInst
  const lineDrawer = drawInstance?.lineDrawer
  if (!lineDrawer?.featureData) return

  const coords: [number, number][] = lineDrawer.shapeLngLats
  if (coords.length <= 1) {
    void gm.disableDraw()
    return
  }
  coords.pop()

  const isPolygon = !!polygonInst
  const markers: Map<string, any> | undefined = lineDrawer.featureData.markers
  if (markers) {
    const entries = Array.from(markers.entries())
    if (entries.length > 0) {
      const [key, markerData] = entries[entries.length - 1]
      markerData?.instance?.remove?.()
      markers.delete(key)
    }
  }

  const controlMarker = lineDrawer.gm?.markerPointer?.marker

  if (isPolygon) {
    const ring: [number, number][] = [...coords]
    if (controlMarker) {
      const ll = controlMarker.getLngLat()
      ring.push([ll.lng, ll.lat])
    }
    if (ring.length > 0) {
      ring.push([ring[0][0], ring[0][1]])
    }

    await lineDrawer.featureData.updateGeometry({
      type: "Polygon",
      coordinates: [ring],
    })

    if (lineDrawer.featureData.convertToPolygon) {
      await lineDrawer.featureData.convertToPolygon()
    }

    if (controlMarker && lineDrawer.fireUpdateEvent) {
      await lineDrawer.fireUpdateEvent(lineDrawer.featureData, {
        type: "dom",
        instance: controlMarker,
        position: {
          coordinate: [controlMarker.getLngLat().lng, controlMarker.getLngLat().lat],
          path: ["geometry", "coordinates", coords.length],
        },
      })
    }
  } else {
    await lineDrawer.featureData.updateGeometry(
      lineDrawer.getFeatureGeoJson({ withControlMarker: true }).geometry,
    )
    if (controlMarker && lineDrawer.fireUpdateEvent) {
      await lineDrawer.fireUpdateEvent(lineDrawer.featureData, {
        type: "dom",
        instance: controlMarker,
        position: {
          coordinate: [controlMarker.getLngLat().lng, controlMarker.getLngLat().lat],
          path: ["geometry", "coordinates", coords.length],
        },
      })
    }
  }

  lineDrawer.snappingHelper?.setCustomSnappingCoordinates?.(lineDrawer.snappingKey, coords)
  if (typeof lineDrawer.setSnapping === "function") {
    lineDrawer.setSnapping()
  }
}
/* eslint-enable @typescript-eslint/no-explicit-any */
