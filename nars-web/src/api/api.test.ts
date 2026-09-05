import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

// The global test setup (src/test/setup.ts) mocks ../api with a stub fetch.
// This file needs the REAL apiFetch, so re-mock ../api to its original
// implementation for this suite only.
vi.mock("../api", async (importOriginal) => {
  return await importOriginal<typeof import("../api")>()
})

import { apiFetch, apiUrl } from "../api"
import { createConflictError } from "../lib/errors"
import { getApiBaseUrl, getLoginPath } from "../config"

function mockResponse(status: number, body: string | object = ""): Response {
  // 204/304 forbid a body — the Response constructor throws RangeError.
  const bodyless = status === 204 || status === 304
  const bodyStr = bodyless ? null : typeof body === "string" ? body : JSON.stringify(body)
  return new Response(bodyStr, {
    status,
    headers: { "Content-Type": "application/json" },
  })
}

/** Fetch mock that resolves once the request's abort signal fires. */
function signalAwareFetch(): ReturnType<typeof vi.fn> {
  return vi.fn(
    (_url: string, init?: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener("abort", () =>
          reject(new DOMException("The operation was aborted.", "AbortError")),
        )
      }),
  )
}

function clearCsrfMeta(): void {
  document.querySelector('meta[name="csrf-token"]')?.remove()
}

describe("apiUrl", () => {
  it("prepends the configured API base URL", () => {
    expect(apiUrl("/health")).toBe(`${getApiBaseUrl()}/health`)
  })
})

