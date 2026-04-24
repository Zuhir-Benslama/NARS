// ─── DRAW EVENTS ──────────────────────────────────────────────────────────────
// Custom drawing event registration:
// - Phase watch → enable draw mode
// - gm:create → complete drawing with geometry
// - Right click → remove last vertex or cancel draw
// - Left click → restart draw mode
// - ESC → cancel draw or edit
// - Ctrl+Z → undo last deletion
// - Geoman marker pointer patch for snap integration

import { watch } from 'vue'
import { PHASES } from '../phases'
import { store, setSelectedFeature } from '../store'
import { ctx, updateSelectionHighlight, featuresStore } from './state'
import { showContextMenu, showMapContextMenu } from './context-menu'
import { GEOMETRY_CONFIG } from '../config'
import {
    enableCrosshair,
    enableSnapping,
    disableSnapping,
    installSnapInterceptors,
    getFrozenSnapPos,
    getActiveSnapPhases,
    findNearestSnap,
    mergeExternalSnapWithDrawFirstVertex,
} from './snapping'
import { buildDrawControl } from './draw-control'
import {
    setDrawingPhase,
    getDrawingPhase,
    completeDrawingWithGeometry,
    isSavingFeature,
    removeLastVertex,
    getFeatureStyle,
    setRepatchMarkerPointer,
    registerGeomanMarker,
} from './draw-complete'
import { isEditMode, enableEditMode, commitEditMode, cancelEditMode, suppressGeomanFill } from './edit-mode'
import { registerGeomanEvents } from './geoman-events'
import { undo } from './undo'
import { debugLog, debugError, debugWarn } from '../utils/debug'
import type { GeomanCreateEvent, ActionInstances } from './geoman-types'
import type { GeomanMarkerPointer } from './geoman-types'
import type { MapMouseEvent as MapLibreMapMouseEvent } from 'maplibre-gl'

// Re-export state and functions used by other modules
export { isEditMode, enableEditMode, commitEditMode, cancelEditMode, suppressGeomanFill, getFeatureStyle }

// ─── GEOMETRY HELPERS ─────────────────────────────────────────────────────────

function pointToSegmentDist(px: number, py: number, x1: number, y1: number, x2: number, y2: number): number {
    const dx = x2 - x1
    const dy = y2 - y1
    const lenSq = dx * dx + dy * dy
    if (lenSq === 0) return Math.sqrt((px - x1) ** 2 + (py - y1) ** 2)
    const t = Math.max(0, Math.min(1, ((px - x1) * dx + (py - y1) * dy) / lenSq))
    const nearX = x1 + t * dx
    const nearY = y1 + t * dy
    return Math.sqrt((px - nearX) ** 2 + (py - nearY) ** 2)
}

// ─── GEOMAN MARKER POINTER PATCH ──────────────────────────────────────────────
// Geoman's MarkerPointer.onMouseMove is the SINGLE entry point for cursor
// positioning during ALL drawing modes (polygon, line, circle, marker, etc).
//
// Flow:
//   1. MapLibre fires mousemove event
//   2. MarkerPointer.onMouseMove(e) is called (throttled)
//   3. It calls this.snappingHelper.getSnappedLngLat() IF snapping is on
//   4. It calls this.marker.setLngLat(snappedCoords)
//   5. All draw classes read marker.getLngLat() for vertex placement
//
// Problem: The snapping helper action instance (helper__snapping) is only
// active during explicit snapping helper mode, not during regular drawing.
// So getSnappedLngLat returns the raw coordinates unchanged.
//
// Solution: Monkey-patch MarkerPointer.onMouseMove to use our NARS snap logic
// directly, bypassing the snapping helper entirely. This is the earliest
// interception point — we control the marker position before any draw class
// reads it.

