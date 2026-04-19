// ─── DRAW COMPLETE HANDLER ────────────────────────────────────────────────────
// Called when Geoman's native draw mode finishes a shape.
// Handles validation, modal open, database save, and render layer update.

import { PHASES } from '../phases'
import { syncCounts, openModal, store } from '../store'
import { useLayerStore } from '../stores/layerStore'
import type { LayerState } from '../stores/layerStore'
import { ctx, featuresStore } from './state'
import { areaStyle } from './styles'
import { buildFeatureData, saveToDatabase, prepareModalExtras } from './features'
import { getFeatureType } from './house-numbering'
import { buildDrawControl } from './draw-control'
import { refreshLayerVisibility } from './labels'
import { computeCircleRing, computeCircleRadius } from './geometry'
import { showToast } from '../toast'
import { debugError } from '../utils/debug'
import { getRoadSide } from '../validation'
import { t } from '../i18n'
import type { LayerEntry, ModalResult } from '../types'
import { updateEndpointMarkers } from './road-directions'

// Re-patch function imported lazily to avoid circular dependency with draw-events.
// Set by draw-events after it initializes.
let _repatchMarkerPointer: (() => void) | null = null
export function setRepatchMarkerPointer(fn: () => void): void {
    _repatchMarkerPointer = fn
}

// Also export for direct use by edit-mode.ts (avoids circular import via draw-events).
export function repatchMarker(): void {
    _repatchMarkerPointer?.()
}

// Store reference to the original Geoman marker setLngLat for snapping toggle.
/* eslint-disable @typescript-eslint/no-explicit-any */
let _geomanMarkerPointer: any = null
let _originalGeomanMarkerSetLngLat: ((...args: any[]) => void) | null = null
let _snappingEnabled = true // Starts enabled

/** Store the original Geoman marker setLngLat for later restoration. */
export function registerGeomanMarker(mp: any, _marker: any, orig: (...args: any[]) => void): void {
    _geomanMarkerPointer = mp
    _originalGeomanMarkerSetLngLat = orig
}
/* eslint-enable @typescript-eslint/no-explicit-any */

/** Restore the original Geoman marker setLngLat (disable snap patching). */
export function unpatchGeomanMarker(): void {
    _snappingEnabled = false
    if (_geomanMarkerPointer?.marker && _originalGeomanMarkerSetLngLat) {
        _geomanMarkerPointer.marker.setLngLat = _originalGeomanMarkerSetLngLat
        /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
        ;(_geomanMarkerPointer.marker as any)._narsSnapPatchedInstance = false
    }
}

/** Check if snapping is currently enabled on the Geoman marker. */
export function isSnappingEnabled(): boolean {
    return _snappingEnabled
}

/** Set the snapping enabled state (used by snapping.ts toggle). */
export function setSnappingEnabled(v: boolean): void {
    _snappingEnabled = v
}

// Draw phase is shared with draw-events.ts via module-level state
// It's set by watchDrawType and read here for style/save context.
let _drawingPhase: (typeof PHASES)[number] | null = null
export function setDrawingPhase(phase: (typeof PHASES)[number] | null): void {
    _drawingPhase = phase
}
export function getDrawingPhase(): (typeof PHASES)[number] | null {
    return _drawingPhase
}

// ─── SAVE GUARD ───────────────────────────────────────────────────────────────

let savingFeature = false
export function isSavingFeature(): boolean {
    return savingFeature
}

// ─── GEOMETRY NORMALIZATION ───────────────────────────────────────────────────

/**
 * Normalize GeoJSON geometry to match the drawing phase's expected type.
 * Geoman Free can produce Polygon even in 'line' mode — this forces the correct
 * GeoJSON type so roads render as LineString strokes, not filled polygons.
 */
function normalizeGeometry(
    geometry: GeoJSON.Geometry,
    drawType: string,
): GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon {
    if (drawType === 'polyline' && geometry.type === 'Polygon') {
        // Geoman produced a Polygon for a polyline — extract first ring as LineString
        const ring = geometry.coordinates[0].map((c) => [c[0], c[1]] as [number, number])
        return { type: 'LineString', coordinates: ring }
    }
    if (drawType === 'polygon' && geometry.type === 'LineString') {
        // LineString for a polygon phase — close the ring and wrap as Polygon
        const ring = geometry.coordinates.map((c) => [c[0], c[1]] as [number, number])
        const first = ring[0],
            last = ring[ring.length - 1]
        if (first[0] !== last[0] || first[1] !== last[1]) {
            ring.push([first[0], first[1]])
        }
        return { type: 'Polygon', coordinates: [ring] }
    }
    return geometry as GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
}

