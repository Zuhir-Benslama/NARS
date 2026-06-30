// ─── DRAW COMPLETE ───────────────────────────────────────────────────────────
// Re-exports from split modules for backward compatibility.
// Contains removeLastVertex (not moved — tightly coupled to Geoman internals).

import { ctx } from "../core/state"
import { asGeomanInternal } from "../core/geoman-types"
import type { GeomanActionInstance } from "../core/geoman-types"

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

function geomanActionInstance(name: string): GeomanActionInstance | undefined {
  return asGeomanInternal(ctx.geoman)?.actionInstances?.[name]
}

export async function removeLastVertex(): Promise<void> {
  const polygonInst = geomanActionInstance("draw__polygon")
  const lineInst = geomanActionInstance("draw__line")
  const drawInstance = polygonInst ?? lineInst
  const lineDrawer = drawInstance?.lineDrawer
  if (!lineDrawer?.featureData) return

  const coords: [number, number][] = lineDrawer.shapeLngLats ?? []
  if (coords.length <= 1) {
    const gm = asGeomanInternal(ctx.geoman)
    try {
      await gm?.disableDraw?.()
    } catch {
      /* ignore */
    }
    return
  }
  coords.pop()

  const isPolygon = !!polygonInst
  const markersMap = lineDrawer.featureData?.markers
  if (markersMap) {
    const entries = Array.from(markersMap.entries())
    if (entries.length > 0) {
      const [key, markerData] = entries[entries.length - 1]
      markerData?.instance?.remove?.()
      markersMap.delete(key)
    }
  }

  const markerControl = lineDrawer.gm?.markerPointer?.marker ?? undefined

  if (isPolygon) {
    const ring: [number, number][] = [...coords]
    if (markerControl) {
      const ll = markerControl.getLngLat()
      ring.push([ll.lng, ll.lat])
    }
    if (ring.length > 0) {
      ring.push([ring[0][0], ring[0][1]])
    }

    await lineDrawer.featureData?.updateGeometry?.({
      type: "Polygon",
      coordinates: [ring],
    })

    await lineDrawer.featureData?.convertToPolygon?.()

    if (markerControl) {
      await lineDrawer.featureData?.fireUpdateEvent?.(lineDrawer.featureData, {
        type: "dom",
        instance: markerControl,
        position: {
          coordinate: [markerControl.getLngLat().lng, markerControl.getLngLat().lat],
          path: ["geometry", "coordinates", String(coords.length)],
        },
      })
    }
  } else {
    const geoJson = lineDrawer.getFeatureGeoJson?.({ withControlMarker: true })
    if (geoJson) {
      await lineDrawer.featureData?.updateGeometry?.(geoJson.geometry)
    }
    if (markerControl) {
      await lineDrawer.featureData?.fireUpdateEvent?.(lineDrawer.featureData, {
        type: "dom",
        instance: markerControl,
        position: {
          coordinate: [markerControl.getLngLat().lng, markerControl.getLngLat().lat],
          path: ["geometry", "coordinates", String(coords.length)],
        },
      })
    }
  }

  lineDrawer.snappingHelper?.setCustomSnappingCoordinates?.(lineDrawer.snappingKey, coords)
  lineDrawer.setSnapping?.()
}
