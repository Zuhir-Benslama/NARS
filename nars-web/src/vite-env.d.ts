/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE: string
  readonly VITE_OTEL_CORS_URLS: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

// Vue SFC type declarations — resolves "Cannot find module '*.vue'" errors
// that occur with bundler moduleResolution in Vite+TypeScript projects.
declare module "*.vue" {
  import type { DefineComponent } from "vue"
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}