// ─── DRAW COMPLETE ────────────────────────────────────────────────────────────

export async function completeDrawingWithGeometry(
    geometry: GeoJSON.Geometry,
    narsDrawType: string,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    geomanFeatureData: any,
): Promise<void> {
    if (!_drawingPhase) return

    // City center: enforce 0-1 constraint — only one city center per area
    if (_drawingPhase.key === 'cityCenter') {
        const layerStore = useLayerStore()
        const state = layerStore.$state as LayerState
        const existingCityCenters = state.cityCenter?.length ?? 0
        if (existingCityCenters > 0) {
            showToast('A city center already exists. Delete it first to create a new one.', 'error')
            try {
                geomanFeatureData?.delete?.()
            } catch {
                /* already gone */
            }
            return
        }
    }

    // Validate geometry before proceeding — prevents saving degenerate shapes.
    if (geometry.type === 'LineString' && geometry.coordinates.length < 2) {
        showToast('Road must have at least 2 points.', 'error')
        try {
            geomanFeatureData?.delete?.()
        } catch {
            /* already gone */
        }
        return
    }
    if (geometry.type === 'Polygon' && (!geometry.coordinates[0] || geometry.coordinates[0].length < 3)) {
        showToast('Area must have at least 3 points.', 'error')
        try {
            geomanFeatureData?.delete?.()
        } catch {
            /* already gone */
        }
        return
    }
    // City center validation — handle both Point (with radius) and Polygon (degenerate circle)
    if (_drawingPhase.key === 'cityCenter') {
        let radius = (geometry as { radius?: number }).radius
        // If Geoman produced a Polygon instead of a Point (degenerate circle),
        // compute the radius from the polygon's bounds
        if (geometry.type === 'Polygon' && geometry.coordinates[0]?.length >= 3) {
            const coords = geometry.coordinates[0] as [number, number][]
            let sumLat = 0,
                sumLng = 0
            for (const [lng, lat] of coords) {
                sumLat += lat
                sumLng += lng
            }
            const centerLat = sumLat / coords.length
            const centerLng = sumLng / coords.length
            radius = computeCircleRadius(centerLat, centerLng, coords)
        }
        if (!radius || radius < 5) {
            showToast('City center radius is too small (minimum 5 meters).', 'error')
            try {
                geomanFeatureData?.delete?.()
            } catch {
                /* already gone */
            }
            return
        }
        if (radius > 50000) {
            showToast('City center radius is too large (maximum 50 km).', 'error')
            try {
                geomanFeatureData?.delete?.()
            } catch {
                /* already gone */
            }
            return
        }
    }

    const featureId = crypto.randomUUID()

    // Hide Geoman's vertex handles immediately by disabling draw mode before modal opens.
    // This prevents the "dots" (vertex markers) from being visible while the user fills the modal.
    const gm = ctx.geoman
    if (gm) {
        try {
            await gm.disableDraw()
        } catch {
            /* ignore */
        }
    }

    const modalResult = await openModalForFeature(_drawingPhase, featureId, geometry)
    if (!modalResult) {
        // User cancelled — remove the Geoman-drawn shape and re-enable draw mode.
        try {
            geomanFeatureData?.delete?.()
        } catch {
            // Feature may already have been removed by user action
        }
        // Re-enable draw mode so the user can try again.
        if (_drawingPhase) {
            buildDrawControl({
                key: _drawingPhase.key,
                drawType: _drawingPhase.drawType,
                color: _drawingPhase.color,
            })
            _repatchMarkerPointer?.()
        }
        return
    }

    savingFeature = true
    try {
        const featureData = buildFeatureData(geometry as GeoJSON.Geometry, _drawingPhase, modalResult)
        const saveResult = await saveToDatabase(featureData)
        if (!saveResult.ok) {
            showToast('Save failed: ' + (saveResult.error ?? 'Please try again.'), 'error')
            return
        }

        const dbId = saveResult.data!.id
        let style = getFeatureStyle(_drawingPhase, modalResult)

        // Normalize geometry type to match the drawing phase — Geoman Free can
        // produce Polygon even in 'line' mode, causing roads to render as filled
        // polygons instead of strokes. Force the correct GeoJSON type.
        let storeGeometry: GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon = normalizeGeometry(
            geometry as GeoJSON.Geometry,
            _drawingPhase.drawType,
        )

        // City center: store as LineString circle ring (simple outline, no fill)
        if (_drawingPhase.key === 'cityCenter' && featureData.radius) {
            const ring = computeCircleRing(featureData.lat!, featureData.lng!, featureData.radius)
            // Close the ring for proper rendering
            ring.push([ring[0][0], ring[0][1]])
            storeGeometry = { type: 'LineString', coordinates: ring }
            // Create clean style object for line rendering (no fill properties)
            style = {
                lineColor: '#e74c3c',
                lineWidth: 6,
                textColor: '#333333',
                radius: featureData.radius,
            }
        }

        // Single renderer: featuresStore → NARS GeoJSON source → nars-* layers
        featuresStore.add({
            id: featureId,
            geometry: storeGeometry,
            properties: {
                dbId,
                phaseKey: _drawingPhase.key,
                label: featureData.label,
                geomType: storeGeometry.type,
                ...style,
            },
        })

        const layerEntry: LayerEntry = {
            id: featureId,
            dbId,
            data: featureData,
            type: getFeatureType(narsDrawType),
        }
        const layerStore = useLayerStore()
        const phaseKey = _drawingPhase.key as keyof LayerState
        ;(layerStore.$state[phaseKey] as LayerEntry[]).push(layerEntry)

        syncCounts()
        refreshLayerVisibility()

        // Update road endpoint markers automatically
        if (_drawingPhase.key === 'roads') {
            updateEndpointMarkers()
        }

        showToast('Feature saved.', 'success')

        // Remove the Geoman-drawn shape from the map after a short delay.
        setTimeout(() => {
            try {
                geomanFeatureData?.delete?.()
            } catch {
                /* already gone */
            }
        }, 100)
    } catch (err) {
        debugError('[COMPLETE] Save error:', err)
        showToast('Save failed: ' + (err as Error).message, 'error')
    } finally {
        savingFeature = false
        // Reset draw mode so Geoman's internal state is clean for the next feature.
        setTimeout(() => resetDrawMode(), 200)
    }
}

