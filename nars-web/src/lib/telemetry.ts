import { WebTracerProvider } from "@opentelemetry/sdk-trace-web"
import { SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base"
import { resourceFromAttributes } from "@opentelemetry/resources"
import { ATTR_SERVICE_NAME } from "@opentelemetry/semantic-conventions"
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http"
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch"
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load"
import { registerInstrumentations } from "@opentelemetry/instrumentation"

export function initTelemetry(endpoint?: string) {
  const otelEndpoint = endpoint ?? import.meta.env.VITE_OTEL_ENDPOINT
  if (!otelEndpoint) {
    if (import.meta.env.PROD) {
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
      }),
    ],
  })
}

function buildCorsUrls(): RegExp[] {
  const envUrls = import.meta.env.VITE_OTEL_CORS_URLS
  if (envUrls) {
    return envUrls.split(",").map((u) => new RegExp(u.trim()))
  }

  const apiBase = import.meta.env.VITE_API_BASE ?? ""
  const urls: RegExp[] = []

  if (apiBase) {
    urls.push(new RegExp(`^${escapeRegExp(apiBase)}`))
  }

  urls.push(/http:\/\/localhost:\d+\/api/, /http:\/\/127\.0\.0\.1:\d+\/api/)

  return urls
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}
