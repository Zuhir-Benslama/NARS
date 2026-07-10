import { describe, it, expect, vi } from "vitest"

vi.mock("@opentelemetry/instrumentation-fetch", () => ({ FetchInstrumentation: class {} }))
vi.mock("@opentelemetry/instrumentation-document-load", () => ({
  DocumentLoadInstrumentation: class {},
}))
vi.mock("@opentelemetry/instrumentation", () => ({ registerInstrumentations: vi.fn() }))
vi.mock("@opentelemetry/sdk-trace-web", () => ({
  WebTracerProvider: class {
    register() {}
  },
}))
vi.mock("@opentelemetry/sdk-trace-base", () => ({ SimpleSpanProcessor: class {} }))
vi.mock("@opentelemetry/resources", () => ({ resourceFromAttributes: vi.fn(() => ({})) }))
vi.mock("@opentelemetry/exporter-trace-otlp-http", () => ({ OTLPTraceExporter: class {} }))

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
