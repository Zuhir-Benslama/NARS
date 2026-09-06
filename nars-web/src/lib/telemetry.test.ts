import { describe, it, expect, vi, afterEach } from "vitest"

const mockRegister = vi.fn()
const mockFetchInst = vi.fn()

vi.mock("@opentelemetry/instrumentation-fetch", () => ({
  FetchInstrumentation: class {
    constructor(config: unknown) {
      mockFetchInst(config)
    }
  },
}))
vi.mock("@opentelemetry/instrumentation-document-load", () => ({
  DocumentLoadInstrumentation: class {},
}))
vi.mock("@opentelemetry/instrumentation", () => ({ registerInstrumentations: mockRegister }))
vi.mock("@opentelemetry/sdk-trace-web", () => ({
  WebTracerProvider: class {
    register() {}
  },
}))
vi.mock("@opentelemetry/sdk-trace-base", () => ({ SimpleSpanProcessor: class {} }))
vi.mock("@opentelemetry/resources", () => ({ resourceFromAttributes: vi.fn(() => ({})) }))
vi.mock("@opentelemetry/exporter-trace-otlp-http", () => ({ OTLPTraceExporter: class {} }))

afterEach(() => {
  vi.unstubAllEnvs()
  vi.clearAllMocks()
})

describe("initTelemetry", () => {
  it("skips initialization when endpoint is not set", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {})
    const { initTelemetry } = await import("./telemetry")
    initTelemetry()
    expect(mockRegister).not.toHaveBeenCalled()
    warn.mockRestore()
  }, 20_000)

  it("skips initialization when explicitly disabled even with an endpoint", async () => {
    vi.stubEnv("VITE_OTEL_DISABLED", "true")
    const { initTelemetry } = await import("./telemetry")
    initTelemetry("http://otel:4318/v1/traces")
    expect(mockRegister).not.toHaveBeenCalled()
  }, 20_000)

  it("initializes OTel when endpoint is set", async () => {
    const { initTelemetry } = await import("./telemetry")
    expect(() => initTelemetry("http://otel:4318/v1/traces")).not.toThrow()
    expect(mockRegister).toHaveBeenCalledTimes(1)
  }, 20_000)

  it("configures the fetch instrumentation with a URL-sanitizing request hook", async () => {
    const { initTelemetry } = await import("./telemetry")
    initTelemetry("http://otel:4318/v1/traces")

    const config = mockFetchInst.mock.calls[0][0]
    expect(config.requestHook).toEqual(expect.any(Function))
  }, 20_000)
})

describe("sanitizeTelemetryUrl", () => {
  it("strips the query string", async () => {
    const { sanitizeTelemetryUrl } = await import("./telemetry")
    expect(sanitizeTelemetryUrl("http://localhost:5000/api/roads?search=foo&x=1")).toBe(
      "http://localhost:5000/api/roads",
    )
  })

  it("strips the URL fragment", async () => {
    const { sanitizeTelemetryUrl } = await import("./telemetry")
    expect(sanitizeTelemetryUrl("http://localhost:5000/api/roads#section")).toBe(
      "http://localhost:5000/api/roads",
    )
  })

  it("leaves URLs without query or fragment untouched", async () => {
    const { sanitizeTelemetryUrl } = await import("./telemetry")
    expect(sanitizeTelemetryUrl("http://localhost:5000/api/roads")).toBe(
      "http://localhost:5000/api/roads",
    )
  })
})
