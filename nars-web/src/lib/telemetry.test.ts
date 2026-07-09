import { describe, it, expect, vi } from "vitest"

describe("initTelemetry", () => {
  it("skips initialization when endpoint is not set", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {})
    const { initTelemetry } = await import("./telemetry")
    initTelemetry()
    expect(warn).not.toHaveBeenCalled()
    warn.mockRestore()
  })

  it("initializes OTel when endpoint is set", async () => {
    const { initTelemetry } = await import("./telemetry")
    expect(() => initTelemetry("http://otel:4318/v1/traces")).not.toThrow()
  })
})
