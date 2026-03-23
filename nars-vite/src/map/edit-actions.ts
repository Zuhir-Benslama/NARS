// ─── FEATURE EDIT ACTIONS ─────────────────────────────────────────────────────
// Implements the three context-menu edit operations (remove, edit geometry,
// edit info) and registers them as window globals so the menu click handler
// can call them without a circular import chain.
// Extracted from context-menu.ts. All three functions are exported directly;
// context-menu.ts imports them instead of going through window globals.

import { apiFetch }                                               from '../api'
import { PHASES }                                                 from '../phases'
import { store, featureLayers, openEditModal, syncCounts }        from '../store'
import type { LayerEntry }                                        from '../types'
import { ctx }                                                    from './state'
import { areaStyle, buildPopup }                                  from './styles'
import { createAreaPerimeterLabel, createPolygonEdgeLabel,
         createPermanentLabel }                                   from './labels'
import { enableSnapping, disableSnapping, hookEditHandles }       from './snapping'
import { refreshScatteredAreas }                                  from './geometry'
import { t }                                                      from '../i18n'

declare const L: typeof import('leaflet')

// ─── REMOVE ───────────────────────────────────────────────────────────────────

export async function removeFeature(dbId: number): Promise<void> {
    if (!confirm(t('msg_confirm_remove'))) return

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    const currentPhaseKey = PHASES[store.currentPhase]?.key
    if (phaseKey !== currentPhaseKey) {
        alert(t('alert_switch_phase_to_remove', { phase: t(PHASES.find(p => p.key === phaseKey)?.label ?? phaseKey) }))
        return
    }

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    try {
        const res = await apiFetch(`/api/delete/${dbId}`, { method: 'DELETE' })
        if (!res.ok) { alert(t('alert_remove_failed')); return }
    } catch { alert(t('alert_remove_failed')); return }

    const layer = entry.layer
    ctx.drawnItems.removeLayer(layer)
    if (ctx.roadsDisplayLayer.hasLayer(layer)) ctx.roadsDisplayLayer.removeLayer(layer)

    if ((layer as any)._endpointMarkers)
        (layer as any)._endpointMarkers.forEach((m: L.Layer) => ctx.lineEndpointLayer.removeLayer(m))
    if ((layer as any)._perimeterLabel)
        ctx.perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
    if ((layer as any)._edgeLabelMarkers)
        (layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => ctx.polygonEdgeLabelLayer.removeLayer(m))

    featureLayers[phaseKey] = featureLayers[phaseKey]
        .filter((e: LayerEntry) => (e.layer as any)._dbId !== dbId)

    if (phaseKey === 'areas') await refreshScatteredAreas()
    syncCounts()
}

// ─── EDIT GEOMETRY ────────────────────────────────────────────────────────────

export async function editGeometry(dbId: number): Promise<void> {
    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    const entry = phaseKey
        ? featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId) as LayerEntry | undefined
        : undefined

    if (!entry) { alert(t('alert_edit_not_found')); return }

    if (document.getElementById('nars-boundary-finish')) {
        alert(t('alert_finish_current_edit')); return
    }

    const isMarker = entry.layer instanceof L.Marker && !(entry.layer instanceof L.Circle)

    // ── Marker path — drag to new location ───────────────────────────────────
    if (isMarker) {
        const marker = entry.layer as L.Marker
        marker.dragging?.enable()

        const mapEl     = ctx.map.getContainer()
        const finishBtn = document.createElement('button')
        finishBtn.id        = 'nars-boundary-finish'
        finishBtn.className = 'nars-boundary-finish-btn nars-btn-confirm'
        finishBtn.innerHTML = t('btn_save_location')
        mapEl.appendChild(finishBtn)

        const cleanup = async (save: boolean) => {
            finishBtn.remove()
            document.removeEventListener('keydown', onKeyDown)
            marker.dragging?.disable()
            if (!save) { marker.setLatLng(L.latLng(entry.data.lat!, entry.data.lng!)); return }

            const ll = marker.getLatLng()
            entry.data.lat = ll.lat
            entry.data.lng = ll.lng
            if (phaseKey === 'cityCenter') store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }

            try {
                await apiFetch(`/api/update/${dbId}`, {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ data: entry.data }),
                })
            } catch (err) { console.error('Edit location save error:', err) }
        }

        finishBtn.addEventListener('click', () => cleanup(true),  { once: true })
        const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') cleanup(false) }
        document.addEventListener('keydown', onKeyDown)
        return
    }

    // ── Polygon / polyline path — reshape vertices ────────────────────────────
    const snapMode = phaseKey === 'roads'
        ? 'roads'
        : (phaseKey === 'districts' || phaseKey === 'areas') ? 'districts'
        : null

    if (snapMode) enableSnapping(snapMode as 'districts' | 'roads', entry.layer, phaseKey)
    hookEditHandles()
    ;(entry.layer as any).pm.enable()

    const mapEl     = ctx.map.getContainer()
    const finishBtn = document.createElement('button')
    finishBtn.id        = 'nars-boundary-finish'
    finishBtn.className = 'nars-boundary-finish-btn nars-btn-confirm'
    finishBtn.innerHTML = t('btn_save_geometry')
    mapEl.appendChild(finishBtn)

    const cleanup = async (save: boolean) => {
        finishBtn.remove()
        document.removeEventListener('keydown', onKeyDown)
        ;(entry.layer as any).pm.disable()
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
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ data: { ...entry.data, coordinates } }),
                })
                if (phaseKey === 'areas') await refreshScatteredAreas()
            }
        } catch (err) { console.error('Edit geometry save error:', err) }
    }

    finishBtn.addEventListener('click', () => cleanup(true),  { once: true })
    const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') cleanup(false) }
    document.addEventListener('keydown', onKeyDown)
}

