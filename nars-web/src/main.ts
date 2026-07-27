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
import type { UserInfo } from "./types"

initTelemetry()
initTheme()

// ─── Auth guard (before Vue mounts) ──────────────────────────────────────────

async function checkAuth(): Promise<{ ok: boolean; user?: UserInfo }> {
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
    return { ok: false }
  }

  const user = (await authCheck.json()) as UserInfo
  return { ok: true, user }
}

// ─── Vue bootstrap ───────────────────────────────────────────────────────────

function createVueApp(pinia: ReturnType<typeof createPinia>) {
  const app = createApp(App)

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
  let authResult: { ok: boolean; user?: UserInfo }
  try {
    authResult = await checkAuth()
  } catch (error) {
    logError(
      createServerError(
        "Auth check failed during app initialization",
        { action: "auth-guard" },
        error instanceof Error ? error : new Error(String(error)),
      ),
    )
    window.location.href = getLoginPath()
    return
  }

  if (!authResult.ok) {
    window.location.href = getLoginPath()
    return
  }

  // Create Pinia and populate user BEFORE Vue app mounts.
  // The router guard checks appStore.isAuthenticated, so the user
  // must be set before the router is installed.
  const pinia = createPinia()
  const appStore = useAppStore(pinia)
  if (authResult.user) {
    appStore.setUser(authResult.user)
  }

  createVueApp(pinia)

  // Expose stores for E2E tests *before* initializeApp() so tests can
  // interact with the UI immediately — map/feature loading is slow and
  // irrelevant for store-level assertions.
  if (import.meta.env.DEV) {
    const { useModalStore } = await import("./stores/modalStore")
    const { useLayerStore } = await import("./stores/layerStore")
    interface TestStores {
      appStore: ReturnType<typeof useAppStore>
      modalStore: ReturnType<typeof useModalStore>
      layerStore: ReturnType<typeof useLayerStore>
    }
    Object.defineProperty(window, "__TEST__", {
      value: {
        appStore: useAppStore(),
        modalStore: useModalStore(),
        layerStore: useLayerStore(),
      } satisfies TestStores,
      writable: false,
      configurable: import.meta.env.DEV,
    })
  }

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
})()