// ─── DRAW MODE RESET ─────────────────────────────────────────────────────────

/**
 * Disable and re-enable the current draw mode to clear Geoman's internal state.
 * This removes any leftover markers, line drawer, or control markers from the
 * previous feature, giving the user a clean slate for the next drawing.
 *
 * Called from finally block without await — designed to be fire-and-forget.
 */
async function resetDrawMode(): Promise<void> {
    const gm = ctx.geoman
    const phase = _drawingPhase
    if (!gm || !phase) return

    try {
        await gm.disableDraw()
    } catch {
        // ignore
    }
    // buildDrawControl handles the 50ms settle delay internally
    buildDrawControl({
        key: phase.key,
        drawType: phase.drawType,
        color: phase.color,
    })
    // Re-patch the new marker so snapping works for the next draw
    _repatchMarkerPointer?.()
}

// ─── MODAL HELPERS ────────────────────────────────────────────────────────────

async function openModalForFeature(
    phase: (typeof PHASES)[number],
    featureId: string,
    geometry: GeoJSON.Geometry,
): Promise<ModalResult | null> {
    // House entrances are reference-driven and do not use manual modal input.
    if (phase.key === 'houseEntrances') {
        const layerStore = useLayerStore()
        const state = layerStore.$state as LayerState

        if (store.referenceEntranceDbId != null) {
            const mainEntry = (state.houseEntrances || []).find((e) => e.dbId === store.referenceEntranceDbId)
            if (!mainEntry) {
                showToast(t('alert_ref_entrance_not_found'), 'error')
                return null
            }

            const bisCount = (state.houseEntrances || []).filter(
                (e) =>
                    e.data.entranceTypeKey === 'secondary_entrance' &&
                    e.data.mainEntranceDbId === store.referenceEntranceDbId,
            ).length
            const bisNumber = bisCount + 1

            return {
                label: 'BIS' + String(bisNumber).padStart(2, '0'),
                decisionNumber: '',
                decisionDate: '',
                entranceTypeKey: 'secondary_entrance',
                mainEntranceDbId: store.referenceEntranceDbId,
                mainEntranceLabel: mainEntry.data.label,
                bisNumber,
            }
        }

        if (store.referenceRoadDbId != null) {
            const roadEntry = (state.roads || []).find((r) => r.dbId === store.referenceRoadDbId)
            if (!roadEntry) {
                showToast(t('alert_ref_road_not_found'), 'error')
                return null
            }

            let side: 'left' | 'right' = 'left'
            if (geometry.type === 'Point') {
                const lat = geometry.coordinates[1]
                const lng = geometry.coordinates[0]
                const sideResult = await getRoadSide(store.referenceRoadDbId, lat, lng)
                side = sideResult?.side ?? 'left'
            }

            return {
                label: '?',
                decisionNumber: '',
                decisionDate: '',
                entranceTypeKey: 'main_entrance',
                roadDbId: store.referenceRoadDbId,
                roadLabel: roadEntry.data.label,
                side,
                entranceNumber: undefined,
            }
        }

        showToast(t('alert_no_reference_set'), 'error')
        return null
    }

    await prepareModalExtras(phase)

    // Extract radius from circle geometry for city center
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    const radius = phase.key === 'cityCenter' && geometry.type === 'Point' ? ((geometry as any).radius ?? null) : null

    return openModal(phase.index, featureId, radius ? { radius } : undefined)
}

