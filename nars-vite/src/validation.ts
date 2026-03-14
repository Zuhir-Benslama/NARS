import L from 'leaflet'
import * as turf from '@turf/turf'
import { apiFetch } from './api'
import type {
    ValidateRoadResponse,
    ValidateDistrictResponse,
    DistrictCoverageResponse,
    RoadSideResponse,
} from './types'

const MIN_ROAD_LENGTH_M = 10

// POST /api/validate/road
export async function validateRoad(layer: L.Polyline): Promise<ValidateRoadResponse> {
    const coords = layer.getLatLngs().map((ll) => {
        const l = ll as L.LatLng
        return { lat: l.lat, lng: l.lng }
    })

    // Client-side minimum length check — avoids a round-trip for trivially short roads.
    if (coords.length >= 2) {
        const line   = turf.lineString(coords.map(c => [c.lng, c.lat]))
        const metres = turf.length(line, { units: 'meters' })
        if (metres < MIN_ROAD_LENGTH_M)
            return { valid: false, error: `Road is too short (${metres.toFixed(1)} m). Minimum length is ${MIN_ROAD_LENGTH_M} m.` }
    }

    try {
        const res = await apiFetch('/api/validate/road', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ coordinates: coords }),
        })
        if (!res.ok) return { valid: false, error: 'Road validation request failed.' }
        return await res.json() as ValidateRoadResponse
    } catch {
        return { valid: false, error: 'Cannot reach validation service.' }
    }
}

// POST /api/validate/district
export async function validateDistrict(layer: L.Polygon, districtTypeKey?: string): Promise<ValidateDistrictResponse> {
    const lls = layer.getLatLngs()[0] as L.LatLng[]
    let coords = lls.map((ll) => ({ lat: ll.lat, lng: ll.lng }))
    // PostGIS requires closed ring — ensure first and last points are identical
    if (coords.length >= 3) {
        const first = coords[0], last = coords[coords.length - 1]
        if (first.lat !== last.lat || first.lng !== last.lng) {
            coords = [...coords, { lat: first.lat, lng: first.lng }]
        }
    }
    try {
        const res = await apiFetch('/api/validate/district', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ coordinates: coords, districtTypeKey }),
        })
        if (!res.ok) return { valid: false, error: 'District validation request failed.' }
        return await res.json() as ValidateDistrictResponse
    } catch {
        return { valid: false, error: 'Cannot reach validation service.' }
    }
}

// GET /api/validate/districts/coverage
export async function checkDistrictCoverage(): Promise<DistrictCoverageResponse> {
    try {
        const res = await apiFetch('/api/validate/districts/coverage')
        if (!res.ok) return { covered: false, message: 'Coverage check failed.' }
        return await res.json() as DistrictCoverageResponse
    } catch {
        return { covered: false, message: 'Cannot reach validation service.' }
    }
}

// GET /api/validate/area/main-urban-exists
export async function checkMainUrbanExists(): Promise<boolean> {
    try {
        const res = await apiFetch('/api/validate/area/main-urban-exists')
        if (!res.ok) return false
        const d = await res.json() as { exists: boolean }
        return d.exists
    } catch {
        return false
    }
}

// POST /api/road-side → { side: 'left'|'right', suggestedNumber: number }
export async function getRoadSide(
    roadDbId: number,
    lat: number,
    lng: number,
): Promise<RoadSideResponse | null> {
    try {
        const res = await apiFetch('/api/road-side', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ roadId: roadDbId, lat, lng }),
        })
        if (!res.ok) return null
        return await res.json() as RoadSideResponse
    } catch {
        return null
    }
}
