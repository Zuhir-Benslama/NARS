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

declare const L: typeof import('leaflet') & {
    Draw: any
    Control: typeof import('leaflet').Control & { Draw: new (opts: any) => any }
    DrawEvents: any
}

// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.id = 'nars-ctx-menu'
    el.className = 'nars-ctx-menu'
    el.style.display = 'none'
    document.body.appendChild(el)
    document.addEventListener('click',       () => { el.style.display = 'none' })
    document.addEventListener('contextmenu', () => { el.style.display = 'none' })
    return el
}

let _ctxEl: HTMLElement | null = null
function getCtxEl(): HTMLElement {
    if (!_ctxEl) _ctxEl = createContextMenuEl()
    return _ctxEl
}

function showContextMenu(x: number, y: number, dbId: number, phaseKey: string): void {
    const el = getCtxEl()
    const isRoad        = phaseKey === 'roads'
    const currentPhase  = PHASES[store.currentPhase]?.key
    const isCurrentPhase = phaseKey === currentPhase
    el.innerHTML = `
        <div class="nars-ctx-item" data-action="edit">✏️ Edit Info</div>
        <div class="nars-ctx-item" data-action="boundaries">⬟ Edit Boundaries</div>
        ${isRoad ? '<div class="nars-ctx-item" data-action="reverse">⇄ Reverse Direction</div>' : ''}
        ${isCurrentPhase ? '<div class="nars-ctx-item nars-ctx-danger" data-action="remove">🗑️ Remove Object</div>' : ''}
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
            if      (action === 'edit')       (window as any).__narsEditFeature(dbId)
            else if (action === 'boundaries') (window as any).__narsEditBoundaries(dbId)
            else if (action === 'reverse')    (window as any).__narsReverseRoad(dbId)
            else if (action === 'remove')     (window as any).__narsRemoveFeature(dbId)
        }
    })
}

export function bindContextMenu(layer: L.Layer, dbId: number, phaseKey: string): void {
    layer.on('contextmenu', (e: any) => {
        e.originalEvent.preventDefault()
        e.originalEvent.stopPropagation()
        showContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, dbId, phaseKey)
    })
}

// ─── REMOVE FEATURE ───────────────────────────────────────────────────────────

async function removeFeature(dbId: number): Promise<void> {
    if (!confirm('Remove this feature? This cannot be undone.')) return

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

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

async function editBoundaries(dbId: number): Promise<void> {
    // Find the layer to edit
    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))

    const entry = phaseKey
        ? featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId) as LayerEntry | undefined
        : undefined

    if (!entry) {
        alert('Could not find the feature to edit.')
        return
    }

    const snapMode = phaseKey === 'roads' ? 'roads'
                   : (phaseKey === 'districts' || phaseKey === 'areas') ? 'districts'
                   : null

    if (snapMode) enableSnapping(snapMode, entry?.layer, phaseKey)
    if (snapMode) hookEditHandles()

    // Disable snapping once edit mode ends (save or cancel)
    const onEditStop = () => {
        if (snapMode) disableSnapping()
        ;(ctx.map as any).off('pm:editend', onEditStop)
    }
    ;(ctx.map as any).on('pm:editend', onEditStop)

    // Enable Geoman editing on the layer
    (entry.layer as any).pm.enable()
}

// ─── EDIT FEATURE INFO ────────────────────────────────────────────────────────

async function editFeatureInfo(dbId: number): Promise<void> {
    ctx.map.closePopup()

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

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

// ─── REVERSE ROAD DIRECTION ───────────────────────────────────────────────────

async function reverseRoadDirection(dbId: number): Promise<void> {
    const entry = featureLayers.roads.find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    // Reverse coordinates in data and on the layer
    const coords = entry.data.coordinates
    if (!coords || coords.length < 2) return
    const reversed = [...coords].reverse()
    entry.data.coordinates = reversed

    const newLatLngs = reversed.map((c: { lat: number; lng: number }) => L.latLng(c.lat, c.lng))
    ;(entry.layer as L.Polyline).setLatLngs(newLatLngs)

    // Refresh endpoint arrow markers
    if ((entry.layer as any)._endpointMarkers) {
        ;(entry.layer as any)._endpointMarkers.forEach((m: L.Layer) => ctx.lineEndpointLayer.removeLayer(m))
        ;(entry.layer as any)._endpointMarkers = []
    }
    addPolylineEndpoints(entry.layer)

    // Persist to database
    try {
        await apiFetch(`/api/update/${dbId}`, {
            method:  'PUT',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ data: entry.data }),
        })
    } catch (err) { console.error('Reverse road save error:', err) }
}

// ─── GLOBAL WINDOW HANDLERS ───────────────────────────────────────────────────

;(window as any).__narsEditFeature    = editFeatureInfo
;(window as any).__narsEditBoundaries = editBoundaries
;(window as any).__narsReverseRoad    = reverseRoadDirection
;(window as any).__narsRemoveFeature  = removeFeature
