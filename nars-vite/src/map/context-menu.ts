// ─── CONTEXT MENU & FEATURE EDITING ─────────────────────────────────────────

import { ctx }                           from './state'
import { featureLayers, openEditModal, syncCounts, store } from '../store'
import { PHASES }                        from '../phases'
import { apiFetch }                      from '../api'
import type { LayerEntry }               from '../types'
import { areaStyle, buildPopup }         from './styles'
import { createAreaPerimeterLabel, createPolygonEdgeLabel, addPolylineEndpoints } from './labels'
import { enableSnapping, disableSnapping, hookEditHandles } from './snapping'
import { refreshScatteredAreas }           from './geometry'
import { computeAndApplyRoadDirections }   from './road-directions'

declare const L: typeof import('leaflet')

// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.id = 'nars-ctx-menu'
    el.className = 'nars-ctx-menu'
    el.style.display = 'none'
    document.body.appendChild(el)
    // Hide menu on click anywhere except on the menu itself
    document.addEventListener('click', (e) => {
        if (!el.contains(e.target as Node)) {
            el.style.display = 'none'
        }
    })
    // Hide menu on right-click anywhere except on the menu itself
    document.addEventListener('contextmenu', (e) => {
        if (!el.contains(e.target as Node)) {
            el.style.display = 'none'
        }
    })
    // Hide menu on ESC
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') el.style.display = 'none'
    })
    return el
}

let _ctxEl: HTMLElement | null = null
function getCtxEl(): HTMLElement {
    if (!_ctxEl) _ctxEl = createContextMenuEl()
    return _ctxEl
}

function showContextMenu(x: number, y: number, dbId: number, phaseKey: string): void {
    const el = getCtxEl()
    const isRoad       = phaseKey === 'roads'
    const currentPhase = PHASES[store.currentPhase]?.key
    // City center circles are visible in the Roads phase but must not be
    // edited or removed from there — only from the City Center phase.
    const ccReadOnly   = phaseKey === 'cityCenter' && currentPhase !== 'cityCenter'

    if (ccReadOnly) {
        el.innerHTML = `<div class="nars-ctx-item nars-ctx-disabled">🔒 Switch to City Center phase to edit</div>`
        el.style.left = '-9999px'; el.style.top = '-9999px'; el.style.display = 'block'
        const w = el.offsetWidth, h = el.offsetHeight
        el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
        el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'
        el.querySelector('.nars-ctx-disabled')?.addEventListener('click', () => { el.style.display = 'none' })
        return
    }

    el.innerHTML = `
        <div class="nars-ctx-item" data-action="edit">✏️ Edit Info</div>
        <div class="nars-ctx-item" data-action="geometry">⬟ Edit Geometry</div>
        ${isRoad ? '<div class="nars-ctx-item" data-action="road-dir">⇥ Set Road Directions</div>' : ''}
        <div class="nars-ctx-item nars-ctx-danger" data-action="remove">🗑️ Remove Object</div>
    `
    el.style.left = '-9999px'; el.style.top = '-9999px'; el.style.display = 'block'
    const w = el.offsetWidth, h = el.offsetHeight
    el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
    el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            const action = (item as HTMLElement).dataset.action
            if      (action === 'edit')     (window as any).__narsEditFeature(dbId)
            else if (action === 'geometry') (window as any).__narsEditGeometry(dbId)
            else if (action === 'road-dir') computeAndApplyRoadDirections()
            else if (action === 'remove')   (window as any).__narsRemoveFeature(dbId)
        }
    })
}

export function bindContextMenu(layer: L.Layer, dbId: number, phaseKey: string): void {
    try {
        console.log('bindContextMenu called for dbId:', dbId, 'phaseKey:', phaseKey)
        layer.on('contextmenu', (e: any) => {
            try {
                e.originalEvent.preventDefault()
                e.originalEvent.stopPropagation()
                // Signal the map-level contextmenu handler to skip this tick
                // (Leaflet re-fires contextmenu to the map even after a layer handles it).
                ;(ctx.map as any)._narsFeatureCtxHandled = true
                showContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, dbId, phaseKey)
            } catch (err) {
                console.error('Context menu error:', err)
            }
        })
    } catch (err) {
        console.error('bindContextMenu error:', err)
    }
}

// ─── REMOVE FEATURE ───────────────────────────────────────────────────────────

