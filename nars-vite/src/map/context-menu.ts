// ─── CONTEXT MENU & FEATURE EDITING ─────────────────────────────────────────

import { ctx }                           from './state'
import { POLYLINE_WEIGHT }               from './state'
import { featureLayers, openEditModal, syncCounts, store } from '../store'
import { PHASES }                        from '../phases'
import { apiFetch }                      from '../api'
import type { LayerEntry }               from '../types'
import { areaStyle, buildPopup, createEntranceIcon } from './styles'
import { createAreaPerimeterLabel, createPolygonEdgeLabel, addPolylineEndpoints, createPermanentLabel } from './labels'
import { enableSnapping, disableSnapping, hookEditHandles } from './snapping'
import { refreshScatteredAreas }           from './geometry'
import { t }                               from '../i18n'

declare const L: typeof import('leaflet')

// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.id = 'nars-ctx-menu'
    el.className = 'nars-ctx-menu'
    el.style.display = 'none'
    document.body.appendChild(el)
    const hideMenu = () => { el.style.display = 'none' }
    // Hide menu on click anywhere except on the menu itself
    document.addEventListener('click', (e) => {
        if (!el.contains(e.target as Node)) hideMenu()
    })
    // Hide menu on right-click anywhere except on the menu itself
    document.addEventListener('contextmenu', (e) => {
        if (!el.contains(e.target as Node)) hideMenu()
    })
    // Hide menu on ESC
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') hideMenu()
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
        el.innerHTML = `<div class="nars-ctx-item nars-ctx-disabled">${t('ctx_cc_lock')}</div>`
        el.style.left = '-9999px'; el.style.top = '-9999px'; el.style.display = 'block'
        const w = el.offsetWidth, h = el.offsetHeight
        el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
        el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'
        el.querySelector('.nars-ctx-disabled')?.addEventListener('click', () => { el.style.display = 'none' })
        return
    }

    const isRoadsPhase          = currentPhase === 'roads'
    const isHouseEntrancesPhase = currentPhase === 'houseEntrances'
    const isMainEntrance = phaseKey === 'houseEntrances' &&
        (featureLayers.houseEntrances as LayerEntry[]).find(
            e => (e.layer as any)._dbId === dbId
        )?.data?.entranceTypeKey === 'main_entrance'

    // "Set Road Directions" — only available while in the Roads phase (phase 04).
    const roadDir = isRoad && isRoadsPhase
        ? `<div class="nars-ctx-item" data-action="road-dir">${t('ctx_road_dir')}</div>` : ''

    // "Set as Reference Road" — shown in House Entrances phase when this road is NOT
    // already the reference. Replaced by "Remove" when it IS the reference.
    const isCurrentRef   = isRoad && dbId === store.referenceRoadDbId
    const setRoadRef     = isRoad && isHouseEntrancesPhase && !isCurrentRef
        ? `<div class="nars-ctx-item" data-action="set-road-ref">${t('ctx_road_ref')}</div>` : ''
    const removeRoadRef  = isRoad && isHouseEntrancesPhase && isCurrentRef
        ? `<div class="nars-ctx-item" data-action="remove-road-ref">${t('ctx_road_ref_remove')}</div>` : ''

    // "Set as Reference Entrance" for main entrances (when in house entrances phase)
    const setEntranceRef = isMainEntrance && isHouseEntrancesPhase
        ? `<div class="nars-ctx-item" data-action="set-entrance-ref">${t('ctx_ent_ref')}</div>` : ''

    // Roads are fully read-only in the House Entrances phase: no edit, no geometry, no delete.
    // House entrance markers DO keep Edit Info so the user can change the entrance number.
    const roadInHousePhase = isRoad && isHouseEntrancesPhase

    const editInfo     = !roadInHousePhase
        ? `<div class="nars-ctx-item" data-action="edit">${t('ctx_edit_info')}</div>`         : ''
    const editGeometry = !roadInHousePhase
        ? `<div class="nars-ctx-item" data-action="geometry">${t('ctx_edit_geom')}</div>` : ''
    const removeItem   = !roadInHousePhase
        ? `<div class="nars-ctx-item nars-ctx-danger" data-action="remove">${t('ctx_remove')}</div>` : ''

    el.innerHTML = `
        ${editInfo}
        ${editGeometry}
        ${roadDir}
        ${setRoadRef}
        ${removeRoadRef}
        ${setEntranceRef}
        ${removeItem}
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
            if      (action === 'edit')             (window as any).__narsEditFeature(dbId)
            else if (action === 'geometry')          (window as any).__narsEditGeometry(dbId)
            else if (action === 'road-dir') {
                import('./road-directions').then(m => m.computeAndApplyRoadDirections())
            }
            else if (action === 'set-road-ref')      setReferenceRoad(dbId)
            else if (action === 'remove-road-ref')   clearReferenceRoad()
            else if (action === 'set-entrance-ref')  setReferenceEntrance(dbId)
            else if (action === 'remove')            (window as any).__narsRemoveFeature(dbId)
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
                // Right-click cancels draw or edit mode instead of opening the menu.
                if ((ctx.map as any).pm.globalDrawModeEnabled()) {
                    ;(ctx.map as any).pm.disableDraw()
                    return
                }
                if ((ctx.map as any).pm.globalEditModeEnabled()) {
                    ;(ctx.map as any).pm.disableGlobalEditMode()
                    return
                }
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
    if (!confirm(t('msg_confirm_remove'))) return

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    // Guard: only allow removal when on the feature's own phase.
    // Areas are always visible so this prevents accidental deletion from another phase.
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
    // Roads live in roadsDisplayLayer, not drawnItems — remove from there too.
    if (ctx.roadsDisplayLayer.hasLayer(layer)) ctx.roadsDisplayLayer.removeLayer(layer)

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

    if (!entry) { alert(t('alert_edit_not_found')); return }

    // Guard: geometry edits are only allowed in the feature's own phase.
    const currentPhaseKeyEg = PHASES[store.currentPhase]?.key
    if (phaseKey !== currentPhaseKeyEg) {
        const phaseLabel = PHASES.find(p => p.key === phaseKey)?.label ?? phaseKey ?? ''
        alert(t('alert_switch_phase_to_edit', { phase: t(phaseLabel) }))
        return
    }

    // Prevent starting a second geometry edit while one is in progress
    if (document.getElementById('nars-boundary-finish')) {
        alert(t('alert_finish_current_edit'))
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
        finishBtn.className = 'nars-boundary-finish-btn nars-btn-confirm'
        finishBtn.innerHTML = t('btn_save_location')
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
    finishBtn.className = 'nars-boundary-finish-btn nars-btn-confirm'
    finishBtn.innerHTML = t('btn_save_geometry')
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

    // Guard: info edits are only allowed in the feature's own phase.
    const currentPhaseKey = PHASES[store.currentPhase]?.key
    if (phaseKey !== currentPhaseKey) {
        const phaseLabel = PHASES.find(p => p.key === phaseKey)?.label ?? phaseKey ?? ''
        alert(t('alert_switch_phase_to_edit', { phase: t(phaseLabel) }))
        return
    }

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    const phase      = PHASES.find(p => p.key === phaseKey)
    const phaseIndex = PHASES.findIndex(p => p.key === phaseKey)
    if (!phase || phaseIndex === -1) return

    // openEditModal sets basic fields (label, decisionNumber, decisionDate, entranceSide,
    // entranceNumber, bisNumber) but resets roadOptions/mainEntranceOptions to [].
    // Start the modal promise then immediately re-populate the selector lists and
    // pre-select the existing road / main entrance synchronously — before Vue renders.
    const resultPromise = openEditModal(phaseIndex, dbId, entry.data)

    if (phaseKey === 'houseEntrances') {
        const m = store.modal
        // Rebuild the road option list
        m.roadOptions = (featureLayers.roads as LayerEntry[]).map((r, i) => ({
            idx:   i,
            label: r.data.label || `Road ${i + 1}`,
            dbId:  (r.layer as any)._dbId as number,
        }))
        // Rebuild the main-entrance option list (for secondary entrances)
        m.mainEntranceOptions = (featureLayers.houseEntrances as LayerEntry[])
            .filter((e: LayerEntry) => e.data.entranceTypeKey === 'main_entrance')
            .map((e, i) => ({
                idx:   i,
                label: e.data.label || `Entrance ${i + 1}`,
                dbId:  (e.layer as any)._dbId as number,
            }))
        // Pre-select the road this entrance is assigned to
        if (entry.data.roadDbId != null) {
            const idx = m.roadOptions.findIndex(r => r.dbId === entry.data.roadDbId)
            if (idx >= 0) m.selectedRoadIdx = idx
        }
        // Pre-select the main entrance this secondary entrance is linked to
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
    // Update the permanent centre label for roads and other polyline/polygon phases
    if (phaseKey !== 'areas' && phaseKey !== 'districts') {
        createPermanentLabel(entry.layer, entry.data.label, phaseKey)
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
    const el = getCtxEl()

    if (phase.key === 'roads') {
        el.innerHTML = `<div class="nars-ctx-item" data-action="road-dir">${t('ctx_road_dir')}</div>`
    } else if (phase.key === 'houseEntrances') {
        el.innerHTML = `<div class="nars-ctx-item" data-action="set-house-numbers">${t('ctx_house_nums')}</div>`
    } else if (phase.key === 'namingPanels') {
        el.innerHTML = `<div class="nars-ctx-item" data-action="set-naming-panels">${t('ctx_set_naming_panels')}</div>`
    } else {
        return
    }

    el.style.left = '-9999px'; el.style.top = '-9999px'; el.style.display = 'block'
    const w = el.offsetWidth, h = el.offsetHeight
    el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
    el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            const action = (item as HTMLElement).dataset.action
            if (action === 'road-dir') {
                import('./road-directions').then(m => m.computeAndApplyRoadDirections())
            }
            else if (action === 'set-naming-panels') (window as any).__narsSetNamingPanels()
            else if (action === 'set-house-numbers') setHouseNumbers()
        }
    })
}

// ─── HOUSE ENTRANCE REFERENCE HELPERS ────────────────────────────────────────

function highlightLayer(dbId: number, phaseKey: string, active: boolean): void {
    const entries = featureLayers[phaseKey] as LayerEntry[]
    const entry = entries?.find(e => (e.layer as any)._dbId === dbId)
    if (!entry) return
    if (entry.layer instanceof L.Polyline && !(entry.layer instanceof L.Polygon)) {
        entry.layer.setStyle({ color: active ? '#f39c12' : '#3498db', weight: active ? 5 : POLYLINE_WEIGHT })
    } else if (entry.layer instanceof L.Marker) {
        // pulse class toggled for markers
        const el = (entry.layer as any).getElement?.() as HTMLElement | undefined
        if (el) el.classList.toggle('nars-reference', active)
    }
}

export function setReferenceRoad(dbId: number): void {
    // Clear previous road highlight
    if (store.referenceRoadDbId != null)
        highlightLayer(store.referenceRoadDbId, 'roads', false)
    store.referenceRoadDbId = dbId
    highlightLayer(dbId, 'roads', true)
}

export function clearReferenceRoad(): void {
    if (store.referenceRoadDbId != null) {
        highlightLayer(store.referenceRoadDbId, 'roads', false)
        store.referenceRoadDbId = null
    }
}

export function setReferenceEntrance(dbId: number): void {
    if (store.referenceEntranceDbId != null)
        highlightLayer(store.referenceEntranceDbId, 'houseEntrances', false)
    store.referenceEntranceDbId = dbId
    highlightLayer(dbId, 'houseEntrances', true)
}

export async function setHouseNumbers(): Promise<void> {
    if (store.referenceRoadDbId == null) {
        alert(t('alert_no_ref_road'))
        return
    }

    const roadEntry = (featureLayers.roads as LayerEntry[])
        .find(r => (r.layer as any)._dbId === store.referenceRoadDbId)
    if (!roadEntry?.data.coordinates?.length) {
        alert(t('alert_ref_road_no_coords'))
        return
    }

    // Collect all unassigned (label === '?') main entrances on this road
    const unassigned = (featureLayers.houseEntrances as LayerEntry[]).filter(e =>
        e.data.entranceTypeKey === 'main_entrance' &&
        e.data.roadDbId === store.referenceRoadDbId &&
        e.data.label === '?'
    )
    if (!unassigned.length) {
        alert(t('alert_no_unassigned_entrances'))
        return
    }

    const turf = await import('@turf/turf')

    // Build a turf LineString from the road coordinates
    const roadLine = turf.lineString(
        roadEntry.data.coordinates.map(c => [c.lng, c.lat])
    )

    // Project each entrance onto the road and record distance from road start
    const withDist = unassigned.map(e => {
        const ll    = (e.layer as L.Marker).getLatLng()
        const pt    = turf.point([ll.lng, ll.lat])
        const snapped = turf.nearestPointOnLine(roadLine, pt, { units: 'meters' })
        return { entry: e, dist: snapped.properties.location ?? 0 }
    })

    // Sort by distance from road start (ascending)
    withDist.sort((a, b) => a.dist - b.dist)

    // Assign odd numbers to left side, even to right — each counter independent
    let oddNext = 1, evenNext = 2
    // Find the current max odd/even already assigned on this road to continue numbering
    ;(featureLayers.houseEntrances as LayerEntry[])
        .filter(e => e.data.entranceTypeKey === 'main_entrance'
                  && e.data.roadDbId === store.referenceRoadDbId
                  && e.data.label !== '?'
                  && e.data.entranceNumber != null)
        .forEach(e => {
            const n = e.data.entranceNumber!
            if (n % 2 !== 0 && n >= oddNext)  oddNext  = n + 2
            if (n % 2 === 0 && n >= evenNext)  evenNext = n + 2
        })

    const phase = PHASES.find(p => p.key === 'houseEntrances')!
    const updates: Promise<void>[] = []

    for (const { entry } of withDist) {
        const isLeft = entry.data.side === 'left'
        const number = isLeft ? oddNext : evenNext
        if (isLeft) oddNext += 2; else evenNext += 2

        entry.data.entranceNumber = number
        entry.data.label          = String(number)

        // Update icon
        ;(entry.layer as L.Marker).setIcon(createEntranceIcon(String(number), phase.color))

        // Persist
        const dbId = (entry.layer as any)._dbId
        updates.push(
            apiFetch(`/api/update/${dbId}`, {
                method:  'PUT',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ data: entry.data }),
            }).then(() => {}).catch(err => console.error(`setHouseNumbers save error (id=${dbId}):`, err))
        )
    }

    await Promise.all(updates)
    syncCounts()
}
