// ─── API HELPERS ──────────────────────────────────────────────────────────────
// Enhanced API helpers with error handling, timeout support, and retry logic.

import {
  NarsError,
  createNetworkError,
  createServerError,
  createAuthError,
  createTimeoutError,
  createNotFoundError,
  createConflictError,
  withRetry,
  logError,
} from "../lib/errors"
import { API_CONFIG, getApiBaseUrl, getLoginPath } from "../config"
import { getCsrfToken } from "../lib/csrf"
import { debugLog } from "../utils/debug"

const DEFAULT_TIMEOUT = API_CONFIG.defaultTimeout

export const apiUrl = (path: string): string => `${getApiBaseUrl()}${path}`

// ─── ERROR RESPONSE PARSING ───────────────────────────────────────────────────

interface ApiErrorResponse {
  detail?: string
  title?: string
  message?: string
  error?: string
  code?: string
}

function parseErrorResponse(body: string, status: number): { message: string; code?: string } {
  // Try to parse as JSON
  try {
    const data = JSON.parse(body) as ApiErrorResponse
    const message = data.detail ?? data.title ?? data.message ?? data.error ?? body
    return { message, code: data.code }
  } catch {
    // Not JSON, return raw body or status text
    return { message: body || `HTTP ${status}` }
  }
}

// ─── SILENT SESSION REFRESH ───────────────────────────────────────────────────

// Single-flight: N parallel requests expiring together must share ONE
// /api/refresh round trip instead of stampeding the endpoint and producing
// N redirect + error-log pairs. Resolves true only when the session was
// actually renewed.
let refreshInFlight: Promise<boolean> | null = null
let loginRedirectLogged = false

export function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const controller = new AbortController()
      const timeoutId = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT)
      const res = await fetch(apiUrl("/api/refresh"), {
        method: "POST",
        credentials: "include",
        signal: controller.signal,
      })
      clearTimeout(timeoutId)
      return res.ok
    } catch {
      // Offline / aborted / 5xx — the caller decides what failure means.
      return false
    }
  })().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

/** Hard fallback once the refresh cookie is also dead: bounce to login. */
function redirectToLogin(context: { url: string; method: string }): never {
  const error = createAuthError("Session expired. Redirecting to login.", {
    ...context,
    status: 401,
  })
  // Parallel 401s all land here; ship exactly one error-log batch for them.
  if (!loginRedirectLogged) {
    loginRedirectLogged = true
    logError(error)
  }
  // Idempotent assignment — safe even when several callers race here.
  window.location.href = getLoginPath()
  throw error
}

// ─── RESPONSE HANDLING ────────────────────────────────────────────────────────

async function handleResponse(
  response: Response,
  context: { url: string; method: string },
): Promise<Response> {
  if (response.ok) return response

  // 401 after a refresh attempt (or with no recoverable session) → login.
  // The silent-refresh path lives in executeRequest, which replays the
  // request once before this fallback runs.
  if (response.status === 401) {
    redirectToLogin(context)
  }

  const body = await response.text()
  const { message, code } = parseErrorResponse(body, response.status)

  let error: NarsError

  switch (response.status) {
    case 403:
      error = createAuthError(message, { ...context, status: response.status })
      break
    case 404:
      error = createNotFoundError(message, {
        ...context,
        status: response.status,
      })
      break
    case 409:
      error = createConflictError(message, {
        ...context,
        status: response.status,
        code,
      })
      break
    case 422:
      error = createServerError(`Validation failed: ${message}`, {
        ...context,
        status: response.status,
        code,
      })
      break
    case 500:
    case 502:
    case 503:
    case 504:
      error = createServerError(message, {
        ...context,
        status: response.status,
        code,
      })
      break
    default:
      error = createServerError(message, {
        ...context,
        status: response.status,
        code,
      })
  }

  logError(error)
  throw error
}

// ─── MAIN API FETCH ───────────────────────────────────────────────────────────

export interface ApiFetchOptions extends RequestInit {
  timeout?: number
  skipRetry?: boolean
}

// POST/PATCH are not retried by default: a client-side timeout can race with a
// successful server write, and retrying would duplicate the created resource.
// Idempotent methods (GET/HEAD/OPTIONS/PUT/DELETE) keep retrying on transient
// network/timeout errors. Callers of safe query-style POSTs can opt back in
// with `skipRetry: false`.
const NON_RETRYABLE_METHODS = new Set(["POST", "PATCH"])

