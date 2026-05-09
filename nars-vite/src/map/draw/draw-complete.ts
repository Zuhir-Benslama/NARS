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

export async function removeLastVertex(): Promise<void> {
  const gm = ctx.geoman as unknown as Record<string, unknown> | null
  if (!gm) return

  const polygonInst = (gm.actionInstances as Record<string, unknown> | undefined)?.["draw__polygon"]
  const lineInst = (gm.actionInstances as Record<string, unknown> | undefined)?.["draw__line"]
  const drawInstance = (polygonInst ?? lineInst) as Record<string, unknown> | undefined
  const lineDrawer = drawInstance?.lineDrawer as Record<string, unknown> | undefined
  if (!lineDrawer?.featureData) return

  const coords: [number, number][] = lineDrawer.shapeLngLats as [number, number][]
  if (coords.length <= 1) {
    void (gm.disableDraw as Function)()
    return
  }
  coords.pop()

  const isPolygon = !!polygonInst
  const markers = lineDrawer.featureData as Record<string, unknown> | undefined
  const markersMap = markers?.markers as Map<string, Record<string, unknown>> | undefined
  if (markersMap) {
    const entries = Array.from(markersMap.entries())
    if (entries.length > 0) {
      const [key, markerData] = entries[entries.length - 1]
      const instance = markerData?.instance as Record<string, unknown> | undefined
      const removeFn = instance?.remove as unknown as (() => void) | undefined
      removeFn?.()
      markersMap.delete(key)
    }
  }

  const controlMarker = (lineDrawer?.gm as Record<string, unknown> | undefined)?.markerPointer as
    | Record<string, unknown>
    | undefined
  const markerControl = controlMarker?.marker as maplibregl.Marker | undefined

  if (isPolygon) {
    const ring: [number, number][] = [...coords]
    if (markerControl) {
      const ll = markerControl.getLngLat()
      ring.push([ll.lng, ll.lat])
    }
    if (ring.length > 0) {
      ring.push([ring[0][0], ring[0][1]])
    }

    const fd = lineDrawer.featureData as Record<string, unknown>
    await (fd.updateGeometry as Function)({
      type: "Polygon",
      coordinates: [ring],
    })

    if (fd.convertToPolygon) {
      await (fd.convertToPolygon as Function)()
    }

    if (markerControl) {
      const fireEvent = fd.fireUpdateEvent as Function | undefined
      if (fireEvent) {
        await fireEvent(fd, {
          type: "dom",
          instance: markerControl,
          position: {
            coordinate: [markerControl.getLngLat().lng, markerControl.getLngLat().lat],
            path: ["geometry", "coordinates", coords.length],
          },
        })
      }
    }
  } else {
    const fd = lineDrawer.featureData as Record<string, unknown>
    const getGeoJson = lineDrawer.getFeatureGeoJson as Function
    await (fd.updateGeometry as Function)(getGeoJson({ withControlMarker: true }).geometry)
    if (markerControl) {
      const fireEvent = fd.fireUpdateEvent as Function | undefined
      if (fireEvent) {
        await fireEvent(fd, {
          type: "dom",
          instance: markerControl,
          position: {
            coordinate: [markerControl.getLngLat().lng, markerControl.getLngLat().lat],
            path: ["geometry", "coordinates", coords.length],
          },
        })
      }
    }
  }

  const snapHelper = lineDrawer.snappingHelper as Record<string, unknown> | undefined
  const setCustomSnap = snapHelper?.setCustomSnappingCoordinates as Function | undefined
  setCustomSnap?.(lineDrawer.snappingKey, coords)
  if (typeof lineDrawer.setSnapping === "function") {
    ;(lineDrawer.setSnapping as Function)()
  }
}
