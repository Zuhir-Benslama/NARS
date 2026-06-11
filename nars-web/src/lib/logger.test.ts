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
      const error = new NarsError(ErrorCode.VALIDATION, "Something failed", { url: "/test", method: "GET" })

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
      logger.captureError(error, { extra: "info" })
      await logger.flushLogs()

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      const context = JSON.parse(body.logs[0].context)
      expect(context).toMatchObject({ extra: "info" })
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
})