function patchGeomanMarkerPointerSnap(): void {
    const gm = ctx.geoman
    if (!gm?.markerPointer) {
        debugWarn('[SNAP] No markerPointer')
        return
    }

    const mp = gm.markerPointer as GeomanMarkerPointer

    // Flag to prevent double-patching
    if (mp._narsSnapPatched) return
    mp._narsSnapPatched = true

    const PATCH_TIMEOUT_MS = 15_000
    const startTime = performance.now()
    let rafId: number | null = null

    const tryPatch = () => {
        if (mp.marker && typeof mp.marker.setLngLat === 'function') {
            if (mp.marker._narsSnapPatchedInstance) {
                if (rafId !== null) cancelAnimationFrame(rafId)
                rafId = null
                return
            }

            const orig = mp.marker.setLngLat.bind(mp.marker)
            registerGeomanMarker(mp, mp.marker, orig)
            mp.marker._narsSnapPatchedInstance = true
            mp.marker.setLngLat = makeSnapSetLngLat(mp, orig)

            debugLog('[SNAP] marker setLngLat patched')
            if (rafId !== null) cancelAnimationFrame(rafId)
            rafId = null
            return
        }

        if (performance.now() - startTime > PATCH_TIMEOUT_MS) {
            debugWarn('[SNAP] Timed out waiting for Geoman marker — snapping disabled')
            if (rafId !== null) cancelAnimationFrame(rafId)
            rafId = null
            return
        }

        rafId = requestAnimationFrame(tryPatch)
    }

    rafId = requestAnimationFrame(tryPatch)

    debugLog('[SNAP] Snap patching started (rAF polling for marker)')
}

// ─── SNAP SET-LNG-LAT FACTORY ─────────────────────────────────────────────────
// Shared factory for the snap-aware setLngLat override applied to Geoman's
// markerPointer.marker. Both the initial patch (patchGeomanMarkerPointerSnap)
// and the re-patch after draw reset (repatchMarkerPointer) use identical logic.
// Centralising here means a future change to snap behaviour only needs one edit.

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeSnapSetLngLat(
    mp: GeomanMarkerPointer,
    orig: (...args: any[]) => void,
): (lngLat: [number, number] | { lng: number; lat: number; toArray?(): [number, number] }) => void {
    return function (lngLat) {
        const rawPair = Array.isArray(lngLat)
            ? lngLat
            : (lngLat.toArray?.() ?? [lngLat.lng ?? 0, lngLat.lat ?? 0])
        const lng0 = Number(rawPair[0])
        const lat0 = Number(rawPair[1])
        const rawPx = ctx.map.project([lng0, lat0] as [number, number])

        const frozen = getFrozenSnapPos()
        if (frozen) {
            orig.call(mp.marker!, [frozen.lng, frozen.lat])
            return
        }

        const phases = getActiveSnapPhases()
        const project = (ll: [number, number]) => ctx.map.project(ll)
        const external = phases.length > 0 ? findNearestSnap(rawPx.x, rawPx.y, phases, true) : null
        const snap = mergeExternalSnapWithDrawFirstVertex(rawPx.x, rawPx.y, external, project)
        if (snap) {
            orig.call(mp.marker!, [snap.lng, snap.lat])
        } else {
            orig.call(mp.marker!, lngLat)
        }
    }
}

// ─── REGISTRATION ─────────────────────────────────────────────────────────────