describe("apiFetch", () => {
  beforeEach(() => {
    clearCsrfMeta()
    // apiFetch logs errors via logError() (console.group/error) in dev — mute
    // the noise so assertions focus on behavior, not log output.
    vi.spyOn(console, "group").mockImplementation(() => {})
    vi.spyOn(console, "error").mockImplementation(() => {})
    vi.spyOn(console, "log").mockImplementation(() => {})
    vi.spyOn(console, "warn").mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
    clearCsrfMeta()
  })

  it("returns the response for a successful GET", async () => {
    const mockFetch = vi.fn(() => Promise.resolve(mockResponse(200, { data: "ok" })))
    vi.stubGlobal("fetch", mockFetch)

    const res = await apiFetch("/health")
    expect(res.status).toBe(200)
    await expect(res.json()).resolves.toEqual({ data: "ok" })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it("sets Content-Type for body-capable methods but not for GET", async () => {
    const mockFetch = vi.fn((_url: string, _init: RequestInit) =>
      Promise.resolve(mockResponse(200, {})),
    )
    vi.stubGlobal("fetch", mockFetch)

    await apiFetch("/no-body")
    const noBodyHeaders = mockFetch.mock.calls[0][1].headers as Record<string, string>
    expect(noBodyHeaders["Content-Type"]).toBeUndefined()

    await apiFetch("/with-body", { method: "POST" })
    const bodyHeaders = mockFetch.mock.calls[1][1].headers as Record<string, string>
    expect(bodyHeaders["Content-Type"]).toBe("application/json")
  })

  it("attaches X-CSRF-Token to state-changing requests and never lets callers override it", async () => {
    const meta = document.createElement("meta")
    meta.name = "csrf-token"
    meta.content = "test-csrf"
    document.head.appendChild(meta)

    const mockFetch = vi.fn((_url: string, _init: RequestInit) =>
      Promise.resolve(mockResponse(200, {})),
    )
    vi.stubGlobal("fetch", mockFetch)

    await apiFetch("/things", { method: "PUT", headers: { "X-CSRF-Token": "evil" }, body: "{}" })
    const headers = mockFetch.mock.calls[0][1].headers as Record<string, string>
    expect(headers["X-CSRF-Token"]).toBe("test-csrf")
    expect(headers["Content-Type"]).toBe("application/json")
  })

  it("does not attach a CSRF token to GET requests", async () => {
    const meta = document.createElement("meta")
    meta.name = "csrf-token"
    meta.content = "test-csrf"
    document.head.appendChild(meta)

    const mockFetch = vi.fn((_url: string, _init: RequestInit) =>
      Promise.resolve(mockResponse(200, {})),
    )
    vi.stubGlobal("fetch", mockFetch)

    await apiFetch("/things")
    const headers = mockFetch.mock.calls[0][1].headers as Record<string, string>
    expect(headers["X-CSRF-Token"]).toBeUndefined()
  })

  it("proceeds without a token in development when the CSRF token is missing", async () => {
    const mockFetch = vi.fn(() => Promise.resolve(mockResponse(200, {})))
    vi.stubGlobal("fetch", mockFetch)

    await apiFetch("/things", { method: "POST", body: "{}" })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it("aborts state-changing requests in production when the CSRF token is missing", async () => {
    vi.stubEnv("PROD", true)
    const mockFetch = vi.fn(() => Promise.resolve(mockResponse(200, {})))
    vi.stubGlobal("fetch", mockFetch)

    await expect(
      apiFetch("/things", { method: "POST", body: "{}", skipRetry: true }),
    ).rejects.toMatchObject({
      code: "NETWORK_ERROR",
    })
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it("redirects to login on 401 and throws an auth error", async () => {
    const mockFetch = vi.fn(() => Promise.resolve(mockResponse(401, {})))
    vi.stubGlobal("fetch", mockFetch)

    const locationMock = { href: "" }
    const originalLocation = window.location
    Object.defineProperty(window, "location", { value: locationMock, writable: true })
    try {
      await expect(apiFetch("/secret", { skipRetry: true })).rejects.toMatchObject({
        code: "AUTH_ERROR",
      })
      expect(locationMock.href).toBe(getLoginPath())
    } finally {
      Object.defineProperty(window, "location", { value: originalLocation, writable: true })
    }
  })

  it("silently refreshes once and replays the request when the access token expired", async () => {
    let endpointCalls = 0
    const mockFetch = vi.fn((url: string) => {
      if (url.endsWith("/api/refresh")) return Promise.resolve(mockResponse(200, {}))
      endpointCalls += 1
      // First attempt runs on the dead token; replay hits the fresh one.
      return Promise.resolve(mockResponse(endpointCalls === 1 ? 401 : 200, { data: "ok" }))
    })
    vi.stubGlobal("fetch", mockFetch)

    const res = await apiFetch("/secret", { skipRetry: true })
    expect(res.status).toBe(200)
    await expect(res.json()).resolves.toEqual({ data: "ok" })
    expect(mockFetch.mock.calls.filter(([u]) => String(u).endsWith("/api/refresh"))).toHaveLength(1)
    expect(endpointCalls).toBe(2)
  })

  it("does not redirect while a silent refresh recovers the session", async () => {
    let endpointCalls = 0
    vi.stubGlobal(
      "fetch",
      vi.fn((url: string) => {
        if (url.endsWith("/api/refresh")) return Promise.resolve(mockResponse(200, {}))
        endpointCalls += 1
        return Promise.resolve(mockResponse(endpointCalls === 1 ? 401 : 204))
      }),
    )

    const locationMock = { href: "" }
    const originalLocation = window.location
    Object.defineProperty(window, "location", { value: locationMock, writable: true })
    try {
      const res = await apiFetch("/secret", { skipRetry: true })
      expect(res.status).toBe(204)
      expect(locationMock.href).toBe("")
    } finally {
      Object.defineProperty(window, "location", { value: originalLocation, writable: true })
    }
  })

  it("shares a single refresh round trip across parallel expirations (single-flight)", async () => {
    let refreshCalls = 0
    let endpointCalls = 0
    const mockFetch = vi.fn((url: string) => {
      if (url.endsWith("/api/refresh")) {
        refreshCalls += 1
        // Small delay so both requests race into the same in-flight promise.
        return new Promise((resolve) => setTimeout(() => resolve(mockResponse(200, {})), 5))
      }
      endpointCalls += 1
      const n = endpointCalls
      return new Promise((resolve) =>
        setTimeout(() => resolve(mockResponse(n <= 2 ? 401 : 200, {})), 5),
      )
    })
    vi.stubGlobal("fetch", mockFetch)

    const [a, b] = await Promise.all([
      apiFetch("/one", { skipRetry: true }),
      apiFetch("/two", { skipRetry: true }),
    ])
    expect(a.status).toBe(200)
    expect(b.status).toBe(200)
    expect(refreshCalls).toBe(1)
  })

  it("redirects to login when the replay after a successful refresh is still unauthorized", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(mockResponse(401, {}))),
    )

    const locationMock = { href: "" }
    const originalLocation = window.location
    Object.defineProperty(window, "location", { value: locationMock, writable: true })
    try {
      await expect(apiFetch("/secret", { skipRetry: true })).rejects.toMatchObject({
        code: "AUTH_ERROR",
      })
      expect(locationMock.href).toBe(getLoginPath())
    } finally {
      Object.defineProperty(window, "location", { value: originalLocation, writable: true })
    }
  })

  it.each([
    [403, "AUTH_ERROR"],
    [404, "NOT_FOUND"],
    [409, "CONFLICT_ERROR"],
    [422, "VALIDATION_ERROR"],
    [500, "SERVER_ERROR"],
    [503, "SERVER_ERROR"],
  ])("maps HTTP %i to %s", async (status, code) => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(mockResponse(status, { detail: "nope" }))),
    )

    await expect(apiFetch("/x", { skipRetry: true })).rejects.toMatchObject({ code })
  })

  it("extracts the detail field from a JSON error body", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(mockResponse(404, { detail: "feature not found" }))),
    )

    await expect(apiFetch("/x", { skipRetry: true })).rejects.toMatchObject({
      code: "NOT_FOUND",
      message: "feature not found",
    })
  })

  it("maps a fetch TypeError to a NETWORK error", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.reject(new TypeError("Failed to fetch"))),
    )

    await expect(apiFetch("/x", { skipRetry: true })).rejects.toMatchObject({
      code: "NETWORK_ERROR",
    })
  })

  it("rethrows NarsError instances as-is", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.reject(createConflictError("conflict"))),
    )

    await expect(apiFetch("/x", { skipRetry: true })).rejects.toMatchObject({
      code: "CONFLICT_ERROR",
    })
  })

  it("maps an internal timeout to a TIMEOUT error", async () => {
    const mockFetch = signalAwareFetch()
    vi.stubGlobal("fetch", mockFetch)

    await expect(apiFetch("/slow", { timeout: 30, skipRetry: true })).rejects.toMatchObject({
      code: "TIMEOUT_ERROR",
    })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it("surfaces a caller abort before the request starts as an AbortError without retrying", async () => {
    const controller = new AbortController()
    const mockFetch = vi.fn(() =>
      Promise.reject(new DOMException("The operation was aborted.", "AbortError")),
    )
    vi.stubGlobal("fetch", mockFetch)

    controller.abort()
    const promise = apiFetch("/abort", { signal: controller.signal })

    await expect(promise).rejects.toMatchObject({ name: "AbortError" })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it("surfaces a caller abort mid-flight as an AbortError and does not retry", async () => {
    const controller = new AbortController()
    const mockFetch = signalAwareFetch()
    vi.stubGlobal("fetch", mockFetch)

    const promise = apiFetch("/abort", { signal: controller.signal })
    controller.abort()

    await expect(promise).rejects.toMatchObject({ name: "AbortError" })
    expect(mockFetch).toHaveBeenCalledTimes(1)
  })

  it("retries transient network errors up to maxRetries", async () => {
    vi.useFakeTimers()
    const mockFetch = vi.fn(() => Promise.reject(new TypeError("Failed to fetch")))
    vi.stubGlobal("fetch", mockFetch)
    try {
      const promise = apiFetch("/retry", { timeout: 1000 })
      // Attach the handler first so the eventual rejection is never flagged
      // as unhandled while fake timers advance.
      const assertion = expect(promise).rejects.toMatchObject({ code: "NETWORK_ERROR" })
      // initial attempt + 3 retries; backoff is 1s/2s/4s with up to 30% jitter
      await vi.advanceTimersByTimeAsync(15_000)

      await assertion
      expect(mockFetch).toHaveBeenCalledTimes(4)
    } finally {
      vi.useRealTimers()
    }
  })

  it("does not retry POST by default", async () => {
    vi.useFakeTimers()
    const mockFetch = vi.fn(() => Promise.reject(new TypeError("Failed to fetch")))
    vi.stubGlobal("fetch", mockFetch)
    try {
      const promise = apiFetch("/things", { method: "POST", body: "{}" })
      const assertion = expect(promise).rejects.toMatchObject({ code: "NETWORK_ERROR" })
      await vi.advanceTimersByTimeAsync(15_000)

      await assertion
      expect(mockFetch).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it("retries a query-style POST when the caller opts in via skipRetry: false", async () => {
    vi.useFakeTimers()
    const mockFetch = vi.fn(() => Promise.reject(new TypeError("Failed to fetch")))
    vi.stubGlobal("fetch", mockFetch)
    try {
      const promise = apiFetch("/validate/road", { method: "POST", body: "{}", skipRetry: false })
      const assertion = expect(promise).rejects.toMatchObject({ code: "NETWORK_ERROR" })
      await vi.advanceTimersByTimeAsync(15_000)

      await assertion
      expect(mockFetch).toHaveBeenCalledTimes(4)
    } finally {
      vi.useRealTimers()
    }
  })
})