async function removeFeature(dbId: number): Promise<void> {
    if (!confirm('Remove this feature? This cannot be undone.')) return

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    // Guard: only allow removal when on the feature's own phase.
    // Areas are always visible so this prevents accidental deletion from another phase.
    const currentPhaseKey = PHASES[store.currentPhase]?.key
    if (phaseKey !== currentPhaseKey) {
        alert(`⛔ Switch to the ${PHASES.find(p => p.key === phaseKey)?.label ?? phaseKey} phase to remove this feature.`)
        return
    }

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    try {
        const res = await apiFetch(`/api/delete/${dbId}`, { method: 'DELETE' })
        if (!res.ok) { alert('Failed to remove feature.'); return }
    } catch { alert('Failed to remove feature.'); return }

    const layer = entry.layer
    ctx.drawnItems.removeLayer(layer)

    if ((layer as any)._endpointMarkers)
        (layer as any)._endpointMarkers.forEach((m: L.Layer) => ctx.lineEndpointLayer.removeLayer(m))
    if ((layer as any)._perimeterLabel)
        ctx.perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
    if ((layer as any)._edgeLabelMarkers)
        (layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => ctx.polygonEdgeLabelLayer.removeLayer(m))

    featureLayers[phaseKey] = featureLayers[phaseKey].filter((e: LayerEntry) => (e.layer as any)._dbId !== dbId)

    if (phaseKey === 'areas') await refreshScatteredAreas()
    syncCounts()
}

// ─── EDIT BOUNDARIES ──────────────────────────────────────────────────────────

async function editGeometry(dbId: number): Promise<void> {
    // Find the layer to edit
    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))

    const entry = phaseKey
        ? featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId) as LayerEntry | undefined
        : undefined

    if (!entry) { alert('Could not find the feature to edit.'); return }

    // Prevent starting a second geometry edit while one is in progress
    if (document.getElementById('nars-boundary-finish')) {
        alert('Please finish the current geometry edit first.')
        return
    }

    const isMarker = entry.layer instanceof L.Marker && !(entry.layer instanceof L.Circle)

    // ── MARKER path (city center — drag to new location) ─────────────────────
    if (isMarker) {
        const marker = entry.layer as L.Marker
        marker.dragging?.enable()

        const mapEl    = ctx.map.getContainer()
        const finishBtn = document.createElement('button')
        finishBtn.id        = 'nars-boundary-finish'
        finishBtn.className = 'nars-boundary-finish-btn'
        finishBtn.innerHTML = '✓ Save Location'
        mapEl.appendChild(finishBtn)

        const cleanup = async (save: boolean) => {
            finishBtn.remove()
            document.removeEventListener('keydown', onKeyDown)
            marker.dragging?.disable()

            if (!save) {
                // Restore original position
                marker.setLatLng(L.latLng(entry.data.lat!, entry.data.lng!))
                return
            }

            const ll = marker.getLatLng()
            entry.data.lat = ll.lat
            entry.data.lng = ll.lng

            // Keep the store in sync for city center
            if (phaseKey === 'cityCenter') {
                store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }
            }

            try {
                await apiFetch(`/api/update/${dbId}`, {
                    method:  'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body:    JSON.stringify({ data: entry.data }),
                })
            } catch (err) { console.error('Edit location save error:', err) }
        }

        finishBtn.addEventListener('click', () => cleanup(true),  { once: true })
        const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') cleanup(false) }
        document.addEventListener('keydown', onKeyDown)
        return
    }

    // ── POLYGON / POLYLINE path (reshape vertices) ───────────────────────────
    const snapMode = phaseKey === 'roads'
        ? 'roads'
        : (phaseKey === 'districts' || phaseKey === 'areas') ? 'districts'
        : null

    if (snapMode) enableSnapping(snapMode as 'districts' | 'roads', entry.layer, phaseKey)
    hookEditHandles()

    // layer.pm.enable() shows vertex handles but provides no finish affordance;
    // inject a button so the user has a clear way to commit the edit.
    ;(entry.layer as any).pm.enable()

    const mapEl = ctx.map.getContainer()
    const finishBtn = document.createElement('button')
    finishBtn.id        = 'nars-boundary-finish'
    finishBtn.className = 'nars-boundary-finish-btn'
    finishBtn.innerHTML = '✓ Save Geometry'
    mapEl.appendChild(finishBtn)

    const cleanup = async (save: boolean) => {
        finishBtn.remove()
        document.removeEventListener('keydown', onKeyDown)
        ;(entry.layer as any).pm.disable()   // fires pm:edit + pm:editend internally
        if (snapMode) disableSnapping()

        if (!save) return

        try {
            let coordinates: { lat: number; lng: number }[] | undefined

            if (entry.layer instanceof L.Polygon) {
                let coords = (entry.layer.getLatLngs()[0] as L.LatLng[])
                    .map(ll => ({ lat: ll.lat, lng: ll.lng }))
                if (coords.length >= 3) {
                    const f = coords[0], l = coords[coords.length - 1]
                    if (f.lat !== l.lat || f.lng !== l.lng)
                        coords = [...coords, { lat: f.lat, lng: f.lng }]
                }
                coordinates = coords
            } else if (entry.layer instanceof L.Polyline) {
                coordinates = (entry.layer.getLatLngs() as L.LatLng[])
                    .map(ll => ({ lat: ll.lat, lng: ll.lng }))
            }

            if (coordinates) {
                entry.data.coordinates = coordinates
                await apiFetch(`/api/update/${dbId}`, {
                    method:  'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body:    JSON.stringify({ data: { ...entry.data, coordinates } }),
                })
                if (phaseKey === 'areas') await refreshScatteredAreas()
            }
        } catch (err) { console.error('Edit geometry save error:', err) }
    }

    finishBtn.addEventListener('click', () => cleanup(true),  { once: true })

    // ESC cancels without saving
    const onKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape') cleanup(false)
    }
    document.addEventListener('keydown', onKeyDown)
}

