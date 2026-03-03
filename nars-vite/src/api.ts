// ─── API HELPERS ──────────────────────────────────────────────────────────────

const API_BASE: string = (import.meta.env.VITE_API_BASE as string) ?? ''

export const apiUrl = (path: string): string => `${API_BASE}${path}`

export const apiFetch = (path: string, options: RequestInit = {}): Promise<Response> =>
    fetch(apiUrl(path), {
        ...options,
        credentials: 'include',
    })
