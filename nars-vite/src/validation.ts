import L from 'leaflet'
import { apiFetch } from './api'
import type {
    ValidateRoadResponse,
    ValidateDistrictResponse,
    DistrictCoverageResponse,
    RoadSideResponse,
} from './types'

// POST /api/validate/road
export async function validateRoad(layer: L.Polyline): Promise<ValidateRoadResponse> {
    const coords = layer.getLatLngs().map((ll) => {
        const l = ll as L.LatLng
        return { lat: l.lat, lng: l.lng }
    })
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
export async function validateDistrict(layer: L.Polygon): Promise<ValidateDistrictResponse> {
    const lls = layer.getLatLngs()[0] as L.LatLng[]
    const coords = lls.map((ll) => ({ lat: ll.lat, lng: ll.lng }))
    try {
        const res = await apiFetch('/api/validate/district', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ coordinates: coords }),
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
