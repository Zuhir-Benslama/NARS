// ─── EDIT MODE ────────────────────────────────────────────────────────────────
// Geoman only shows vertex handles for features imported into its internal store.
// We import the single target feature on demand, enable edit mode, then call
// .delete() on the Geoman FeatureData when done — keeping NARS's featuresStore
// as the single source of truth for rendering.

import { apiFetch } from '../api'
import { setSelectedFeature } from '../store'
import { useLayerStore } from '../stores/layerStore'
import type { LayerState } from '../stores/layerStore'
import { ctx, featuresStore, updateSelectionHighlight } from './state'
import {
    enableCrosshair,
    disableCrosshair,
    disableSnapping,
    enableSnapping,
    snapPointForEdit,
    setSnapExclude,
    setEditModeActive,
} from './snapping'
import { computeCircleRing, computeCircleRingForEdit } from './geometry'
import { GEOMETRY_CONFIG } from '../config'
import { showToast } from '../toast'
import { debugError } from '../utils/debug'
import { buildDrawControl } from './draw-control'
import { repatchMarker } from './draw-complete'
import { PHASES } from '../phases'
import type { LayerEntry } from '../types'

// ─── EDIT STATE ───────────────────────────────────────────────────────────────

export let isEditMode = false

// Track the Geoman feature ID (string) so we can fully remove it via features.delete()
let activeGeomanFeatureId: string | null = null
let activeEditEntry: LayerEntry | null = null
// Snapshot of original coordinates before editing — used to restore on cancel.
let activeEditCoordsSnapshot: import('../types').LatLng[] | null = null
// eslint-disable-next-line @typescript-eslint/no-explicit-any
let _origSetLngLat: ((...args: any[]) => void) | null = null

export function getActiveEditEntry(): LayerEntry | null {
    return activeEditEntry
}

// ─── ENABLE EDIT MODE ────────────────────────────────────────────────────────

export async function enableEditMode(featureId?: string): Promise<void> {
    if (!ctx.geoman) return

    if (featureId) {
        const entry = findLayerEntryByFeatureId(featureId)
        if (entry) {
            activeEditEntry = entry
            // Deep copy original coordinates for cancel/restore — shallow copy
            // would hold references to the same objects that Geoman mutates
            // during drag, corrupting the snapshot.
            activeEditCoordsSnapshot = entry.data.coordinates
                ? entry.data.coordinates.map((c) => ({ lat: c.lat, lng: c.lng }))
                : entry.data.lat != null && entry.data.lng != null
                  ? [{ lat: entry.data.lat, lng: entry.data.lng }]
                  : null
            const gj = buildGeomanImportFeature(entry)
            if (gj) {
                try {
                    // overwrite:true ensures a re-edit of the same feature replaces
                    // the old Geoman entry rather than adding a second copy underneath.
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    const result = await ctx.geoman.features.importGeoJson(gj as any, { overwrite: true } as any)
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    const added = (result as any)?.addedFeatures?.[0]
                    activeGeomanFeatureId = added?.id ?? null
                } catch (err) {
                    debugError('Geoman importGeoJson failed:', err)
                }
            }
        }
    }

    disableCrosshair()
    disableSnapping()

    // Await so that markerPointer.marker is created before we patch it.
    // Geoman's change action creates the invisible markerPointer marker in
    // onStartAction(), which is called inside enableGlobalEditMode().
    await ctx.geoman.enableGlobalEditMode()
    isEditMode = true
    setEditModeActive(true)
    setSnapExclude(featureId ?? null)

    // Patch markerPointer.marker.setLngLat to inject our snap.
    //
    // Why: Geoman's markerPointer.onMouseMove calls mp.setLngLat(rawMousePos)
    // in bubble phase, AFTER our old capture-phase listener ran. That overwrote
    // whatever we set. sendMarkerMoveEvent then reads the un-snapped value.
    //
    // By patching setLngLat itself, every position update — whether from
    // markerPointer.onMouseMove, from our old capture listener, or from any
    // other Geoman internal call — goes through our snap before being stored.
    // sendMarkerMoveEvent then naturally reads the snapped value via getLngLat().
    patchMarkerPointerSnap()

    // Show the edit-mode Save button
    showEditSaveButton()
}

// ─── DISABLE EDIT MODE ───────────────────────────────────────────────────────

export function disableEditMode(): void {
    if (!ctx.geoman) return
    unpatchMarkerPointerSnap()
    ctx.geoman.disableGlobalEditMode()
    isEditMode = false
    setEditModeActive(false)
    activeGeomanFeatureId = null
    activeEditEntry = null
    activeEditCoordsSnapshot = null
    setSnapExclude(null)
    // Clear selection so the user can select another feature to edit.
    setSelectedFeature(null)
    updateSelectionHighlight(null)
    enableCrosshair()
    reEnableSnapping()
    // Hide the edit-mode save button
    hideEditSaveButton()
}

