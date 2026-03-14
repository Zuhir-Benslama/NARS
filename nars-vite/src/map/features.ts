// ─── FEATURE DATA, SAVE & MODAL HELPERS ──────────────────────────────────────

import { PHASES }              from '../phases'
import { store, featureLayers, currentModalLayer } from '../store'
import { apiFetch }            from '../api'
import { getRoadSide, checkMainUrbanExists } from '../validation'
import type { FeatureData, LayerEntry, SaveResult } from '../types'

declare const L: typeof import('leaflet')

// ─── FEATURE DATA BUILDER ────────────────────────────────────────────────────

export function buildFeatureData(layer: L.Layer, phase: typeof PHASES[number], modalResult: Record<string, unknown>): FeatureData {
    const base: FeatureData = {
        type:           phase.key,
        label:          modalResult.label as string,
        decisionNumber: modalResult.decisionNumber as string,
        decisionDate:   modalResult.decisionDate as string,
        ...modalResult as Partial<FeatureData>,
    }
    if (phase.drawType === 'marker') {
        const ll = (layer as L.Marker).getLatLng()
        return { ...base, lat: ll.lat, lng: ll.lng }
    }
    if (phase.drawType === 'circle') {
        const ll = (layer as L.Circle).getLatLng()
        const radius = (layer as L.Circle).getRadius()
        return { ...base, lat: ll.lat, lng: ll.lng, radius }
    }
    const lls = phase.drawType === 'polygon'
        ? ((layer as L.Polygon).getLatLngs()[0] as L.LatLng[])
        : ((layer as L.Polyline).getLatLngs() as L.LatLng[])

    // PostGIS/GEOS requires closed rings — close if snapping drifted first/last apart
    let coords = lls.map(ll => ({ lat: ll.lat, lng: ll.lng }))
    if (phase.drawType === 'polygon' && coords.length >= 3) {
        const first = coords[0], last = coords[coords.length - 1]
        if (first.lat !== last.lat || first.lng !== last.lng)
            coords = [...coords, { lat: first.lat, lng: first.lng }]
    }
    return { ...base, coordinates: coords }
}

// ─── API SHAPE MAPPING ────────────────────────────────────────────────────────

export function toApiSaveShape(fd: FeatureData): { type: string; layer: string } | null {
    switch (fd.type) {
        case 'areas':           return { type: 'area',            layer: fd.areaTypeKey     ?? 'central_urban' }
        case 'cityCenter':      return { type: 'city_center',     layer: 'city_center' }
        case 'districts':       return { type: 'district',        layer: fd.districtTypeKey ?? 'district' }
        case 'roads':           return { type: 'road',            layer: fd.roadTypeKey     ?? 'street' }
        case 'houseEntrances':  return { type: 'house_entrance',  layer: fd.entranceTypeKey ?? 'main_entrance' }
        case 'publicBuildings': return { type: 'public_building', layer: fd.buildingTypeKey ?? 'public_building' }
        case 'publicSpaces':    return { type: 'public_space',    layer: fd.spaceTypeKey    ?? 'garden' }
        case 'namingPanels':    return { type: 'naming_panel',    layer: 'naming_panel' }
        default: return null
    }
}

// ─── DATABASE SAVE ────────────────────────────────────────────────────────────

export async function saveToDatabase(featureData: FeatureData): Promise<SaveResult> {
    try {
        const shape = toApiSaveShape(featureData)
        if (!shape) return { ok: false, error: `Unknown type '${featureData.type}'.` }

        const res = await apiFetch('/api/save', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ type: shape.type, layer: shape.layer, label: featureData.label, data: featureData }),
        })
        if (!res.ok) {
            const raw = await res.text()
            let detail = raw || `HTTP ${res.status}`
            try { const p = JSON.parse(raw) as { detail?: string; title?: string }; detail = p?.detail ?? p?.title ?? detail } catch { /* ignore */ }
            return { ok: false, error: `HTTP ${res.status}: ${String(detail).slice(0, 240)}` }
        }
        return { ok: true, data: await res.json() as { id: number } }
    } catch (err) {
        return { ok: false, error: (err as Error)?.message ?? 'Network error' }
    }
}

// ─── MODAL EXTRA PREPARATION ──────────────────────────────────────────────────

export async function prepareModalExtras(phase: typeof PHASES[number], _layer: L.Layer): Promise<void> {
    const m = store.modal

    if (phase.key === 'areas') {
        m.mainUrbanExists = await checkMainUrbanExists()
        if (!m.mainUrbanExists && store.municipalityName) m.label = store.municipalityName
        m.areaTypeKey = m.mainUrbanExists ? 'secondary_urban' : 'central_urban'
    }

    if (phase.key === 'houseEntrances') {
        m.roadOptions = featureLayers.roads.map((r, i) => ({
            idx:   i,
            label: r.data.label || `Road ${i + 1}`,
            dbId:  (r.layer as any)._dbId as number,
        }))
        m.mainEntranceOptions = featureLayers.houseEntrances
            .filter((e: LayerEntry) => e.data.entranceTypeKey === 'main_entrance')
            .map((e, i) => ({
                idx:   i,
                label: e.data.label || `Entrance ${i + 1}`,
                dbId:  (e.layer as any)._dbId as number,
            }))
    }
}

// ─── ROAD-SIDE & BIS HELPERS ──────────────────────────────────────────────────

export async function fetchRoadSide(roadDbId: number, _roadIdx: number): Promise<void> {
    const m = store.modal
    m.entranceSideLoading = true
    m.entranceSide        = null
    m.entranceNumber      = null
    try {
        const ll = currentModalLayer ? (currentModalLayer as L.Marker).getLatLng() : null
        if (!ll) return
        const result = await getRoadSide(roadDbId, ll.lat, ll.lng)
        if (result) {
            m.entranceSide   = result.side
            m.entranceNumber = result.suggestedNumber
        }
    } finally {
        m.entranceSideLoading = false
    }
}

export function computeBisNumber(mainEntranceDbId: number): void {
    const count = featureLayers.houseEntrances.filter((s: LayerEntry) =>
        s.data.entranceTypeKey === 'secondary_entrance' &&
        s.data.mainEntranceDbId === mainEntranceDbId).length
    store.modal.bisNumber = count + 1
    store.modal.label     = 'BIS' + String(count + 1).padStart(2, '0')
}
