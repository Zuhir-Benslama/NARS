import { apiFetch } from './api'
import { NarsError } from './errors'
import { VALIDATION_CONFIG } from './config'
import { debugError } from './utils/debug'
import type {
    ValidateRoadResponse,
    ValidateDistrictResponse,
    DistrictCoverageResponse,
    RoadSideResponse,
    LatLng,
} from './types'

const MIN_ROAD_LENGTH_M = VALIDATION_CONFIG.minRoadLengthMeters

// POST /api/validate/road
export async function validateRoad(coordinates: LatLng[]): Promise<ValidateRoadResponse> {
    // Client-side minimum length check
    if (coordinates.length >= 2) {
        let turfHelpers: typeof import('@turf/helpers')
        let turfLength: typeof import('@turf/length')
        try {
            ;[turfHelpers, turfLength] = await Promise.all([import('@turf/helpers'), import('@turf/length')])
        } catch {
            return { valid: false, error: 'Local validation library failed to load.' }
        }
        const line = turfHelpers.lineString(coordinates.map((c) => [c.lng, c.lat]))
        const metres = turfLength.length(line, { units: 'meters' })
        if (metres < MIN_ROAD_LENGTH_M)
            return {
                valid: false,
                error: `Road is too short (${metres.toFixed(1)} m). Minimum length is ${MIN_ROAD_LENGTH_M} m.`,
            }
    }

    try {
        return (await apiFetch('/api/validate/road', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ coordinates }),
        }).then((r) => r.json())) as Promise<ValidateRoadResponse>
    } catch (err) {
        const message =
            err instanceof NarsError
                ? err.message
                : err instanceof TypeError
                  ? 'Cannot reach validation service.'
                  : 'Road validation encountered an unexpected error.'
        return { valid: false, error: message }
    }
}

// POST /api/validate/district
export async function validateDistrict(
    coordinates: LatLng[],
    districtTypeKey?: string,
): Promise<ValidateDistrictResponse> {
    // PostGIS requires closed ring — ensure first and last points are identical
    let coords = [...coordinates]
    if (coords.length >= 3) {
        const first = coords[0],
            last = coords[coords.length - 1]
        if (first.lat !== last.lat || first.lng !== last.lng) {
            coords = [...coords, { lat: first.lat, lng: first.lng }]
        }
    }
    try {
        return (await apiFetch('/api/validate/district', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ coordinates: coords, districtTypeKey }),
        }).then((r) => r.json())) as Promise<ValidateDistrictResponse>
    } catch (err) {
        const message = err instanceof NarsError ? err.message : 'Cannot reach validation service.'
        return { valid: false, error: message }
    }
}

// GET /api/validate/districts/coverage
export async function checkDistrictCoverage(): Promise<DistrictCoverageResponse> {
    try {
        return (await apiFetch('/api/validate/districts/coverage').then((r) =>
            r.json(),
        )) as Promise<DistrictCoverageResponse>
    } catch (err) {
        const message = err instanceof NarsError ? err.message : 'Cannot reach validation service.'
        return { covered: false, message }
    }
}

// GET /api/validate/area/main-urban-exists
export async function checkMainUrbanExists(): Promise<boolean> {
    try {
        const d = (await apiFetch('/api/validate/area/main-urban-exists').then((r) => r.json())) as { exists: boolean }
        return d.exists
    } catch (err) {
        debugError('checkMainUrbanExists failed:', err)
        return false
    }
}

// POST /api/road-side → { side: 'left'|'right', suggestedNumber: number }
export async function getRoadSide(roadDbId: string, lat: number, lng: number): Promise<RoadSideResponse | null> {
    try {
        return (await apiFetch('/api/road-side', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ roadId: roadDbId, lat, lng }),
        }).then((r) => r.json())) as Promise<RoadSideResponse>
    } catch (err) {
        debugError('getRoadSide failed:', err)
        return null
    }
}
