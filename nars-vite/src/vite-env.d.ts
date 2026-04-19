/// <reference types="vite/client" />

interface ImportMetaEnv {
    readonly VITE_API_BASE: string
}

interface ImportMeta {
    readonly env: ImportMetaEnv
}

// Vue SFC type declarations — resolves "Cannot find module '*.vue'" errors
// that occur with bundler moduleResolution in Vite+TypeScript projects.
declare module '*.vue' {
    import type { DefineComponent } from 'vue'
    const component: DefineComponent<object, object, unknown>
    export default component
}
