import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { NarsError, ErrorCode } from "./errors"

const mockFetch = vi.fn()
vi.stubGlobal("fetch", mockFetch)
vi.stubGlobal("navigator", { sendBeacon: vi.fn() })

vi.mock("../config", () => ({
  getApiBaseUrl: () => "",
  isDev: () => false,
}))

async function freshLogger() {
  return await import("./logger")
}

function firePagehide(): void {
  window.dispatchEvent(new Event("pagehide"))
}

describe("logger", () => {
  beforeEach(() => {
    vi.useFakeTimers()
    mockFetch.mockReset()
    mockFetch.mockResolvedValue({ ok: true })
    vi.mocked(navigator.sendBeacon).mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe("captureError", () => {
    it("pushes a log entry with error details", async () => {
      const logger = await freshLogger()
      const error = new NarsError(ErrorCode.VALIDATION, "Something failed", {
        url: "/test",
        method: "GET",
      })

      logger.captureError(error)
      await logger.flushLogs()

      expect(mockFetch).toHaveBeenCalledTimes(1)
      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.logs).toHaveLength(1)
      expect(body.logs[0]).toMatchObject({
        level: "error",
        code: "VALIDATION_ERROR",
        message: "Something failed",
        url: "/test",
        method: "GET",
      })
    })

    it("includes context when provided", async () => {
      const logger = await freshLogger()
      const error = new NarsError(ErrorCode.UNKNOWN, "err")
      logger.captureError(error, { action: "info" })
      await logger.flushLogs()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      const context = JSON.parse(body.logs[0].context)
      expect(context).toMatchObject({ action: "info" })
    })
  })

  describe("batching", () => {
    it("flushes when batch reaches limit", async () => {
      const logger = await freshLogger()
      for (let i = 0; i < 20; i++) {
        logger.captureError(new NarsError(ErrorCode.UNKNOWN, `err ${i}`))
      }

      await vi.advanceTimersByTimeAsync(100)
      expect(mockFetch).toHaveBeenCalledTimes(1)

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.logs).toHaveLength(20)
    })

    it("flushes remaining entries on flushLogs", async () => {
      const logger = await freshLogger()
      logger.captureError(new NarsError(ErrorCode.UNKNOWN, "single"))
      await logger.flushLogs()

      expect(mockFetch).toHaveBeenCalledTimes(1)
      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.logs).toHaveLength(1)
    })

    it("sends batch via fetch to /api/logs", async () => {
      const logger = await freshLogger()
      logger.captureError(new NarsError(ErrorCode.UNKNOWN, "test"))
      await logger.flushLogs()

      expect(mockFetch).toHaveBeenCalledWith(
        "/api/logs",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
        }),
      )
    })
  })

  describe("retry", () => {
    it("retries on fetch failure", async () => {
      const logger = await freshLogger()
      mockFetch.mockRejectedValueOnce(new TypeError("Network error"))
      mockFetch.mockResolvedValueOnce({ ok: true })

      logger.captureError(new NarsError(ErrorCode.UNKNOWN, "retry test"))
      await logger.flushLogs()

      expect(mockFetch).toHaveBeenCalledTimes(2)
    })

    it("gives up after max retries", async () => {
      const logger = await freshLogger()
      mockFetch.mockRejectedValue(new TypeError("Network error"))

      logger.captureError(new NarsError(ErrorCode.UNKNOWN, "fail"))
      await logger.flushLogs()

      expect(mockFetch).toHaveBeenCalledTimes(2)
    })
  })

  describe("pagehide flush", () => {
    it("sends pending entries via fetch keepalive with the CSRF header, not sendBeacon", async () => {
      const logger = await freshLogger()
      document.head.insertAdjacentHTML("beforeend", '<meta name="csrf-token" content="tok-123">')

      logger.captureError(new NarsError(ErrorCode.UNKNOWN, "last words"))
      firePagehide()

      // keepalive fetch is fire-and-forget; the call itself is synchronous.
      expect(navigator.sendBeacon).not.toHaveBeenCalled()
      expect(mockFetch).toHaveBeenCalledTimes(1)
      const [, init] = mockFetch.mock.calls[0]
      expect(init).toMatchObject({
        method: "POST",
        credentials: "include",
        keepalive: true,
      })
      expect(init.headers).toMatchObject({
        "Content-Type": "application/json",
        "X-CSRF-Token": "tok-123",
      })
      expect(JSON.parse(init.body).logs).toHaveLength(1)
      document.querySelector('meta[name="csrf-token"]')?.remove()
    })

    it("trims entries until the keepalive body fits the 64KB budget", async () => {
      const logger = await freshLogger()
      // 19 entries: below BATCH_LIMIT so nothing auto-flushes and the whole
      // batch is still pending when pagehide fires.
      for (let i = 0; i < 19; i++) {
        logger.captureError(new NarsError(ErrorCode.UNKNOWN, "x".repeat(8_000)))
      }
      firePagehide()

      expect(mockFetch).toHaveBeenCalledTimes(1)
      const [, init] = mockFetch.mock.calls[0]
      expect(init.keepalive).toBe(true)
      expect(init.body.length).toBeLessThanOrEqual(60_000)
      expect(JSON.parse(init.body).logs.length).toBeLessThan(19)
    })

    it("does nothing when the batch is empty", async () => {
      await freshLogger()
      firePagehide()
      expect(mockFetch).not.toHaveBeenCalled()
    })
  })

  describe("privacy", () => {
    it("strips query strings from the url field and keeps them out of the context blob", async () => {
      const logger = await freshLogger()
      logger.captureError(
        new NarsError(ErrorCode.NETWORK, "search failed", {
          url: "https://api.test/api/features?search=secret-term",
          method: "GET",
          action: "load",
        }),
      )
      await logger.flushLogs()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.logs[0].url).toBe("https://api.test/api/features")
      const ctx = JSON.parse(body.logs[0].context)
      expect(ctx.url).toBeUndefined()
      expect(ctx.method).toBeUndefined()
      expect(JSON.stringify(body)).not.toContain("secret-term")
      expect(ctx.action).toBe("load")
    })
  })
})
