import { describe, it, expect, vi, beforeEach } from "vitest"

describe("initTelemetry", () => {
  beforeEach(() => {
    vi.resetModules()
  })

  it("skips initialization when VITE_OTEL_ENDPOINT is not set", async () => {
    vi.stubEnv("VITE_OTEL_ENDPOINT", undefined)
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {})
    const { initTelemetry } = await import("./telemetry")
    initTelemetry()
    expect(warn).not.toHaveBeenCalled()
    warn.mockRestore()
    vi.unstubAllEnvs()
  })

  it("initializes OTel when endpoint is set", async () => {
    vi.stubEnv("VITE_OTEL_ENDPOINT", "http://otel:4318/v1/traces")
    const { initTelemetry } = await import("./telemetry")
    expect(() => initTelemetry()).not.toThrow()
    vi.unstubAllEnvs()
  })
})