// ─── EDIT FEATURE INFO ────────────────────────────────────────────────────────

export async function editFeatureInfo(dbId: number): Promise<void> {
    ctx.map.closePopup()

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    const currentPhaseKey = PHASES[store.currentPhase]?.key
    if (phaseKey === 'areas' && currentPhaseKey === 'districts') {
        alert(t('alert_areas_uneditable_in_districts')); return
    }

    const entry      = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    const phase      = PHASES.find(p => p.key === phaseKey)
    const phaseIndex = PHASES.findIndex(p => p.key === phaseKey)
    if (!entry || !phase || phaseIndex === -1) return

    const resultPromise = openEditModal(phaseIndex, dbId, entry.data)

    if (phaseKey === 'houseEntrances') {
        const m = store.modal
        m.roadOptions = (featureLayers.roads as LayerEntry[]).map((r, i) => ({
            idx: i, label: r.data.label || `Road ${i + 1}`, dbId: (r.layer as any)._dbId as number,
        }))
        m.mainEntranceOptions = (featureLayers.houseEntrances as LayerEntry[])
            .filter((e: LayerEntry) => e.data.entranceTypeKey === 'main_entrance')
            .map((e, i) => ({
                idx: i, label: e.data.label || `Entrance ${i + 1}`, dbId: (e.layer as any)._dbId as number,
            }))
        if (entry.data.roadDbId != null) {
            const idx = m.roadOptions.findIndex(r => r.dbId === entry.data.roadDbId)
            if (idx >= 0) m.selectedRoadIdx = idx
        }
        if (entry.data.mainEntranceDbId != null) {
            const idx = m.mainEntranceOptions.findIndex(e => e.dbId === entry.data.mainEntranceDbId)
            if (idx >= 0) m.selectedMainIdx = idx
        }
    }

    const result = await resultPromise
    if (!result) return

    Object.assign(entry.data, result)

    try {
        await apiFetch(`/api/update/${dbId}`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ data: entry.data }),
        })
    } catch (err) { console.error('Edit info save error:', err) }

    ;(entry.layer as L.Path).bindPopup(buildPopup(entry.data, phase, dbId))

    if (phaseKey === 'areas') {
        ;(entry.layer as L.Path).setStyle(areaStyle(entry.data.areaTypeKey ?? 'central_urban'))
        createAreaPerimeterLabel(entry.layer, entry.data.areaTypeKey ?? 'central_urban')
    }
    if (phaseKey === 'districts')
        createPolygonEdgeLabel(entry.layer, entry.data.label, '#f39c12')
    if (phaseKey !== 'areas' && phaseKey !== 'districts')
        createPermanentLabel(entry.layer, entry.data.label, phaseKey)
}

// ─── WINDOW GLOBALS ───────────────────────────────────────────────────────────
// Registered here (not inline in context-menu.ts) so context-menu.ts can
// call them by reference without a circular import.

;(window as any).__narsEditFeature   = editFeatureInfo
;(window as any).__narsEditGeometry  = editGeometry
;(window as any).__narsRemoveFeature = removeFeature

// Used by addPolylineEndpoints (labels.ts) to suppress start arrows that would
// overlap city center circles.
;(window as any).__narsGetCityCenterLatLngs = () =>
    (featureLayers.cityCenter as LayerEntry[])
        .filter((e: LayerEntry) => e.layer instanceof L.Circle)
        .map((e: LayerEntry) => (e.layer as L.Circle).getLatLng())
