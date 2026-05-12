import { WebTracerProvider } from "@opentelemetry/sdk-trace-web"
import { SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base"
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http"
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch"
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load"
import { registerInstrumentations } from "@opentelemetry/instrumentation"

const otelEndpoint = import.meta.env.VITE_OTEL_ENDPOINT || "http://localhost:4318/v1/traces"

export function initTelemetry() {
  const exporter = new OTLPTraceExporter({
    url: otelEndpoint,
  })

  const provider = new WebTracerProvider({
    spanProcessors: [new SimpleSpanProcessor(exporter)],
  })
  provider.register()

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        propagateTraceHeaderCorsUrls: [
          /http:\/\/localhost:5173\/api/,
          /http:\/\/nars\.dz.*\/api/,
          /https:\/\/nars\.dz.*\/api/,
        ],
        clearTimingResources: true,
      }),
    ],
  })
}
