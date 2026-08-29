import "maplibre-gl/dist/maplibre-gl.css"
import { createApp } from "vue"
import { createPinia } from "pinia"
import App from "./App.vue"
import "./app.css"
import { i18n, t } from "./i18n"
import router from "./router"
import { initTheme } from "./composables/useTheme"
import { initMap, loadFromDatabase, loadUserAndCommune, displayCommuneBoundary } from "./map"
import { useAppStore } from "./stores/appStore"
import { apiUrl, refreshSession } from "./api"
import { API_CONFIG, getLoginPath } from "./config"
import { logError, createServerError } from "./lib/errors"
import { showToast } from "./lib/toast"
import { debugLog, debugError } from "./utils/debug"
import { initTelemetry } from "./lib/telemetry"
import { vClickOutside } from "./directives/clickOutside"
import type { UserInfo } from "./types"

initTelemetry()
initTheme()

// ─── Auth guard (before Vue mounts) ──────────────────────────────────────────

async function checkAuth(): Promise<{ ok: boolean; user?: UserInfo }> {
  // Boot traffic must never hang the app on a dead backend: bound every
  // bootstrap request with the same default timeout apiFetch uses.
  const bootTimeout = () => AbortSignal.timeout(API_CONFIG.defaultTimeout)

  let authCheck = await fetch(apiUrl("/api/current_user"), {
    credentials: "include",
    signal: bootTimeout(),
  })

  if (authCheck.status === 401) {
    debugLog("[Auth] Access token expired, attempting silent refresh...")
    // Shares the single-flight promise with mid-session refreshes, so a boot
    // colliding with an in-flight retry never double-hits /api/refresh.
    if (await refreshSession()) {
      debugLog("[Auth] Silent refresh successful, retrying auth check")
      authCheck = await fetch(apiUrl("/api/current_user"), {
        credentials: "include",
        signal: bootTimeout(),
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
    // checkAuth() already fetched /api/current_user and populated the store
    // before mount; only hit the endpoint again when boot ran without one.
    const appStore = useAppStore()
    if (!appStore.isAuthenticated) {
      await loadUserAndCommune()
    }

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
    showToast(t("app_init_failed"), "error")
  }
}

// ─── Startup sequence ────────────────────────────────────────────────────────

void (async () => {
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

  // Expose stores for E2E tests BEFORE Vue app mounts so that tests
  // can interact with stores even if the router or map init fails.
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

  createVueApp(pinia)

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
