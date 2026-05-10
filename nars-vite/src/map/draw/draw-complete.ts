// ─── DRAW COMPLETE (BARREL) ───────────────────────────────────────────────────
// Re-exports from split modules for backward compatibility.
// Contains removeLastVertex (not moved — tightly coupled to Geoman internals).

import { ctx } from "../core/state"

// ─── GEOMAN INTERNAL TYPES ─────────────────────────────────────────────────────
// Geoman's public API types are incomplete — these internal shapes document
// the properties we access at runtime. Defined here to avoid `as unknown as`
// casts throughout removeLastVertex.

interface GeomanInternalActionInstance {
  lineDrawer?: {
    featureData?: Record<string, unknown>
    shapeLngLats?: [number, number][]
    getFeatureGeoJson?: Function
    snappingHelper?: Record<string, unknown>
    snappingKey?: unknown
    setSnapping?: Function
    gm?: Record<string, unknown>
  }
}

interface GeomanInternal {
  actionInstances?: Record<string, GeomanInternalActionInstance | undefined>
  disableDraw?: Function
}

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

function geomanActionInstance(name: string): GeomanInternalActionInstance | undefined {
  return (ctx.geoman as unknown as GeomanInternal | null)?.actionInstances?.[name]
}

export async function removeLastVertex(): Promise<void> {
  const polygonInst = geomanActionInstance("draw__polygon")
  const lineInst = geomanActionInstance("draw__line")
  const drawInstance = polygonInst ?? lineInst
  const lineDrawer = drawInstance?.lineDrawer
  if (!lineDrawer?.featureData) return

  const coords: [number, number][] = lineDrawer.shapeLngLats ?? []
  if (coords.length <= 1) {
    const gm = ctx.geoman as unknown as GeomanInternal | null
    try {
      await (gm?.disableDraw as Function | undefined)?.()
    } catch {
      /* ignore */
    }
    return
  }
  coords.pop()

  const isPolygon = !!polygonInst
  const markersMap = lineDrawer.featureData?.markers as
    | Map<string, Record<string, unknown>>
    | undefined
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

  const markerPointer = (lineDrawer?.gm as Record<string, unknown> | undefined)?.markerPointer as
    | Record<string, unknown>
    | undefined
  const markerControl = markerPointer?.marker as maplibregl.Marker | undefined

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
