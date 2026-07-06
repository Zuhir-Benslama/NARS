import "maplibre-gl/dist/maplibre-gl.css"
import "@geoman-io/maplibre-geoman-free/dist/maplibre-geoman.css"
import { createApp } from "vue"
import { createPinia } from "pinia"
import App from "./App.vue"
import "./app.css"
import { i18n } from "./i18n"
import router from "./router"
import { initTheme } from "./composables/useTheme"
import { initMap, loadFromDatabase, loadUserAndCommune, displayCommuneBoundary } from "./map"
import { useAppStore } from "./stores/appStore"
import { apiUrl } from "./api"
import { logError, createServerError } from "./lib/errors"
import { showToast } from "./lib/toast"
import { getLoginPath } from "./config"
import { debugLog, debugError } from "./utils/debug"
import { initTelemetry } from "./lib/telemetry"
import { vClickOutside } from "./directives/clickOutside"

initTelemetry()
initTheme()

// ─── Auth guard (before Vue mounts) ──────────────────────────────────────────

async function checkAuth(): Promise<boolean> {
  let authCheck = await fetch(apiUrl("/api/current_user"), {
    credentials: "include",
  })

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
    window.location.href = getLoginPath()
    return false
  }

  return true
}

// ─── Vue bootstrap ───────────────────────────────────────────────────────────

function createVueApp() {
  const app = createApp(App)
  const pinia = createPinia()

  app.use(pinia)
  app.use(i18n)
  app.use(router)

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
}

// ─── Post-mount init: load user, init map based on role ──────────────────────────

async function initializeApp(): Promise<void> {
  try {
    await loadUserAndCommune()

    const appStore = useAppStore()
    const role = appStore.user?.role ?? "commune_user"

    if (role === "commune_user") {
      await initMap()
      const communeId = appStore.user?.commune?.id
      if (communeId) await displayCommuneBoundary(communeId)
      await loadFromDatabase()
      debugLog("NARS Urban Addressing — Maplibre GL initialized")
    } else {
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
}

// ─── Startup sequence ────────────────────────────────────────────────────────

;(async () => {
  let authenticated: boolean
  try {
    authenticated = await checkAuth()
  } catch (error) {
    logError(
      createServerError(
        "Auth check failed during app initialization",
        { action: "auth-guard" },
        error,
      ),
    )
    window.location.href = getLoginPath()
    return
  }

  if (!authenticated) return

  createVueApp()

  try {
    await initializeApp()
  } catch (error) {
    logError(
      createServerError(
        "App initialization failed",
        { action: "app-init" },
        error instanceof Error ? error : new Error(String(error)),
      ),
    )
  }

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
