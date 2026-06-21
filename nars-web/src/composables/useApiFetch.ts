// ─── USE API FETCH COMPOSABLE ─────────────────────────────────────────────────
// Vue composable that wraps apiFetch and returns reactive data, error, and
// loading state — eliminating boilerplate in every component that calls the API.

import { ref, type Ref } from "vue"
import { apiFetch, type ApiFetchOptions } from "../api"
import { isNarsError, logError, createNetworkError } from "../lib/errors"

export interface UseApiFetchReturn<T> {
  /** Reactive response data, or null if not yet loaded / error occurred. */
  data: Ref<T | null>
  /** Reactive error object, or null if the last request succeeded. */
  error: Ref<Error | null>
  /** True while the request is in flight. */
  isLoading: Ref<boolean>
  /** Execute the request. Returns the parsed JSON on success. */
  execute: (path: string, options?: ApiFetchOptions) => Promise<T | null>
  /** Reset the composable to its initial state. */
  reset: () => void
}

/**
 * Create a reactive API fetch state.
 *
 * @example
 * ```ts
 * const { data, error, isLoading, execute } = useApiFetch<UserInfo>()
 * await execute('/api/current_user')
 * if (data.value) console.log(data.value.name)
 * ```
 */
export function useApiFetch<T = unknown>(): UseApiFetchReturn<T> {
  const data: Ref<T | null> = ref(null)
  const error: Ref<Error | null> = ref(null)
  const isLoading = ref(false)

  async function execute(path: string, options?: ApiFetchOptions): Promise<T | null> {
    isLoading.value = true
    error.value = null

    try {
      const response = await apiFetch(path, options)
      data.value = (await response.json()) as T
      return data.value
    } catch (err) {
      error.value = err instanceof Error ? err : new Error(String(err))
      return null
    } finally {
      isLoading.value = false
    }
  }

  function reset(): void {
    data.value = null
    error.value = null
    isLoading.value = false
  }

  return { data, error, isLoading, execute, reset }
}

export type ApiRequestResult<T> =
  | { success: true; data: T }
  | { success: false; error: Error; status?: number }

/**
 * Execute a one-off API request and return a result object that
 * distinguishes success from failure.
 *
 * @example
 * ```ts
 * const result = await apiRequest<UserInfo>('/api/current_user')
 * if (result.success) {
 *   console.log(result.data.name)
 * } else {
 *   console.error(result.error)
 * }
 * ```
 */
export async function apiRequest<T = unknown>(
  path: string,
  options?: ApiFetchOptions,
): Promise<ApiRequestResult<T>> {
  try {
    const response = await apiFetch(path, options)
    if (!response.ok) {
      return { success: false, error: new Error(`HTTP ${response.status}`), status: response.status }
    }
    const data = (await response.json()) as T
    return { success: true, data }
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err))
    logError(createNetworkError(`apiRequest failed: ${path}`, { url: path }, error))
    return { success: false, error }
  }
}

/**
 * Type guard to check if an error from useApiFetch is a NarsError.
 *
 * @example
 * ```ts
 * if (isNarsFetchError(error.value)) {
 *   console.error(error.value.getTechnicalDetails())
 * }
 * ```
 */
export function isNarsFetchError(err: Error | null): err is import("../lib/errors").NarsError {
  return isNarsError(err)
}