export function registerDrawEvents(): void {
    const map = ctx.map

    debugLog('Registering custom draw events')

    // Wire up the marker re-patch callback so draw-complete can re-patch
    // the fresh Geoman marker after resetDrawMode creates a new one.
    setRepatchMarkerPointer(repatchMarkerPointer)

    // Fix #4 — Vue watch keeps drawType in sync with the active phase reactively
    watchDrawType()

    // Drawing is handled by Geoman's native enableDraw() (like nars-vite reference).
    // Geoman forwards the create event to MapLibre as 'gm:create'.
    map.on('gm:create', async (e: GeomanCreateEvent) => {
        if (isSavingFeature()) return

        const featureData = e.featureData || e.feature
        if (!featureData) return
        const shape = e.shape || (featureData as { shape?: string }).shape

        debugLog('Geoman created feature:', shape, featureData)

        const geoJson = featureData.getGeoJson?.() || featureData._geoJson
        if (!geoJson?.geometry) return

        // Map Geoman shape names back to NARS drawType
        const shapeToDrawType: Record<string, string> = {
            polygon: 'polygon',
            line: 'line',
            marker: 'marker',
            circle: 'circle',
        }
        const drawingPhase = getDrawingPhase()
        const narsDrawType = (shape ? shapeToDrawType[shape] : undefined) ?? drawingPhase?.drawType ?? 'polygon'

        let geometry = geoJson.geometry

        // Circle: convert Polygon to Point with radius
        if (shape === 'circle' && geoJson.geometry.type === 'Polygon') {
            const coords = geoJson.geometry.coordinates[0] as [number, number][]
            debugLog('[CIRCLE DRAW] Converting circle Polygon to Point with radius, coords count:', coords.length)

            if (coords.length >= 3) {
                let sumLat = 0,
                    sumLng = 0
                for (const [lng, lat] of coords) {
                    sumLat += lat
                    sumLng += lng
                }
                const centerLat = sumLat / coords.length
                const centerLng = sumLng / coords.length
                let totalDist = 0
                for (const [lng, lat] of coords) {
                    const dlat = ((lat - centerLat) * Math.PI) / 180
                    const dlng = ((lng - centerLng) * Math.PI) / 180
                    const a =
                        Math.sin(dlat / 2) ** 2 +
                        Math.cos((centerLat * Math.PI) / 180) *
                            Math.cos((lat * Math.PI) / 180) *
                            Math.sin(dlng / 2) ** 2
                    totalDist += GEOMETRY_CONFIG.earthRadiusMeters * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
                }
                const radius = totalDist / coords.length
                debugLog('[CIRCLE DRAW] Circle center:', centerLat, centerLng, 'radius:', radius, 'meters')

                geometry = { type: 'Point', coordinates: [centerLng, centerLat] } as GeoJSON.Point
                ;(geometry as GeoJSON.Point & { radius: number }).radius = radius
            } else {
                debugError('[CIRCLE DRAW] Circle has too few coordinates:', coords.length)
            }
        } else if (shape === 'polygon' && geoJson.geometry.type === 'MultiPolygon') {
            // Geoman can produce MultiPolygon when the user draws a self-intersecting
            // shape or creates multiple rings. Flatten to single Polygon by taking
            // the first (largest) ring — this matches the backend's expectation.
            const mp = geoJson.geometry as GeoJSON.MultiPolygon
            if (mp.coordinates.length > 0 && mp.coordinates[0].length > 0) {
                geometry = {
                    type: 'Polygon',
                    coordinates: mp.coordinates[0],
                } as unknown as GeoJSON.Polygon
            }
        }

        try {
            await completeDrawingWithGeometry(
                geometry as GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon,
                narsDrawType,
                featureData,
            )
        } catch (err) {
            debugError('[GM:CREATE] Error:', err)
        }
    })

    // RIGHT CLICK — during polygon/line drawing, remove last vertex or cancel draw.
    // Otherwise: show context menu.
    // Listened on window during capture phase to beat Geoman / MapLibre.
    window.addEventListener(
        'contextmenu',
        (e: MouseEvent) => {
            const mapEl = ctx.map.getContainer()
            if (!mapEl.contains(e.target as Node)) return
            if (isEditMode) {
                e.preventDefault()
                void commitEditMode()
                return
            }
            const actionInstances = ctx.geoman?.actionInstances as ActionInstances | undefined
            const polygonInst = actionInstances?.['draw__polygon']
            const lineInst = actionInstances?.['draw__line']
            const drawInstance = polygonInst ?? lineInst
            const lineDrawer = drawInstance?.lineDrawer
            const midDraw = lineDrawer?.shapeLngLats && lineDrawer.shapeLngLats.length > 0
            if (midDraw) {
                e.preventDefault()
                e.stopPropagation()
                const coords: [number, number][] = lineDrawer.shapeLngLats
                if (coords.length <= 1) {
                    const phase = PHASES[store.currentPhase]
                    void ctx.geoman!.disableDraw().then(() => {
                        if (phase && phase.key !== 'namingPanels') buildDrawControl(phase)
                    })
                    return
                }
                void removeLastVertex()
                return
            }
            // Not drawing — show context menu
            e.preventDefault()
            e.stopImmediatePropagation()
            const phase = PHASES[store.currentPhase]
            if (!phase) return

            const rect = ctx.map.getContainer().getBoundingClientRect()
            const px = e.clientX - rect.left
            const py = e.clientY - rect.top

            // Try queryRenderedFeatures first
            const features = ctx.map.queryRenderedFeatures([px, py] as [number, number])
            let feature
            if (phase.key === 'cityCenter') {
                feature = features.find((f) => f.source === 'features' && f.properties?.phaseKey === 'cityCenter')
            } else {
                feature = features.find((f) => f.source === 'features' && f.properties?.dbId)
            }
            if (feature && feature.properties?.dbId && feature.properties?.phaseKey) {
                showContextMenu(e.clientX, e.clientY, feature.properties.dbId, feature.properties.phaseKey)
            } else {
                // Fallback: find nearest road/feature from featuresStore
                const allFeatures = featuresStore.getAll()
                let nearestDbId: string | null = null
                let nearestPhaseKey: string | null = null
                let nearestDist = 20 // pixels threshold

                for (const f of allFeatures) {
                    const fPhaseKey = f.properties?.phaseKey
                    const fDbId = f.properties?.dbId
                    if (!fDbId || !fPhaseKey) continue

                    // Check if feature is visible in current phase
                    if (fPhaseKey === 'roads' || fPhaseKey === 'houseEntrances') {
                        // For points (entrances)
                        if (f.geometry.type === 'Point') {
                            const point = ctx.map.project([f.geometry.coordinates[0], f.geometry.coordinates[1]])
                            const dist = Math.sqrt((point.x - px) ** 2 + (point.y - py) ** 2)
                            if (dist < nearestDist) {
                                nearestDist = dist
                                nearestDbId = fDbId
                                nearestPhaseKey = fPhaseKey
                            }
                        }
                        // For lines (roads) - check if click is near the line
                        if (f.geometry.type === 'LineString') {
                            const coords = f.geometry.coordinates
                            for (let i = 0; i < coords.length - 1; i++) {
                                const p1 = ctx.map.project([coords[i][0], coords[i][1]])
                                const p2 = ctx.map.project([coords[i + 1][0], coords[i + 1][1]])
                                const dist = pointToSegmentDist(px, py, p1.x, p1.y, p2.x, p2.y)
                                if (dist < nearestDist) {
                                    nearestDist = dist
                                    nearestDbId = fDbId
                                    nearestPhaseKey = fPhaseKey
                                }
                            }
                        }
                    }
                }

                if (nearestDbId && nearestPhaseKey) {
                    showContextMenu(e.clientX, e.clientY, nearestDbId, nearestPhaseKey)
                } else {
                    showMapContextMenu(e.clientX, e.clientY, phase)
                }
            }
        },
        true,
    )

    // LEFT CLICK — select feature or restart draw mode
    // Clicking on a feature selects it for editing; clicking on empty space
    // clears selection and re-enables draw mode.
    map.on('click', (e: MapLibreMapMouseEvent & { point: { x: number; y: number } }) => {
        if (isEditMode) return
        // If Geoman draw mode is active, do nothing (user is actively drawing)
        if (ctx.geoman && ctx.geoman.getActiveDrawModes?.().length > 0) return

        const phase = PHASES[store.currentPhase]
        const features = map.queryRenderedFeatures(e.point)

        // When current phase is cityCenter, prioritize cityCenter features
        // because clicking inside the circle ring may hit area polygons underneath
        let feature
        if (phase?.key === 'cityCenter') {
            feature = features.find((f) => f.source === 'features' && f.properties?.phaseKey === 'cityCenter')
            // If no city center found, don't select anything (don't select areas)
        } else {
            feature = features.find((f) => f.source === 'features')
        }

        if (feature) {
            // User clicked on a feature — select it
            const dbId = feature.properties?.dbId
            if (dbId) {
                setSelectedFeature(dbId)
                updateSelectionHighlight(dbId)
                debugLog('[SELECT] Selected feature:', dbId)
            }
        } else {
            // User clicked on empty space — clear selection
            setSelectedFeature(null)
            updateSelectionHighlight(null)
            // Re-enable draw mode for the current phase
            const phase = PHASES[store.currentPhase]
            if (phase && phase.key !== 'namingPanels') {
                buildDrawControl(phase)
            }
        }
    })

    // ESC — cancel draw or edit (capture phase so we beat Geoman / modal focus).
    document.addEventListener(
        'keydown',
        (e: KeyboardEvent) => {
            if (e.key !== 'Escape') return
            // If modal is visible, let it handle ESC — don't interfere.
            if (store.modal.visible) return

            const drawing = (ctx.geoman?.getActiveDrawModes?.().length ?? 0) > 0
            if (drawing) {
                e.preventDefault()
                e.stopImmediatePropagation()
                // Cancel draw and re-enable for the current phase so user can draw again
                const phase = PHASES[store.currentPhase]
                void ctx.geoman!.disableDraw().then(() => {
                    if (phase && phase.key !== 'namingPanels') {
                        buildDrawControl(phase)
                    }
                })
                return
            }
            if (isEditMode) {
                e.preventDefault()
                e.stopImmediatePropagation()
                void cancelEditMode()
            }
        },
        true,
    )

    // Ctrl+Z — restore last deleted feature
    document.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'z' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault()
            undo()
        }
    })

    // ── CUSTOM SNAPPING: Patch Geoman MarkerPointer ──────────────────────────
    // Geoman's MarkerPointer uses its own internal snapping helper. We disable it
    // and patch the mousemove handler to use our custom NARS snap logic instead.
    // This ensures BOTH the visual crosshair cursor and the created geometry use
    // snapped coordinates.
    patchGeomanMarkerPointerSnap()

    // Also patch map events so any other code reading e.lngLat gets snapped coords.
    installSnapInterceptors()

    // ── GEOMAN EVENTS: vertex drag, editend, remove ──────────────────────────
    registerGeomanEvents()
}

