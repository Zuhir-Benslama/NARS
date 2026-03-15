import { createApp }                               from 'vue'
import type { DirectiveBinding }                   from 'vue'
import App                                         from './App.vue'
import './app.css'
import { i18n }                                   from './i18n'
import { initTheme }                              from './composables/useTheme'
import { initMap, loadFromDatabase, loadUserAndCommune } from './map'

import '@geoman-io/leaflet-geoman-free'
import '@geoman-io/leaflet-geoman-free/dist/leaflet-geoman.css'

// Apply the saved theme before anything renders — prevents flash of wrong theme.
initTheme()

;(async () => {

    // ── Auth guard ─────────────────────────────────────────────────────────
    const authCheck = await fetch('/api/current_user', { credentials: 'include' })
    if (!authCheck.ok) {
        window.location.href = '/login'
        return
    }

    // ── Vue application ────────────────────────────────────────────────────
    const app = createApp(App)

    app.use(i18n)

    // ── Global directive: v-click-outside ──────────────────────────────────
    app.directive('click-outside', {
        mounted(el: HTMLElement & { _clickOutsideHandler?: (e: MouseEvent) => void }, binding: DirectiveBinding) {
            el._clickOutsideHandler = (e: MouseEvent) => {
                if (!el.contains(e.target as Node)) binding.value(e)
            }
            document.addEventListener('click', el._clickOutsideHandler)
        },
        unmounted(el: HTMLElement & { _clickOutsideHandler?: (e: MouseEvent) => void }) {
            if (el._clickOutsideHandler)
                document.removeEventListener('click', el._clickOutsideHandler)
        },
    })

    app.mount('#app')

    await initMap()
    await loadUserAndCommune()
    await loadFromDatabase()
    console.log('NARS Urban Addressing — initialized')

})()
