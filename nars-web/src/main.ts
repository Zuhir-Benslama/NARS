import "maplibre-gl/dist/maplibre-gl.css"
import "@geoman-io/maplibre-geoman-free/dist/maplibre-geoman.css"
import { createApp } from "vue"
import { createPinia } from "pinia"
import App from "./App.vue"
import "./app.css"
import { i18n } from "./i18n"
import { initTheme } from "./composables/useTheme"
import { initMap, loadFromDatabase, loadUserAndCommune } from "./map"
import { useAppStore } from "./stores/appStore"
import { apiUrl } from "./api"
import { logError, createServerError } from "./lib/errors"
import { showToast } from "./lib/toast"
import { debugLog, debugError } from "./utils/debug"
import { initTelemetry } from "./lib/telemetry"
import { vClickOutside } from "./directives/clickOutside"

initTelemetry()

// Apply the saved theme before anything renders — prevents flash of wrong theme.
initTheme()
;(async () => {
  // ── Auth guard ─────────────────────────────────────────────────────────
  try {
    let authCheck = await fetch(apiUrl("/api/current_user"), {
      credentials: "include",
    })

    // If 401, try a silent refresh once before redirecting to login
    if (authCheck.status === 401) {
      debugLog("[Auth] Access token expired, attempting silent refresh...")
      const refreshResponse = await fetch(apiUrl("/api/refresh"), {
        method: "POST",
        credentials: "include",
      })

      if (refreshResponse.ok) {
        debugLog("[Auth] Silent refresh successful, retrying auth check")
        authCheck = await fetch(apiUrl("/api/current_user"), {
          credentials: "include",
        })
      }
    }

    if (!authCheck.ok) {
      window.location.href = "/login"
      return
    }
  } catch (error) {
    // Network error, timeout, or DNS failure — redirect to login
    // rather than mounting the app for an unauthenticated user.
    logError(
      createServerError(
        "Auth check failed during app initialization",
        { action: "auth-guard" },
        error as Error,
      ),
    )
    window.location.href = "/login"
    return
  }

  // ── Vue application ────────────────────────────────────────────────────
  const app = createApp(App)
  const pinia = createPinia()

  app.use(pinia)
  app.use(i18n)

  // ── Global Error Handler ──────────────────────────────────────────────
  // Catches all Vue-level errors and logs them with context
  app.config.errorHandler = (err, _instance, info) => {
    const error = err instanceof Error ? err : new Error(String(err))
    const narsError = createServerError(
      error.message,
      {
        phase: "vue-runtime",
        action: info || "unknown",
      },
      error,
    )
    logError(narsError)
    debugError("[Vue Error]", error, "Info:", info)
  }

  app.directive("click-outside", vClickOutside)

  app.mount("#app")

  try {
    // Load user profile first — the role determines which init path we take.
    await loadUserAndCommune()

    const appStore = useAppStore()
    const role = appStore.user?.role ?? "commune_user"

    if (role === "commune_user") {
      // Full map + feature init — not needed for admin roles.
      await initMap()
      await loadFromDatabase()
      debugLog("NARS Urban Addressing — Maplibre GL initialized")
    } else {
      // Admin users land on AdminDashboard — no map needed.
      debugLog(`NARS Admin Dashboard — role: ${role}`)
    }
  } catch (error) {
    const narsError = createServerError(
      "Failed to initialize application",
      { action: "app-init" },
      error instanceof Error ? error : new Error(String(error)),
    )
    logError(narsError)
    showToast("Failed to load map. Please refresh the page.", "error")
  }

  // Expose stores for Playwright E2E tests
  if (import.meta.env.DEV) {
    const { useModalStore } = await import("./stores/modalStore")
    const { useLayerStore } = await import("./stores/layerStore")
    interface TestStores {
      appStore: ReturnType<typeof useAppStore>
      modalStore: ReturnType<typeof useModalStore>
      layerStore: ReturnType<typeof useLayerStore>
    }
    ;(window as unknown as { __TEST__: TestStores }).__TEST__ = {
      appStore: useAppStore(),
      modalStore: useModalStore(),
      layerStore: useLayerStore(),
    }
  }
})()