// ─── RE-PATCH MARKER AFTER DRAW RESET ─────────────────────────────────────────
// Called by draw-complete.ts after resetDrawMode creates a fresh Geoman marker.
// The previous marker was destroyed by disableDraw(), so the new one needs
// the setLngLat snap patch applied again.

let _patchRafId: number | null = null

export function repatchMarkerPointer(): void {
    const gm = ctx.geoman
    if (!gm?.markerPointer) return
    const mp = gm.markerPointer as GeomanMarkerPointer
    if (!mp) return

    // Cancel any previous rAF loop so we don't have multiple polling loops
    if (_patchRafId !== null) {
        cancelAnimationFrame(_patchRafId)
        _patchRafId = null
    }

    const PATCH_TIMEOUT_MS = 5_000
    const startTime = performance.now()

    const tryPatch = () => {
        if (mp.marker && typeof mp.marker.setLngLat === 'function') {
            if (mp.marker._narsSnapPatchedInstance) return

            const orig = mp.marker.setLngLat.bind(mp.marker)
            registerGeomanMarker(mp, mp.marker, orig)
            mp.marker._narsSnapPatchedInstance = true
            mp.marker.setLngLat = makeSnapSetLngLat(mp, orig)

            debugLog('[SNAP] marker re-patched after draw reset')
            _patchRafId = null
            return
        }

        if (performance.now() - startTime > PATCH_TIMEOUT_MS) {
            debugWarn('[SNAP] Timed out waiting for marker after draw reset')
            _patchRafId = null
            return
        }

        _patchRafId = requestAnimationFrame(tryPatch)
    }

    _patchRafId = requestAnimationFrame(tryPatch)
}

