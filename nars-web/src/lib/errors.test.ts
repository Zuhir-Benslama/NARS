import { afterEach, describe, expect, it, vi } from "vitest"
import {
  NarsError,
  ErrorCode,
  createNetworkError,
  createValidationError,
  getUserMessageKey,
  withRetry,
} from "./errors"

afterEach(() => {
  vi.useRealTimers()
})

describe("createValidationError", () => {
  it("creates a NarsError with the VALIDATION code and context", () => {
    const err = createValidationError("bad input", { status: 422 })
    expect(err).toBeInstanceOf(NarsError)
    expect(err.code).toBe(ErrorCode.VALIDATION)
    expect(err.context.status).toBe(422)
    expect(err.timestamp).toBeInstanceOf(Date)
  })
})

describe("getUserMessageKey", () => {
  it("maps a validation error to the err_validation i18n key", () => {
    expect(getUserMessageKey(createValidationError("x"))).toBe("err_validation")
  })

  it("falls back to UNKNOWN for non-NarsError values", () => {
    expect(getUserMessageKey(new Error("x"))).toBe("err_unknown")
  })
})

describe("withRetry", () => {
  it("returns the first successful result", async () => {
    await expect(withRetry(() => Promise.resolve("ok"))).resolves.toBe("ok")
  })

  it("retries NETWORK errors and fails with the last error after maxRetries", async () => {
    vi.useFakeTimers()
    const fn = vi.fn(() => {
      throw createNetworkError("boom")
    })
    const promise = withRetry(fn, { maxRetries: 3, baseDelay: 1, maxDelay: 10 })
    const assertion = expect(promise).rejects.toMatchObject({ code: "NETWORK_ERROR" })
    await vi.advanceTimersByTimeAsync(1000)
    await assertion
    expect(fn).toHaveBeenCalledTimes(4)
  })

  it("does not retry non-network programming errors", async () => {
    const boom = new RangeError("bad")
    const fn = vi.fn(() => {
      throw boom
    })
    await expect(withRetry(fn, { maxRetries: 3 })).rejects.toBe(boom)
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it("rethrows caller-led AbortError without retrying", async () => {
    const abort = new DOMException("aborted", "AbortError")
    const fn = vi.fn(() => {
      throw abort
    })
    await expect(withRetry(fn, { maxRetries: 3 })).rejects.toBe(abort)
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it("never throws null even with a pathological negative maxRetries", async () => {
    const fn = vi.fn(() => {
      throw new RangeError("boom")
    })
    await expect(withRetry(fn, { maxRetries: -1 })).rejects.toMatchObject({
      code: "UNKNOWN_ERROR",
    })
    expect(fn).not.toHaveBeenCalled()
  })
})
