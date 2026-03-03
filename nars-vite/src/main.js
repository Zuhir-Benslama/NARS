import { createApp } from 'vue'
import App from './App.vue'
import './app.css'

import { initMap, loadFromDatabase, loadUserAndCommune } from './map.js'

// ── Bootstrap ──────────────────────────────────────────────────────────────
// Wrapped in an async IIFE so we can await the auth check without requiring
// top-level await (not supported in the default Vite build target).
;(async () => {

    // ── Auth guard ─────────────────────────────────────────────────────────
    // Verify the session before mounting anything.
    // Handles two cases:
    //   1. Dev mode  — Vite serves index.html directly, bypassing PagesController,
    //                  so there is no server-side redirect to /login.
    //   2. Production — a stale/expired cookie can slip past PagesController.
    // A 401 here means "not logged in" → redirect to /login immediately.
    const authCheck = await fetch('/api/current_user', { credentials: 'include' })
    if (!authCheck.ok) {
        window.location.href = '/login'
        return
    }

    // ── Vue application ────────────────────────────────────────────────────
    const app = createApp(App)

    // ── Global directive: v-click-outside ──────────────────────────────────
    app.directive('click-outside', {
        mounted(el, binding) {
            el._clickOutsideHandler = (e) => {
                if (!el.contains(e.target)) binding.value(e)
            }
            document.addEventListener('click', el._clickOutsideHandler)
        },
        unmounted(el) {
            document.removeEventListener('click', el._clickOutsideHandler)
        },
    })

    // ── Mount ───────────────────────────────────────────────────────────────
    app.mount('#app')

    // ── Initialize Leaflet map and load data ────────────────────────────────
    initMap()
    await loadUserAndCommune()
    await loadFromDatabase()
    console.log('NARS Urban Addressing — initialized')

})()
