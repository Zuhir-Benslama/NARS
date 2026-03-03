import { apiFetch } from './api.js';

// POST /api/validate/road
export async function validateRoad(layer) {
    const coords = layer.getLatLngs().map(ll => ({ lat: ll.lat, lng: ll.lng }));
    try {
        const res = await apiFetch('/api/validate/road', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ coordinates: coords }),
        });
        if (!res.ok) return { valid: false, error: 'Road validation request failed.' };
        return await res.json();
    } catch { return { valid: false, error: 'Cannot reach validation service.' }; }
}

// POST /api/validate/district
export async function validateDistrict(layer) {
    const coords = layer.getLatLngs()[0].map(ll => ({ lat: ll.lat, lng: ll.lng }));
    try {
        const res = await apiFetch('/api/validate/district', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ coordinates: coords }),
        });
        if (!res.ok) return { valid: false, error: 'District validation request failed.' };
        return await res.json();
    } catch { return { valid: false, error: 'Cannot reach validation service.' }; }
}

// GET /api/validate/districts/coverage
export async function checkDistrictCoverage() {
    try {
        const res = await apiFetch('/api/validate/districts/coverage');
        if (!res.ok) return { covered: false, message: 'Coverage check failed.' };
        return await res.json();
    } catch { return { covered: false, message: 'Cannot reach validation service.' }; }
}

// GET /api/validate/area/main-urban-exists
export async function checkMainUrbanExists() {
    try {
        const res = await apiFetch('/api/validate/area/main-urban-exists');
        if (!res.ok) return false;
        const d = await res.json();
        return d.exists;
    } catch { return false; }
}

// POST /api/road-side  →  { side: 'left'|'right', suggestedNumber: int }
export async function getRoadSide(roadDbId, lat, lng) {
    try {
        const res = await apiFetch('/api/road-side', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ roadId: roadDbId, lat, lng }),
        });
        if (!res.ok) return null;
        return await res.json();
    } catch { return null; }
}
