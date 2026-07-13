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

// ─── RESPONSE HANDLING ────────────────────────────────────────────────────────

async function handleResponse(
  response: Response,
  context: { url: string; method: string },
): Promise<Response> {
  if (response.ok) return response

  // 401 = session expired → redirect to login with return URL
  if (response.status === 401) {
    const returnTo = encodeURIComponent(window.location.pathname + window.location.search)
    window.location.href = `${getLoginPath()}?returnTo=${returnTo}`
    // Throw to prevent further execution
    const error = createAuthError("Session expired. Redirecting to login.", {
      ...context,
      status: 401,
    })
    logError(error)
    throw error
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

export async function apiFetch(path: string, options: ApiFetchOptions = {}): Promise<Response> {
  const {
    timeout = DEFAULT_TIMEOUT,
    skipRetry = false,
    signal: externalSignal,
    ...fetchOptions
  } = options

  const url = apiUrl(path)
  const method = (options.method ?? "GET").toUpperCase()

  const context = { url, method }

  const executeRequest = async (): Promise<Response> => {
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), timeout)

    // Merge timeout signal with external signal (e.g. for AbortController from callers)
    const combinedSignal = externalSignal
      ? AbortSignal.any([controller.signal, externalSignal])
      : controller.signal

    // Build headers — add CSRF token for state-changing requests
    const csrfToken = getCsrfToken()
    const csrfHeaders: Record<string, string> = {}
    if (method !== "GET" && method !== "HEAD" && method !== "OPTIONS") {
      if (!csrfToken) {
        // CSRF token is missing — log a warning but proceed.
        // This can happen during SPA navigation or server misconfiguration.
        debugLog(
          "[API] CSRF token is missing for state-changing request. Proceeding without token.",
        )
      } else {
        csrfHeaders["X-CSRF-Token"] = csrfToken
      }
    }

    const hasBody = method === "POST" || method === "PUT" || method === "PATCH"
    try {
      const response = await fetch(url, {
        ...fetchOptions,
        credentials: "include",
        signal: combinedSignal,
        headers: {
          ...(hasBody ? { "Content-Type": "application/json" } : {}),
          ...csrfHeaders,
          ...fetchOptions.headers,
        },
      })

      clearTimeout(timeoutId)
      return await handleResponse(response, context)
    } catch (error) {
      clearTimeout(timeoutId)

      // AbortError = timeout
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
