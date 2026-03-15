// ─── INTERNATIONALISATION ─────────────────────────────────────────────────────
// Built on vue-i18n v10 (Composition API mode, legacy: false).
//
// Public API — unchanged for all existing callers:
//   t(key, replacements?)  — usable in any .ts file (not only components)
//   setLang(lang)          — async, handles RTL / localStorage / Geoman sync
//   applyInitialLang()     — called once from initMap() on startup
//   currentLang            — watchable Ref<string> (i18n.global.locale)
//
// Vue components should use useI18n() from 'vue-i18n' directly instead of
// importing t() — that way Vue's dependency tracking works properly in templates.
// The exported t() here is for non-component TypeScript files (map/*.ts etc.)

import { type Ref } from 'vue'
import { createI18n } from 'vue-i18n'
import en from './locales/en.json'

// ─── KEY HUMANISER ────────────────────────────────────────────────────────────
// Last-resort fallback when a key is missing from every locale and the fallback
// locale. Turns 'alert_outside_boundary' into 'Alert: Outside Boundary'.

function decamel(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function titleCase(value: string): string {
    return value.replace(/\b\w/g, c => c.toUpperCase())
}

function humanizeKey(key: string): string {
    const parts  = key.split('_')
    const prefix = parts[0]
    let   body   = parts.slice(1)

    if (prefix === 'phase' && body.length > 1) {
        const last = body[body.length - 1]
        if (last === 'label' || last === 'hint') body = body.slice(0, -1)
    }
    if (body.length === 0) body = parts

    const words = body
        .flatMap(part => decamel(part).split(' '))
        .filter(Boolean)
        .join(' ')

    const titled = titleCase(words)
    if (prefix === 'alert') return `Alert: ${titled}`
    if (prefix === 'msg')   return `${titled}?`
    return titled
}

// ─── INSTANCE ─────────────────────────────────────────────────────────────────

export const i18n = createI18n({
    legacy:         false,           // Composition API mode
    locale:         localStorage.getItem('nars_lang') || 'en',
    fallbackLocale: 'en',
    messages:       { en },          // bundle English inline; others lazy-loaded
    missing:        (_locale: string, key: string) => humanizeKey(key),
    missingWarn:    false,
    fallbackWarn:   false,
})

// ─── EXPORTS FOR NON-COMPONENT FILES ─────────────────────────────────────────
// map/*.ts files cannot call useI18n() (no component context), so they import
// t() directly. The wrapper normalises the return type to string.

export function t(key: string, replacements?: Record<string, string | number>): string {
    return String(i18n.global.t(key, replacements ?? {}))
}

// Watchable locale ref — same interface as before for watch(currentLang, …)
export const currentLang = i18n.global.locale as Ref<string>

// ─── LANGUAGE SWITCHING ───────────────────────────────────────────────────────

const loadedLocales = new Set<string>(['en'])

export async function setLang(lang: string): Promise<void> {
    if (!loadedLocales.has(lang)) {
        try {
            // Lazy-load fr / ar on first use. Vite bundles them as separate chunks.
            const messages = await import(`./locales/${lang}.json`)
            i18n.global.setLocaleMessage(lang, messages.default ?? messages)
            loadedLocales.add(lang)
        } catch (e) {
            console.error(`Failed to load language: ${lang}`, e)
            return
        }
    }

    // Cast needed because vue-i18n infers locale as a narrow literal type
    // when messages are provided at creation time.
    ;(i18n.global.locale as Ref<string>).value = lang
    localStorage.setItem('nars_lang', lang)
    document.documentElement.dir  = lang === 'ar' ? 'rtl' : 'ltr'
    document.documentElement.lang = lang
    if (typeof (window as any).L?.PM?.setLang === 'function') (window as any).L.PM.setLang(lang)
    if ((window as any).__narsUpdateLayerControl) (window as any).__narsUpdateLayerControl()
}

// Called once from initMap() — awaitable so map init waits for locale to load.
export function applyInitialLang(): Promise<void> {
    const lang = localStorage.getItem('nars_lang') || 'en'
    return setLang(lang)
}