// ─── EDIT FEATURE INFO ────────────────────────────────────────────────────────

async function editFeatureInfo(dbId: number): Promise<void> {
    ctx.map.closePopup()

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    // Prohibit editing areas in districts phase
    const currentPhaseKey = PHASES[store.currentPhase]?.key
    if (phaseKey === 'areas' && currentPhaseKey === 'districts') {
        alert('Areas cannot be edited in the districts phase. Please switch to the Areas phase to edit.')
        return
    }

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    const phase      = PHASES.find(p => p.key === phaseKey)
    const phaseIndex = PHASES.findIndex(p => p.key === phaseKey)
    if (!phase || phaseIndex === -1) return

    const result = await openEditModal(phaseIndex, dbId, entry.data)
    if (!result) return

    Object.assign(entry.data, result)

    try {
        await apiFetch(`/api/update/${dbId}`, {
            method:  'PUT',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ data: entry.data }),
        })
    } catch (err) { console.error('Edit info save error:', err) }

    ;(entry.layer as L.Path).bindPopup(buildPopup(entry.data, phase, dbId))

    if (phaseKey === 'areas') {
        ;(entry.layer as L.Path).setStyle(areaStyle(entry.data.areaTypeKey ?? 'central_urban'))
        createAreaPerimeterLabel(entry.layer, entry.data.areaTypeKey ?? 'central_urban')
    }
    if (phaseKey === 'districts') {
        createPolygonEdgeLabel(entry.layer, entry.data.label, '#f39c12')
    }
}

// ─── GLOBAL WINDOW HANDLERS ───────────────────────────────────────────────────

;(window as any).__narsEditFeature    = editFeatureInfo
;(window as any).__narsEditGeometry   = editGeometry
;(window as any).__narsRemoveFeature  = removeFeature
// Used by addPolylineEndpoints to suppress start arrows overlapping city centers
;(window as any).__narsGetCityCenterLatLngs = () =>
    (featureLayers.cityCenter as LayerEntry[])
        .filter((e: LayerEntry) => e.layer instanceof L.Circle)
        .map((e: LayerEntry) => (e.layer as L.Circle).getLatLng())

// ─── MAP BACKGROUND CONTEXT MENU ──────────────────────────────────────────────
// Shown when the user right-clicks on the map (not on a feature).
// Roads phase: "Set Road Directions" only.


export function showMapContextMenu(x: number, y: number, phase: typeof import('../phases').PHASES[number]): void {
    // Only show a map-level context menu during the Roads phase.
    // For all other phases, left-click starts drawing directly.
    if (phase.key !== 'roads') return

    const el = getCtxEl()
    el.innerHTML = `<div class="nars-ctx-item" data-action="road-dir">⇥ Set Road Directions</div>`

    el.style.left = '-9999px'; el.style.top = '-9999px'; el.style.display = 'block'
    const w = el.offsetWidth, h = el.offsetHeight
    el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
    el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            if ((item as HTMLElement).dataset.action === 'road-dir')
                computeAndApplyRoadDirections()
        }
    })
}
