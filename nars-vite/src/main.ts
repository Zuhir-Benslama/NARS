import { createApp }                               from 'vue'
import type { DirectiveBinding }                   from 'vue'
import App                                         from './App.vue'
import './app.css'
import { initMap, loadFromDatabase, loadUserAndCommune } from './map'

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
