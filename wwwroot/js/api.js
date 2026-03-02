// ─── API HELPERS ──────────────────────────────────────────────────────────────
// Thin wrapper around fetch that prepends the correct base URL and sets
// same-origin credentials (cookies) for all requests.

const API_BASE = window.location.protocol === 'file:' ? 'http://localhost:5000' : '';

export const apiUrl = path => `${API_BASE}${path}`;

export const apiFetch = (path, options = {}) =>
    fetch(apiUrl(path), {
        ...options,
        credentials: options.credentials ?? (API_BASE ? 'omit' : 'same-origin'),
    });
