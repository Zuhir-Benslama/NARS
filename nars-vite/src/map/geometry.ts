// ─── GEOMETRY, BOUNDARY & SCATTERED AREAS ────────────────────────────────────

import { ctx }          from './state'
import { apiFetch }     from '../api'
import { scatteredStyle } from './styles'
import type { ScatteredRefreshResponse } from '../types'

declare const L: typeof import('leaflet')

// ─── SPATIAL HELPERS ─────────────────────────────────────────────────────────

let municipalLimitRings: L.LatLng[][] = []
let scatteredPolygons:   L.LatLng[][] = []

function pointInRing(latlng: L.LatLng, ring: L.LatLng[]): boolean {
    let inside = false
    const x = latlng.lat, y = latlng.lng
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const xi = ring[i].lat, yi = ring[i].lng, xj = ring[j].lat, yj = ring[j].lng
        if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
            inside = !inside
    }
    return inside
}

export function pointInMunicipalLimit(latlng: L.LatLng): boolean {
    if (municipalLimitRings.length === 0) return true
    return municipalLimitRings.some(r => pointInRing(latlng, r))
}

export function pointInScatteredArea(latlng: L.LatLng): boolean {
    return scatteredPolygons.some(r => pointInRing(latlng, r))
}

export function polylineMidpoint(layer: L.Polyline): L.LatLng {
    const lls = layer.getLatLngs() as L.LatLng[]
    return lls[Math.floor(lls.length / 2)]
}

export function extractRings(geom: GeoJSON.Geometry): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    const processRing = (coords: GeoJSON.Position[] | GeoJSON.Position[][] | GeoJSON.Position[][][]): void => {
        if (!coords?.length) return
        if (typeof (coords[0] as GeoJSON.Position)[0] === 'number') {
            rings.push((coords as GeoJSON.Position[]).map(c => L.latLng(c[1], c[0])))
        } else {
            (coords as (GeoJSON.Position[] | GeoJSON.Position[][])[]).forEach(c => processRing(c as any))
        }
    }
    if (geom.type === 'Polygon')           processRing(geom.coordinates)
    else if (geom.type === 'MultiPolygon') geom.coordinates.forEach(p => processRing(p))
    else                                   processRing((geom as any).coordinates)
    return rings
}

// ─── MUNICIPALITY BOUNDARY ────────────────────────────────────────────────────

export async function displayCommuneBoundary(communeId: number, communeName: string): Promise<void> {
    try {
        if (ctx.boundariesLayer) { ctx.map.removeLayer(ctx.boundariesLayer); ctx.boundariesLayer = null }
        const res = await apiFetch(`/api/commune/${communeId}/boundary`)
        if (!res.ok) return
        const data = await res.json() as { geometry: string | GeoJSON.Geometry; commune_name?: string }
        const geojson: GeoJSON.Geometry = typeof data.geometry === 'string' ? JSON.parse(data.geometry) : data.geometry
        if (!geojson?.type) return

        municipalLimitRings = extractRings(geojson)
        ctx.boundariesLayer = L.geoJSON(geojson, {
            style: { color: '#e74c3c', weight: 2.5, fillOpacity: 0.03, fillColor: '#e74c3c' },
        }).addTo(ctx.map)
        ctx.map.fitBounds(ctx.boundariesLayer.getBounds(), { padding: [50, 50], maxZoom: 14 })
    } catch (e) { console.error('Boundary error:', e) }
}

// ─── SCATTERED AREAS ──────────────────────────────────────────────────────────

export function renderScatteredAreas(geoJsonStr: string | GeoJSON.Geometry): void {
    ctx.scatteredLayer.clearLayers()
    scatteredPolygons = []
    if (!geoJsonStr) return
    try {
        const geojson: GeoJSON.Geometry = typeof geoJsonStr === 'string' ? JSON.parse(geoJsonStr) : geoJsonStr
        if (!geojson?.type) return
        scatteredPolygons = extractRings(geojson)
        L.geoJSON(geojson, {
            style: scatteredStyle,
            onEachFeature(_, layer) {
                (layer as L.Path).bindTooltip('Scattered Area', { direction: 'center', className: 'boundary-tooltip' })
            },
        }).addTo(ctx.scatteredLayer)
    } catch (e) { console.error('Scattered render error:', e) }
}

export async function refreshScatteredAreas(): Promise<void> {
    try {
        const res = await apiFetch('/api/areas/refresh-scattered', { method: 'POST' })
        if (!res.ok) return
        const data = await res.json() as ScatteredRefreshResponse
        if (data.geojson) renderScatteredAreas(data.geojson)
        else ctx.scatteredLayer.clearLayers()
    } catch (e) { console.error('Scatter refresh error:', e) }
}