// ─── FEATURE STYLE ────────────────────────────────────────────────────────────

export function getFeatureStyle(phase: (typeof PHASES)[number], modalResult: ModalResult): Record<string, unknown> {
    const style: Record<string, unknown> = {
        fillColor: phase.color,
        fillOpacity: 0.1,
        lineColor: phase.color,
        lineWidth: 2,
        circleColor: phase.color,
        circleRadius: 8,
        textColor: '#333333',
    }

    if (phase.key === 'areas') {
        const s = areaStyle(modalResult.areaTypeKey ?? 'central_urban')
        style.fillColor = s.lineColor
        style.fillOpacity = 0
        style.lineColor = s.lineColor
        style.lineWidth = s.lineWidth
    } else if (phase.key === 'districts') {
        style.fillColor = '#f39c12'
        style.fillOpacity = 0
        style.lineColor = '#f39c12'
        style.lineWidth = 3
    } else if (phase.key === 'publicBuildings') {
        style.fillColor = '#e67e22'
        style.fillOpacity = 0.25
        style.lineColor = '#e67e22'
        style.lineWidth = 3
    } else if (phase.key === 'publicSpaces') {
        style.fillColor = '#2ecc71'
        style.fillOpacity = 0.2
        style.lineColor = '#2ecc71'
        style.lineWidth = 3
    } else if (phase.drawType === 'polyline') {
        style.lineColor = phase.color
        style.lineWidth = 8
        delete style.fillColor
        delete style.fillOpacity
    } else if (phase.key === 'houseEntrances') {
        style.circleColor = phase.color
        style.circleRadius = 10
        style.textColor = '#000000'
    }
    // Note: city center style is replaced in completeDrawingWithGeometry with a clean
    // LineString style (no fillColor/fillOpacity). See line ~140 for the replacement.

    return style
}

// ─── REMOVE LAST VERTEX ───────────────────────────────────────────────────────

/** Remove the last vertex from the in-progress polygon or line during drawing. */
/* eslint-disable @typescript-eslint/no-explicit-any */
export async function removeLastVertex(): Promise<void> {
    const gm = ctx.geoman as any
    if (!gm) return

    const polygonInst = gm.actionInstances?.['draw__polygon']
    const lineInst = gm.actionInstances?.['draw__line']
    const drawInstance = polygonInst ?? lineInst
    const lineDrawer = drawInstance?.lineDrawer
    if (!lineDrawer?.featureData) return

    const coords: [number, number][] = lineDrawer.shapeLngLats
    // shapeLngLats contains only placed vertices (not the control marker)
    // With only 1 vertex placed, cancel the entire draw instead of removing it
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
            type: 'Polygon',
            coordinates: [ring],
        })

        if (lineDrawer.featureData.convertToPolygon) {
            await lineDrawer.featureData.convertToPolygon()
        }

        if (controlMarker && lineDrawer.fireUpdateEvent) {
            await lineDrawer.fireUpdateEvent(lineDrawer.featureData, {
                type: 'dom',
                instance: controlMarker,
                position: {
                    coordinate: [controlMarker.getLngLat().lng, controlMarker.getLngLat().lat],
                    path: ['geometry', 'coordinates', coords.length],
                },
            })
        }
    } else {
        await lineDrawer.featureData.updateGeometry(lineDrawer.getFeatureGeoJson({ withControlMarker: true }).geometry)
        if (controlMarker && lineDrawer.fireUpdateEvent) {
            await lineDrawer.fireUpdateEvent(lineDrawer.featureData, {
                type: 'dom',
                instance: controlMarker,
                position: {
                    coordinate: [controlMarker.getLngLat().lng, controlMarker.getLngLat().lat],
                    path: ['geometry', 'coordinates', coords.length],
                },
            })
        }
    }

    lineDrawer.snappingHelper?.setCustomSnappingCoordinates?.(lineDrawer.snappingKey, coords)
    if (typeof lineDrawer.setSnapping === 'function') {
        lineDrawer.setSnapping()
    }
}
/* eslint-enable @typescript-eslint/no-explicit-any */
