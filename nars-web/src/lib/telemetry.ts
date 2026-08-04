import { WebTracerProvider } from "@opentelemetry/sdk-trace-web"
import { SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base"
import { resourceFromAttributes } from "@opentelemetry/resources"
import { ATTR_SERVICE_NAME, ATTR_URL_FULL } from "@opentelemetry/semantic-conventions"
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http"
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch"
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load"
import { registerInstrumentations } from "@opentelemetry/instrumentation"
import type { Span } from "@opentelemetry/api"

export function initTelemetry(endpoint?: string) {
  const otelEndpoint = endpoint ?? import.meta.env.VITE_OTEL_ENDPOINT
  const disabled = isTelemetryDisabled()
  if (!otelEndpoint || disabled) {
    if (import.meta.env.PROD && !disabled) {
      console.warn("[Telemetry] VITE_OTEL_ENDPOINT not set — telemetry disabled")
    }
    return
  }

  const exporter = new OTLPTraceExporter({
    url: otelEndpoint,
  })

  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: "nars-vite",
    }),
    spanProcessors: [new SimpleSpanProcessor(exporter)],
  })
  provider.register()

  const corsUrls = buildCorsUrls()

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        propagateTraceHeaderCorsUrls: corsUrls,
        clearTimingResources: true,
        requestHook: stripQueryFromSpan,
      }),
    ],
  })
}

// ─── PRIVACY ───────────────────────────────────────────────────────────────────
// Request URLs can carry query-string data (e.g. `?search=`). Strip the query
// and fragment from captured spans so that data never leaves the browser.

export function sanitizeTelemetryUrl(url: string): string {
  const queryIndex = url.indexOf("?")
  const withoutQuery = queryIndex === -1 ? url : url.slice(0, queryIndex)
  const hashIndex = withoutQuery.indexOf("#")
  return hashIndex === -1 ? withoutQuery : withoutQuery.slice(0, hashIndex)
}

function stripQueryFromSpan(span: Span): void {
  // The API Span type exposes no attribute bag, but the SDK span handed to the
  // request hook does (the fetch instrumentation pre-populates it with the URL).
  const attributes = (span as Span & { attributes?: Record<string, unknown> }).attributes
  if (!attributes) return
  const raw = attributes[ATTR_URL_FULL] ?? attributes["http.url"]
  if (typeof raw !== "string") return
  const clean = sanitizeTelemetryUrl(raw)
  if (clean === raw) return
  span.setAttribute(ATTR_URL_FULL, clean)
  span.setAttribute("http.url", clean)
}

function isTelemetryDisabled(): boolean {
  const flag = import.meta.env.VITE_OTEL_DISABLED
  if (flag == null || flag === "") return false
  return ["1", "true", "yes", "on"].includes(String(flag).toLowerCase())
}

function buildCorsUrls(): RegExp[] {
  const envUrls = import.meta.env.VITE_OTEL_CORS_URLS
  if (envUrls) {
    return envUrls
      .split(",")
      .map((u) => {
        try {
          return new RegExp(u.trim())
        } catch {
          console.warn(`[Telemetry] Invalid regex in VITE_OTEL_CORS_URLS: "${u.trim()}"`)
          return null
        }
      })
      .filter((r): r is RegExp => r !== null)
  }

  const apiBase = import.meta.env.VITE_API_BASE ?? ""
  const urls: RegExp[] = []

  if (apiBase) {
    urls.push(new RegExp(`^${escapeRegExp(apiBase)}`))
  }

  urls.push(/https?:\/\/localhost:\d+\/api/, /https?:\/\/127\.0\.0\.1:\d+\/api/)

  return urls
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}
