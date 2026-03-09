import { createApp }                               from 'vue'
import type { DirectiveBinding }                   from 'vue'
import App                                         from './App.vue'
import './app.css'
import { initMap, loadFromDatabase, loadUserAndCommune } from './map'

// Import Leaflet-Geoman
import '@geoman-io/leaflet-geoman-free'
import '@geoman-io/leaflet-geoman-free/dist/leaflet-geoman.css'

// ── Patch deprecated Leaflet Draw _flat method ───────────────────────────────
// Leaflet Draw 1.0.4 uses L.Polyline._flat() which is deprecated in Leaflet 1.9.4.
// Override to redirect to the new L.LineUtil.isFlat() to suppress the warning.
if (typeof (window as any).L !== 'undefined') {
    const L = (window as any).L
    if (L.Polyline && L.Polyline._flat && L.LineUtil && L.LineUtil.isFlat) {
        L.Polyline._flat = function(latlngs: any) {
            return L.LineUtil.isFlat(latlngs)
        }
    }
}

// ── Patch deprecated MouseEvent properties in Firefox ────────────────────────
// Leaflet Draw 1.0.4 uses deprecated mozPressure and mozInputSource properties.
// Patch MouseEvent to provide these as getters that read from PointerEvent instead.
if (typeof window !== 'undefined') {
    Object.defineProperty(MouseEvent.prototype, 'mozPressure', {
        get() { return (this as any).pressure ?? 0 },
        configurable: true,
    })
    Object.defineProperty(MouseEvent.prototype, 'mozInputSource', {
        get() { return (this as any).pointerType ?? 0 },
        configurable: true,
    })
}

;(async () => {

    // ── Auth guard ─────────────────────────────────────────────────────────
    const authCheck = await fetch('/api/current_user', { credentials: 'include' })
    if (!authCheck.ok) {
        window.location.href = '/login'
        return
    }

    // ── Vue application ────────────────────────────────────────────────────
    const app = createApp(App)

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

    initMap()
    await loadUserAndCommune()
    await loadFromDatabase()
    console.log('NARS Urban Addressing — initialized')

})()
