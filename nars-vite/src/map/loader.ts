// ─── DATABASE LOADER ──────────────────────────────────────────────────────────
// Fetches all saved features from the API and reconstructs Leaflet layers for
// the current user. Also handles initial commune boundary loading.
// Extracted from index.ts to break the loader ↔ index circular dependency
// (loader imports buildDrawControl from draw-control.ts, not from index.ts).

import { apiFetch }                                               from '../api'
import { PHASES, API_LAYER_TO_PHASE }                             from '../phases'
import { store, featureLayers, syncCounts }                       from '../store'
import type { FeatureData, LayerEntry, DbFeature }                from '../types'
import { ctx, POLYLINE_WEIGHT }                                   from './state'
import { areaStyle, polygonStyles, createEntranceIcon, buildPopup } from './styles'
import { createPermanentLabel, createAreaPerimeterLabel,
         createPolygonEdgeLabel, refreshLayerVisibility,
         addPolylineEndpoints }                                   from './labels'
import { renderScatteredAreas, displayCommuneBoundary }           from './geometry'
import { bindContextMenu }                                        from './context-menu'
import { buildDrawControl }                                       from './draw-control'
import { bindHoverPopup, getDistrictLabel }                       from './create-handler'

declare const L: typeof import('leaflet')

// ─── LOAD FROM DATABASE ───────────────────────────────────────────────────────