// Re-enable snapping. The snap module decides what to search via getActiveSnapPhases().
function reEnableSnapping(): void {
    disableSnapping()
    enableSnapping()
}

// ─── COMMIT EDIT MODE ────────────────────────────────────────────────────────

// Commit: save current geometry to API, clean up Geoman, exit edit mode.
// Called by the Save button.
export async function commitEditMode(): Promise<void> {
    const entry = activeEditEntry
    if (!entry) {
        disableEditMode()
        return
    }

    // Read final geometry from Geoman to ensure we have the latest changes
    // This prevents stale geometry issues when save is clicked rapidly
    if (activeGeomanFeatureId && ctx.geoman) {
        try {
            const geomanFeatures = await ctx.geoman.features.getAll()
            // _geoJson is an internal Geoman property (no public getGeoJSON() API as of v0.7.x).
            // Pin: @geoman-io/maplibre-geoman-free ^0.7.1 — re-verify on each major upgrade.
            // If _geoJson is absent we fall through to the existing entry.data values.
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const geomanFeature: any = geomanFeatures.features?.find((f: any) => f.id === activeGeomanFeatureId)
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const rawGeometry = (geomanFeature as any)?._geoJson?.geometry
            if (rawGeometry && typeof rawGeometry === 'object' && 'type' in rawGeometry) {
                // Keep as `any` — GeoJSON.Geometry would cause the `else` branch to
                // narrow Polygon to `never` and strip `coordinates` from GeometryCollection.
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const geometry = rawGeometry as any
                // Validate geometry before saving
                if (geometry.type === 'LineString' && (!geometry.coordinates || geometry.coordinates.length < 2)) {
                    showToast('Road must have at least 2 points.', 'error')
                    await cancelEditMode()
                    return
                }
                if (geometry.type === 'Polygon' && (!geometry.coordinates[0] || geometry.coordinates[0].length < 3)) {
                    showToast('Area must have at least 3 points.', 'error')
                    await cancelEditMode()
                    return
                }

                // Mirror updated geometry into entry.data
                if (geometry.type === 'Point') {
                    const c = geometry.coordinates as [number, number]
                    entry.data.lat = c[1]
                    entry.data.lng = c[0]
                } else if (geometry.type === 'Polygon') {
                    const coords = geometry.coordinates[0] as [number, number][]
                    entry.data.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))

                    // City center: recompute center + radius from edited polygon.
                    if (entry.type === 'circle' && coords.length >= 3) {
                        let sumLat = 0,
                            sumLng = 0
                        for (const [lng, lat] of coords) {
                            sumLat += lat
                            sumLng += lng
                        }
                        entry.data.lat = sumLat / coords.length
                        entry.data.lng = sumLng / coords.length

                        // Recompute radius as average distance from center
                        const R = GEOMETRY_CONFIG.earthRadiusMeters
                        let totalDist = 0
                        for (const [lng, lat] of coords) {
                            const dlat = ((lat - entry.data.lat!) * Math.PI) / 180
                            const dlng = ((lng - entry.data.lng!) * Math.PI) / 180
                            const a =
                                Math.sin(dlat / 2) ** 2 +
                                Math.cos((entry.data.lat! * Math.PI) / 180) *
                                    Math.cos((lat * Math.PI) / 180) *
                                    Math.sin(dlng / 2) ** 2
                            totalDist += R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
                        }
                        entry.data.radius = totalDist / coords.length
                    }
                } else {
                    // Handles LineString and any other non-Point geometry.
                    // Geoman may return a Polygon for a line shape depending on
                    // internal representation — use the first ring in that case.
                    const coords =
                        geometry.type === 'Polygon'
                            ? (geometry.coordinates[0] as [number, number][])
                            : (geometry.coordinates as [number, number][])
                    entry.data.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
                }
            }
        } catch (err) {
            debugError('Failed to read Geoman geometry:', err)
        }
    }

    try {
        await apiFetch(`/api/update/${entry.dbId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ data: entry.data }),
        })
        showToast('Geometry saved.', 'success')
    } catch (err) {
        debugError('Failed to save geometry:', err)
        showToast('Failed to save geometry changes', 'error')
    }

    // City center: re-render as clean circle ring (LineString outline)
    if (entry.type === 'circle' && entry.data.lat != null && entry.data.lng != null && entry.data.radius) {
        const ring = computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius)
        // Close the ring
        ring.push([ring[0][0], ring[0][1]])
        featuresStore.update(entry.id, {
            geometry: { type: 'LineString', coordinates: ring },
        })
    }

    await removeGeomanFeature()
    disableEditMode()

    // Return to draw mode after saving so the user can continue working.
    const phase = PHASES.find((p) => p.key === entry.data.type)
    if (phase) {
        buildDrawControl(phase)
        // Re-patch the fresh Geoman marker so snapping works for the next draw.
        repatchMarker()
    }
}

// ─── CANCEL EDIT MODE ────────────────────────────────────────────────────────

/**
 * Cancel edit mode — discard changes and restore original geometry.
 * Matching the reference: ESC during editing exits without saving.
 */
export async function cancelEditMode(): Promise<void> {
    const entry = activeEditEntry
    if (!entry) {
        disableEditMode()
        return
    }

    // Restore original coordinates from snapshot
    if (activeEditCoordsSnapshot) {
        entry.data.coordinates = activeEditCoordsSnapshot
        // Re-render the original geometry in the NARS layer
        if (entry.data.lat != null && entry.data.lng != null) {
            // City center: restore as LineString circle ring, not Point
            if (entry.type === 'circle' && entry.data.radius) {
                const ring = computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius)
                ring.push([ring[0][0], ring[0][1]])
                featuresStore.update(entry.id, {
                    geometry: { type: 'LineString', coordinates: ring },
                })
            } else {
                const geom: GeoJSON.Point = { type: 'Point', coordinates: [entry.data.lng, entry.data.lat] }
                featuresStore.update(entry.id, { geometry: geom })
            }
        } else if (entry.data.coordinates && entry.data.coordinates.length > 0) {
            const coords = entry.data.coordinates.map((c) => [c.lng, c.lat])
            if (entry.type === 'line') {
                const geom: GeoJSON.LineString = { type: 'LineString', coordinates: coords }
                featuresStore.update(entry.id, { geometry: geom })
            } else if (entry.type === 'circle') {
                // City center with coordinates — restore as LineString ring
                const closed =
                    coords[0][0] === coords[coords.length - 1][0] && coords[0][1] === coords[coords.length - 1][1]
                        ? coords
                        : [...coords, coords[0]]
                const geom: GeoJSON.LineString = { type: 'LineString', coordinates: closed }
                featuresStore.update(entry.id, { geometry: geom })
            } else {
                const closed =
                    coords[0][0] === coords[coords.length - 1][0] && coords[0][1] === coords[coords.length - 1][1]
                        ? coords
                        : [...coords, coords[0]]
                const geom: GeoJSON.Polygon = { type: 'Polygon', coordinates: [closed] }
                featuresStore.update(entry.id, { geometry: geom })
            }
        }
    }

    // Remove the Geoman feature and clean up
    await removeGeomanFeature()
    disableEditMode()
    showToast('Edit cancelled.', 'info')

    // Return to draw mode after canceling so the user can continue working.
    const phase = PHASES.find((p) => p.key === entry.data.type)
    if (phase) {
        buildDrawControl(phase)
        repatchMarker()
    }
}

// ─── GEOMAN FEATURE REMOVAL ──────────────────────────────────────────────────

// Fully remove the imported Geoman feature from its internal store.
// Using features.delete(id) (not featureData.delete()) ensures the featureStore
// entry is also removed, preventing a duplicate on the next re-edit.
async function removeGeomanFeature(): Promise<void> {
    if (!ctx.geoman || !activeGeomanFeatureId) return
    try {
        await ctx.geoman.features.delete(activeGeomanFeatureId)
    } catch {
        // Feature may already be gone (e.g. user deleted via Geoman)
    }
    activeGeomanFeatureId = null
}

// ─── EDIT SNAP PATCH ──────────────────────────────────────────────────────────
// We patch markerPointer.marker.setLngLat so that every position update
// (whether from Geoman's own markerPointer.onMouseMove or anywhere else)
// goes through our snap before being stored.
//
// sendMarkerMoveEvent reads position via getLngLat(), which returns whatever
// setLngLat last stored — so patching setLngLat is the correct interception point.
//
// A capture-phase .eousemove does NOT work because markerPointer's own bubble-phase
// handler overwrites our position after we set it.

function patchMarkerPointerSnap(): void {
    const mp = ctx.geoman?.markerPointer?.marker
    if (!mp || _origSetLngLat) return // already patched or no marker

    _origSetLngLat = mp.setLngLat.bind(mp)

    mp.setLngLat = ([lng, lat]: [number, number]) => {
        const px = ctx.map.project([lng, lat])
        const snapped = snapPointForEdit(px.x, px.y, activeEditEntry?.id ?? null)
        _origSetLngLat!(snapped ? [snapped.lng, snapped.lat] : [lng, lat])
    }
}

function unpatchMarkerPointerSnap(): void {
    const mp = ctx.geoman?.markerPointer?.marker
    if (mp && _origSetLngLat) {
        mp.setLngLat = _origSetLngLat
    }
    _origSetLngLat = null
}

// ─── EDIT MODE SAVE BUTTON ───────────────────────────────────────────────────
// Floating Save button shown at bottom-center when in edit mode.
// Right-click cancels (handled by draw-events.ts). Styled to match the app's
// glassmorphism UI pattern.

let _editSaveBtn: HTMLElement | null = null

function showEditSaveButton(): void {
    hideEditSaveButton()

    const btn = document.createElement('button')
    btn.id = 'nars-edit-save'
    btn.className = 'nars-edit-save-btn'
    btn.setAttribute('aria-label', 'Save edited geometry')
    btn.setAttribute('title', 'Save edited geometry')
    btn.innerHTML = `
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
        Save Geometry
    `
    _editSaveBtn = btn
    document.body.appendChild(btn)

    btn.addEventListener('click', () => {
        void commitEditMode()
    })
}

function hideEditSaveButton(): void {
    if (_editSaveBtn) {
        _editSaveBtn.remove()
        _editSaveBtn = null
    }
    document.getElementById('nars-edit-save')?.remove()
}

// ─── GEOMAN IMPORT FEATURE ───────────────────────────────────────────────────

// Build a GeoJSON feature in the shape Geoman's importGeoJson expects.
// The `shape` property tells Geoman which vertex handle style to use.
function buildGeomanImportFeature(entry: LayerEntry): GeoJSON.Feature | null {
    // City center: edit as polygon (not circle) since we store as LineString ring.
    const shape =
        entry.type === 'line'
            ? 'line'
            : entry.type === 'marker'
              ? 'marker'
              : entry.type === 'circle'
                ? 'line'
                : 'polygon' // City centers use line shape

    const props = { shape, dbId: entry.dbId }

    // City center: render as geographic circle ring for proper editing.
    if (entry.type === 'circle' && entry.data.lat != null && entry.data.lng != null && entry.data.radius) {
        const ring = computeCircleRingForEdit(entry.data.lat, entry.data.lng, entry.data.radius)
        // Close the ring
        ring.push([ring[0][0], ring[0][1]])
        return {
            type: 'Feature',
            geometry: { type: 'LineString', coordinates: ring },
            properties: props,
        }
    }

    if (entry.data.lat != null && entry.data.lng != null) {
        return {
            type: 'Feature',
            geometry: { type: 'Point', coordinates: [entry.data.lng, entry.data.lat] },
            properties: props,
        }
    }
    if (entry.data.coordinates && entry.data.coordinates.length > 0) {
        const coords = entry.data.coordinates.map((c) => [c.lng, c.lat])
        if (entry.type === 'line') {
            return {
                type: 'Feature',
                geometry: { type: 'LineString', coordinates: coords },
                properties: props,
            }
        }
        const first = coords[0],
            last = coords[coords.length - 1]
        const ring = first[0] === last[0] && first[1] === last[1] ? coords : [...coords, coords[0]]
        return {
            type: 'Feature',
            geometry: { type: 'Polygon', coordinates: [ring] },
            properties: props,
        }
    }
    return null
}

// ─── LOOKUP HELPERS ───────────────────────────────────────────────────────────

export function findLayerEntryByFeatureId(featureId: string | undefined): LayerEntry | null {
    if (!featureId) return null
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    for (const key of Object.keys(state)) {
        const entries = state[key as keyof LayerState]
        const entry = entries?.find((e) => e.id === featureId)
        if (entry) return entry
    }
    return null
}

// ─── GEOMAN FILL SUPPRESSION ──────────────────────────────────────────────────

// Geoman's default polygon fill layer uses a solid blue fill that covers NARS
// rendered features. Geoman also renders each vertex of its circle approximation
// as a dot via a `circle`-type layer — suppressed here so the drawn city center
// shows as a clean ring outline instead of a ring of dots.
// NARS owns all rendering via the 'features' GeoJSON source.
// Call this once after Geoman initialises.
export function suppressGeomanFill(): void {
    // Fill layers — zero out opacity
    for (const layerId of ['gm_main-polygon__fill-layer-0', 'gm_temporary-polygon__fill-layer-0']) {
        try {
            if (ctx.map.getLayer(layerId)) {
                ctx.map.setPaintProperty(layerId, 'fill-opacity', 0)
            }
        } catch {
            /* layer may not exist */
        }
    }

    // Circle vertex dot layers — shown during draw/edit of circle geometry.
    // Each vertex of the approximated polygon ring is rendered as a MapLibre
    // circle (dot); hiding them leaves only Geoman's line outline visible.
    for (const layerId of ['gm_main-circle__circle-layer-0', 'gm_temporary-circle__circle-layer-0']) {
        try {
            if (ctx.map.getLayer(layerId)) {
                ctx.map.setPaintProperty(layerId, 'circle-opacity', 0)
                ctx.map.setPaintProperty(layerId, 'circle-stroke-opacity', 0)
            }
        } catch {
            /* layer may not exist */
        }
    }
}
