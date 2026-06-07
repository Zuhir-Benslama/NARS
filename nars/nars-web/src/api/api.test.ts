// ─── API.TS TESTS ─────────────────────────────────────────────────────────────
// Tests for error handling classes and retry logic.

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import {
  NarsError,
  ErrorCode,
  createNetworkError,
  createAuthError,
  createServerError,
  createNotFoundError,
  createTimeoutError,
  createPermissionError,
  withRetry,
  isNarsError,
  isNetworkError,
  isAuthError,
} from "../lib/errors"

describe("Error handling", () => {
  describe("NarsError class", () => {
    it("should create error with code and message", () => {
      const error = createNetworkError("Connection failed")
      expect(error).toBeInstanceOf(NarsError)
      expect(error.code).toBe(ErrorCode.NETWORK)
      expect(error.message).toBe("Connection failed")
    })

    it("should capture context", () => {
      const error = createServerError("Server error", { action: "test" })
      expect(error.context.action).toBe("test")
    })

    it("should capture cause", () => {
      const cause = new Error("Original error")
      const error = createNetworkError("Failed", {}, cause)
      expect(error.cause).toBe(cause)
    })

    it("should generate user-friendly messages", () => {
      expect(createNetworkError("").getUserMessage()).toContain("Network error")
      expect(createAuthError("").getUserMessage()).toContain("Authentication")
      expect(createNotFoundError("").getUserMessage()).toContain("not found")
      expect(createTimeoutError("").getUserMessage()).toContain("timed out")
      expect(createPermissionError("").getUserMessage()).toContain("permission")
    })

    it("should include technical details", () => {
      const error = createServerError("Test", { action: "test" })
      const details = error.getTechnicalDetails()
      expect(details).toContain("[SERVER_ERROR]")
      expect(details).toContain("Test")
    })
  })

  describe("Error type guards", () => {
    it("should identify NarsError", () => {
      const error = createNetworkError("")
      expect(isNarsError(error)).toBe(true)
      expect(isNarsError(new Error("Regular"))).toBe(false)
    })

    it("should identify network errors", () => {
      expect(isNetworkError(createNetworkError(""))).toBe(true)
      expect(isNetworkError(createAuthError(""))).toBe(false)
    })

    it("should identify auth errors", () => {
      expect(isAuthError(createAuthError(""))).toBe(true)
      expect(isAuthError(createNetworkError(""))).toBe(false)
    })
  })

  describe("withRetry", () => {
    beforeEach(() => {
      vi.useFakeTimers()
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it("should succeed on first try", async () => {
      const fn = vi.fn().mockResolvedValue("success")
      const result = withRetry(fn, {
        maxRetries: 2,
        baseDelay: 10,
        maxDelay: 100,
      })
      await expect(result).resolves.toBe("success")
      expect(fn).toHaveBeenCalledTimes(1)
    })

    it("should retry on network errors", async () => {
      const fn = vi
        .fn()
        .mockRejectedValueOnce(createNetworkError("Fail"))
        .mockRejectedValueOnce(createNetworkError("Fail"))
        .mockResolvedValueOnce("success")

      const promise = withRetry(fn, {
        maxRetries: 3,
        baseDelay: 10,
        maxDelay: 100,
      })

      // Advance timers for retries
      await vi.advanceTimersByTimeAsync(50)

      await expect(promise).resolves.toBe("success")
      expect(fn).toHaveBeenCalledTimes(3)
    })

    it("should not retry on non-retryable errors", async () => {
      const fn = vi.fn().mockRejectedValue(createServerError("Server error"))

      const promise = withRetry(fn, {
        maxRetries: 3,
        baseDelay: 10,
        maxDelay: 100,
      })

      await expect(promise).rejects.toThrow(NarsError)
      expect(fn).toHaveBeenCalledTimes(1)
    })

    it("should exhaust retries and throw final error", async () => {
      const fn = vi.fn().mockRejectedValue(createNetworkError("Fail"))

      const promise = withRetry(fn, {
        maxRetries: 2,
        baseDelay: 10,
        maxDelay: 100,
      })
      // Attach catch early to prevent unhandled rejection warnings
      const caught = promise.catch((err) => err)

      // Advance timers for all retries
      await vi.advanceTimersByTimeAsync(300)

      const result = await caught
      expect(result).toBeInstanceOf(NarsError)
      expect((result as NarsError).code).toBe(ErrorCode.NETWORK)
      expect(fn).toHaveBeenCalledTimes(3)
    })
  })
})