// ─── FIX #4: REACTIVE DRAW TYPE ───────────────────────────────────────────────

function watchDrawType() {
    watch(
        () => store.currentPhase,
        (phaseIdx) => {
            // Always sync phase even if a draw mode is active.
            // Otherwise _drawingPhase can drift (e.g. still "publicBuildings"
            // while UI is in "houseEntrances"), causing wrong modal/geometry behavior.
            const activeDrawModes = ctx.geoman?.getActiveDrawModes?.() || []
            if (activeDrawModes.length > 0) {
                debugWarn('[WATCH] Phase changed while draw mode is active; forcing mode sync')
            }

            const phase = PHASES[phaseIdx]
            if (phase) {
                setDrawingPhase(phase)
                buildDrawControl(phase)

                // City center: fly camera to user's location when phase is selected
                if (phase.key === 'cityCenter') {
                    const map = ctx.map

                    // Use requestAnimationFrame to ensure map is ready
                    requestAnimationFrame(() => {
                        const userLat = store.user?.commune?.latitude
                        const userLng = store.user?.commune?.longitude

                        if (userLat && userLng) {
                            debugLog('[CITY CENTER] Flying to user location:', userLat, userLng)
                            map.flyTo({
                                center: [userLng, userLat],
                                zoom: 16,
                                duration: 1500,
                                essential: true,
                            })
                        } else if (store.cityCenterLatLng) {
                            // Fly to existing city center if one exists
                            debugLog('[CITY CENTER] Flying to existing city center:', store.cityCenterLatLng)
                            map.flyTo({
                                center: [store.cityCenterLatLng.lng, store.cityCenterLatLng.lat],
                                zoom: 17,
                                duration: 1500,
                                essential: true,
                            })
                        }
                    })
                }
            }
            // Show crosshair and enable snapping as soon as a drawing phase is active.
            if (!isEditMode) {
                enableCrosshair()
                disableSnapping()
                enableSnapping()
            }
        },
        { immediate: true },
    )
}

// ─── HMR CLEANUP ─────────────────────────────────────────────────────────────
// Cancel orphaned rAF loops when the module is hot-replaced during development.

if (import.meta.hot) {
    import.meta.hot.dispose(() => {
        if (_patchRafId !== null) {
            cancelAnimationFrame(_patchRafId)
            _patchRafId = null
        }
    })
}