export async function loadFromDatabase(): Promise<void> {
    try {
        const res = await apiFetch('/api/load')
        if (!res.ok) { console.error('Load failed:', res.status); return }
        const features = await res.json() as DbFeature[]
        if (!features.length) { console.log('No saved features.'); return }

        ctx.drawnItems.clearLayers()
        ctx.roadsDisplayLayer.clearLayers()
        ctx.lineEndpointLayer.clearLayers()
        for (const key of Object.keys(featureLayers)) featureLayers[key] = []

        let loaded = 0, skipped = 0

        for (const feature of features) {
            try {
                const data: FeatureData = typeof feature.data === 'string'
                    ? JSON.parse(feature.data)
                    : feature.data

                if (feature.layer === 'scattered') {
                    if (data.geometry) renderScatteredAreas(data.geometry)
                    continue
                }

                const phaseKey = API_LAYER_TO_PHASE[feature.layer] ?? data.type
                if (!phaseKey || !Object.prototype.hasOwnProperty.call(featureLayers, phaseKey)) {
                    skipped++; continue
                }

                const phase = PHASES.find(p => p.key === phaseKey)
                if (!phase) { skipped++; continue }

                let layer: L.Layer

                if (phase.drawType === 'circle') {
                    if (!data.lat || !data.lng) { skipped++; continue }
                    layer = L.circle([data.lat, data.lng], {
                        radius: data.radius ?? 50,
                        color: '#e74c3c', weight: 2, fillColor: '#e74c3c', fillOpacity: 0.15,
                    })
                } else if (phase.drawType === 'marker') {
                    if (!data.lat || !data.lng) { skipped++; continue }
                    if (phaseKey === 'houseEntrances' && !data.entranceTypeKey)
                        data.entranceTypeKey = feature.layer as 'main_entrance' | 'secondary_entrance'
                    const entranceColor = phaseKey === 'houseEntrances' && data.entranceTypeKey === 'secondary_entrance'
                        ? '#16a085'
                        : phase.color
                    const icon = createEntranceIcon(data.label, entranceColor)
                    layer = L.marker([data.lat, data.lng], { icon })
                    if (phase.key === 'cityCenter' && data.lat != null && data.lng != null) {
                        store.cityCenterMode   = 'city_center'
                        store.cityCenterLatLng = { lat: data.lat, lng: data.lng }
                    }
                } else if (phase.drawType === 'polyline') {
                    if (!data.coordinates?.length) { skipped++; continue }
                    layer = L.polyline(
                        data.coordinates.map(c => [c.lat, c.lng] as [number, number]),
                        { color: phase.color, weight: POLYLINE_WEIGHT }
                    )
                } else {
                    if (!data.coordinates?.length) { skipped++; continue }
                    const style = phase.key === 'areas'
                        ? areaStyle(data.areaTypeKey ?? feature.layer)
                        : (polygonStyles[phaseKey] ?? { color: phase.color, weight: 3, fillOpacity: 0.15 })
                    layer = L.polygon(
                        data.coordinates.map(c => [c.lat, c.lng] as [number, number]),
                        style
                    )
                }

                ;(layer as any)._dbId = feature.id
                if (phaseKey === 'roads') ctx.roadsDisplayLayer.addLayer(layer)
                else                      ctx.drawnItems.addLayer(layer)

                bindContextMenu(layer, feature.id, phaseKey)
                createPermanentLabel(layer, data.label, phaseKey)
                if (phaseKey === 'areas')     createAreaPerimeterLabel(layer, data.areaTypeKey ?? feature.layer)
                if (phaseKey === 'districts') createPolygonEdgeLabel(layer, getDistrictLabel(data.districtTypeKey ?? 'district', data.label), '#f39c12')
                bindHoverPopup(layer, buildPopup(data, phase, feature.id))

                featureLayers[phaseKey].push({ layer, data })
                loaded++
            } catch (err) { console.error('Load feature error:', err); skipped++ }
        }

        // ── Determine resume phase ────────────────────────────────────────────
        // Walk ALL phases and record the highest index that has data.
        // Never break early — a gap (e.g. no city center) must not hide later
        // phases (e.g. roads, entrances) that were drawn after it.
        let lastFilledPhase = -1
        for (let i = 0; i < PHASES.length; i++) {
            const key = PHASES[i].key
            const hasDatta = featureLayers[key]?.length > 0
                || (key === 'cityCenter' && store.cityCenterMode !== null)
            if (hasDatta) lastFilledPhase = i
        }
        // Resume at the phase after the last filled one, capped at the final phase.
        store.currentPhase = lastFilledPhase < 0
            ? 0
            : Math.min(lastFilledPhase + 1, PHASES.length - 1)

        const savedPhase = parseInt(localStorage.getItem('nars_resume_phase') ?? '', 10)
        if (!isNaN(savedPhase) && savedPhase >= 0 && savedPhase < PHASES.length)
            store.currentPhase = savedPhase
        localStorage.removeItem('nars_resume_phase')

        buildDrawControl(PHASES[store.currentPhase])
        syncCounts()
        refreshLayerVisibility()
        // buildDrawControl schedules pm.enableDraw via setTimeout(0), so Geoman's
        // draw-mode activation fires AFTER the synchronous refreshLayerVisibility.
        // Re-run visibility after Geoman settles.
        setTimeout(refreshLayerVisibility, 100)

        // Auto-generate naming panels if resuming into that phase after load.
        const currentKeyAfterLoad = PHASES[store.currentPhase]?.key
        if (currentKeyAfterLoad === 'namingPanels' && (featureLayers.namingPanels?.length ?? 0) === 0) {
            try {
                const { generateNamingPanels } = await import('./naming-panels')
                await generateNamingPanels()
            } catch (err) { console.error('Auto-generate naming panels after load error:', err) }
        }

        // Restore endpoint arrows for roads if directions have already been computed
        // (i.e. the user has moved past the Roads phase).
        const roadsPhaseIdx = PHASES.findIndex(p => p.key === 'roads')
        const isPastRoads   = store.currentPhase > roadsPhaseIdx
        if (featureLayers.roads.length > 0 && isPastRoads) {
            for (const entry of featureLayers.roads as LayerEntry[])
                addPolylineEndpoints(entry.layer)
        }

        console.log(`✓ Loaded ${loaded} features (${skipped} skipped)`)
    } catch (err) { console.error('Load error:', err); store.loadError = true }
}

// ─── USER / COMMUNE BOOTSTRAP ─────────────────────────────────────────────────

export async function loadUserAndCommune(): Promise<void> {
    try {
        const res = await apiFetch('/api/current_user')
        if (!res.ok) return
        const user = await res.json()
        store.user             = user
        store.municipalityName = user.commune?.name_fr ?? ''
        if (user.commune?.id)
            await displayCommuneBoundary(user.commune.id as number, user.commune.name_fr as string)
    } catch (err) { console.error('Commune nav error:', err) }
}
