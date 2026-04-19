import 'maplibre-gl/dist/maplibre-gl.css'
import '@geoman-io/maplibre-geoman-free/dist/maplibre-geoman.css'
import { createApp } from 'vue'
import type { DirectiveBinding } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './app.css'
import { i18n } from './i18n'
import { initTheme } from './composables/useTheme'
import { initMap, loadFromDatabase, loadUserAndCommune } from './map'
import { apiUrl } from './api'
import { logError, createServerError } from './errors'
import { showToast } from './toast'
import { debugLog, debugError } from './utils/debug'

// Apply the saved theme before anything renders — prevents flash of wrong theme.
initTheme()
;(async () => {
    // ── Auth guard ─────────────────────────────────────────────────────────
    try {
        let authCheck = await fetch(apiUrl('/api/current_user'), { credentials: 'include' })

        // If 401, try a silent refresh once before redirecting to login
        if (authCheck.status === 401) {
            debugLog('[Auth] Access token expired, attempting silent refresh...')
            const refreshResponse = await fetch(apiUrl('/api/refresh'), {
                method: 'POST',
                credentials: 'include',
            })

            if (refreshResponse.ok) {
                debugLog('[Auth] Silent refresh successful, retrying auth check')
                authCheck = await fetch(apiUrl('/api/current_user'), { credentials: 'include' })
            }
        }

        if (!authCheck.ok) {
            window.location.href = '/login'
            return
        }
    } catch (error) {
        // Network error, timeout, or DNS failure — redirect to login
        // rather than mounting the app for an unauthenticated user.
        logError(
            createServerError('Auth check failed during app initialization', { action: 'auth-guard' }, error as Error),
        )
        window.location.href = '/login'
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
                phase: 'vue-runtime',
                action: info || 'unknown',
            },
            error,
        )
        logError(narsError)
        debugError('[Vue Error]', error, 'Info:', info)
    }

    // ── Global directive: v-click-outside ──────────────────────────────────
    app.directive('click-outside', {
        mounted(el: HTMLElement & { _clickOutsideHandler?: (e: MouseEvent) => void }, binding: DirectiveBinding) {
            el._clickOutsideHandler = (e: MouseEvent) => {
                if (!el.contains(e.target as Node)) binding.value(e)
            }
            document.addEventListener('click', el._clickOutsideHandler)
        },
        unmounted(el: HTMLElement & { _clickOutsideHandler?: (e: MouseEvent) => void }) {
            if (el._clickOutsideHandler) document.removeEventListener('click', el._clickOutsideHandler)
        },
    })

    app.mount('#app')

    try {
        await initMap()
        await loadUserAndCommune()
        await loadFromDatabase()
        debugLog('NARS Urban Addressing — Maplibre GL initialized')
    } catch (error) {
        const narsError = createServerError(
            'Failed to initialize application',
            { action: 'app-init' },
            error instanceof Error ? error : new Error(String(error)),
        )
        logError(narsError)
        showToast('Failed to load map. Please refresh the page.', 'error')
    }
})()
