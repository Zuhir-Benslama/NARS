// ─── GEOMETRY, BOUNDARY & SCATTERED AREAS ────────────────────────────────────

import { ctx }          from './state'
import { apiFetch }     from '../api'
import type { ScatteredRefreshResponse } from '../types'

declare const L: typeof import('leaflet')

// ─── SPATIAL HELPERS ─────────────────────────────────────────────────────────

let municipalLimitRings: L.LatLng[][] = []

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

export function polylineMidpoint(layer: L.Polyline): L.LatLng {
    const lls = layer.getLatLngs() as L.LatLng[]
    return lls[Math.floor(lls.length / 2)]
}

// A scattered polygon is a GeoJSON polygon with holes — the outer ring is the
// municipal boundary shape and the holes are the urban areas subtracted by
// ST_Difference. A point is in a scattered area if it is inside the outer ring
// AND outside every hole ring.
interface ScatteredPoly { outer: L.LatLng[]; holes: L.LatLng[][] }
let scatteredPolygons: ScatteredPoly[] = []

export function pointInScatteredArea(latlng: L.LatLng): boolean {
    return scatteredPolygons.some(({ outer, holes }) =>
        pointInRing(latlng, outer) && !holes.some(h => pointInRing(latlng, h))
    )
}

export function extractScatteredPolys(geom: GeoJSON.Geometry): ScatteredPoly[] {
    const toLatLngs = (ring: GeoJSON.Position[]): L.LatLng[] =>
        ring.map(c => L.latLng(c[1], c[0]))

    const fromPoly = (coords: GeoJSON.Position[][]): ScatteredPoly => ({
        outer: toLatLngs(coords[0]),
        holes: coords.slice(1).map(toLatLngs),
    })

    if (geom.type === 'Polygon')
        return [fromPoly(geom.coordinates)]
    if (geom.type === 'MultiPolygon')
        return geom.coordinates.map(fromPoly)
    return []
}

export function extractRings(geom: GeoJSON.Geometry): L.LatLng[][] {
    // Used only for the municipal boundary (no holes needed there).
    const toLatLngs = (ring: GeoJSON.Position[]): L.LatLng[] =>
        ring.map(c => L.latLng(c[1], c[0]))
    const rings: L.LatLng[][] = []
    if (geom.type === 'Polygon')
        geom.coordinates.forEach(r => rings.push(toLatLngs(r)))
    else if (geom.type === 'MultiPolygon')
        geom.coordinates.forEach(poly => poly.forEach(r => rings.push(toLatLngs(r))))
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
        // Boundary is display-only — never editable
        ctx.boundariesLayer.eachLayer((l: L.Layer) => {
            ;(l as any).pm?.setOptions?.({ pmIgnore: true })
        })
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
        // Only update the spatial hit-test data — scattered areas are not rendered
        // visually so as not to clutter the map.
        scatteredPolygons = extractScatteredPolys(geojson)
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