export async function apiFetch(path: string, options: ApiFetchOptions = {}): Promise<Response> {
  const { timeout = DEFAULT_TIMEOUT, signal: externalSignal, ...fetchOptions } = options

  const url = apiUrl(path)
  const method = (options.method ?? "GET").toUpperCase()
  const skipRetry = options.skipRetry ?? NON_RETRYABLE_METHODS.has(method)

  const context = { url, method }

  const executeRequest = async (): Promise<Response> => {
    // Build headers — add CSRF token for state-changing requests
    const csrfToken = getCsrfToken()
    const csrfHeaders: Record<string, string> = {}
    if (method !== "GET" && method !== "HEAD" && method !== "OPTIONS") {
      if (!csrfToken) {
        // CSRF token is missing — this can happen during SPA navigation or
        // server misconfiguration. In production this is a security concern.
        if (import.meta.env.PROD) {
          throw createNetworkError(
            "[API] CSRF token is missing for state-changing request. Request aborted.",
            context,
          )
        } else {
          debugLog(
            "[API] CSRF token is missing for state-changing request. Proceeding without token.",
          )
        }
      } else {
        csrfHeaders["X-CSRF-Token"] = csrfToken
      }
    }

    const hasBody = method === "POST" || method === "PUT" || method === "PATCH"

    const sendRequest = async (): Promise<Response> => {
      // Fresh timeout per attempt: the silent-refresh replay gets its own
      // full budget, and a caller abort is merged in via AbortSignal.any.
      const controller = new AbortController()
      const timeoutId = setTimeout(() => controller.abort(), timeout)
      try {
        return await fetch(url, {
          ...fetchOptions,
          credentials: "include",
          signal: externalSignal
            ? AbortSignal.any([controller.signal, externalSignal])
            : controller.signal,
          headers: {
            ...fetchOptions.headers,
            ...(hasBody ? { "Content-Type": "application/json" } : {}),
            ...csrfHeaders,
          },
        })
      } finally {
        clearTimeout(timeoutId)
      }
    }

    try {
      let response = await sendRequest()

      // Access token expired mid-session: attempt one silent refresh and
      // replay the request once before the hard-redirect fallback in
      // handleResponse. A single replay cannot loop, and concurrent callers
      // share the same refresh via refreshSession's single-flight promise.
      if (response.status === 401 && (await refreshSession())) {
        response = await sendRequest()
      }

      return await handleResponse(response, context)
    } catch (error) {
      // Caller-initiated abort (stale request superseded, unmount, etc.) —
      // surface the original AbortError so callers can detect their own
      // cancellation, and so withRetry never treats it as a transient timeout.
      if (externalSignal?.aborted) {
        if (error instanceof DOMException && error.name === "AbortError") {
          throw error
        }
        throw new DOMException("The request was aborted.", "AbortError")
      }

      // Internal timeout — AbortError fired by our own controller
      if (error instanceof DOMException && error.name === "AbortError") {
        const timeoutError = createTimeoutError(`Request timed out after ${timeout}ms`, context)
        logError(timeoutError)
        throw timeoutError
      }

      // Network error (offline, DNS failure, etc.)
      // Use `instanceof TypeError` per the Fetch spec — different browsers
      // produce different error messages (Chrome: "Failed to fetch",
      // Firefox: "NetworkError when attempting to fetch resource",
      // Safari: "Load failed"), so message-based detection is unreliable.
      if (error instanceof TypeError) {
        const networkError = createNetworkError(
          "Network error. Please check your connection.",
          context,
          error,
        )
        logError(networkError)
        throw networkError
      }

      // Re-throw NarsError as-is
      if (error instanceof NarsError) {
        throw error
      }

      // Unknown error
      const unknownError = createNetworkError(
        error instanceof Error ? error.message : "Unknown error",
        context,
        error,
      )
      logError(unknownError)
      throw unknownError
    }
  }

  // Apply retry logic for transient errors (unless skipped)
  if (!skipRetry) {
    return withRetry(executeRequest, {}, (error, attempt) => {
      debugLog(`[API] Retry attempt ${attempt} for ${method} ${url} due to: ${error.code}`)
    })
  }

  return executeRequest()
}
