// API HELPERS
// Thin wrapper around fetch that attaches the HttpOnly access_token cookie.
//
// credentials: 'include' ensures the cookie is sent in ALL cases:
//   - dev:  Vite proxies /api/* to localhost:5000 (different origin technically)
//   - prod: backend serves both page and API on same origin
//
// If you need a different API origin, set VITE_API_BASE in a .env file.

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

export const apiUrl = path => `${API_BASE}${path}`

export const apiFetch = (path, options = {}) =>
    fetch(apiUrl(path), {
        ...options,
        credentials: 'include',
    })
