import { WebTracerProvider } from "@opentelemetry/sdk-trace-web"
import { SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base"
import { resourceFromAttributes } from "@opentelemetry/resources"
import { ATTR_SERVICE_NAME } from "@opentelemetry/semantic-conventions"
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http"
import { FetchInstrumentation } from "@opentelemetry/instrumentation-fetch"
import { DocumentLoadInstrumentation } from "@opentelemetry/instrumentation-document-load"
import { registerInstrumentations } from "@opentelemetry/instrumentation"

const otelEndpoint = import.meta.env.VITE_OTEL_ENDPOINT || "/v1/traces"

export function initTelemetry() {
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

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        propagateTraceHeaderCorsUrls: [
          /http:\/\/localhost:5173\/api/,
          /http:\/\/localhost:8080\/api/,
          /http:\/\/nars\.dz.*\/api/,
          /https:\/\/nars\.dz.*\/api/,
          /https:\/\/api\.nars\.dz.*\/api/,
        ],
        clearTimingResources: true,
      }),
    ],
  })
}
