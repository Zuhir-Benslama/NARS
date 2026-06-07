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

import { type Ref } from "vue"
import { createI18n } from "vue-i18n"
import en from "./en.json"
import { debugError } from "../utils/debug"

// ─── KEY HUMANISER ────────────────────────────────────────────────────────────

function decamel(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2")
}

function titleCase(value: string): string {
  return value.replace(/\b\w/g, (c) => c.toUpperCase())
}

function humanizeKey(key: string): string {
  const parts = key.split("_")
  const prefix = parts[0]
  let body = parts.slice(1)

  if (prefix === "phase" && body.length > 1) {
    const last = body[body.length - 1]
    if (last === "label" || last === "hint") body = body.slice(0, -1)
  }
  if (body.length === 0) body = parts

  const words = body
    .flatMap((part) => decamel(part).split(" "))
    .filter(Boolean)
    .join(" ")

  const titled = titleCase(words)
  if (prefix === "alert") return `Alert: ${titled}`
  if (prefix === "msg") return `${titled}?`
  return titled
}

// ─── INSTANCE ─────────────────────────────────────────────────────────────────

export const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem("nars_lang") || "en",
  fallbackLocale: "en",
  messages: { en },
  missing: (_locale: string, key: string) => humanizeKey(key),
  missingWarn: false,
  fallbackWarn: false,
})

export function t(key: string, replacements?: Record<string, string | number>): string {
  return String(i18n.global.t(key, replacements ?? {}))
}

export const currentLang = i18n.global.locale as Ref<string>

// ─── LANGUAGE SWITCHING ───────────────────────────────────────────────────────

// Dynamic imports for non-English locales only
// English is loaded statically above
const localeImports = {
  fr: () => import("./fr.json"),
  ar: () => import("./ar.json"),
} as const

type LocaleKey = keyof typeof localeImports

const loadedLocales = new Set<string>(["en"])

export async function setLang(lang: string): Promise<void> {
  // English already loaded statically - just set it
  if (lang === "en") {
    ;(i18n.global.locale as Ref<string>).value = "en"
    localStorage.setItem("nars_lang", lang)
    document.documentElement.dir = "ltr"
    document.documentElement.lang = "en"
    return
  }

  if (!loadedLocales.has(lang)) {
    try {
      const localeKey = lang as LocaleKey
      if (localeKey in localeImports) {
        const messages = await localeImports[localeKey]()
        i18n.global.setLocaleMessage(lang, messages.default ?? messages)
        loadedLocales.add(lang)
      }
    } catch (e) {
      debugError(`Failed to load language: ${lang}`, e)
      return
    }
  }

  ;(i18n.global.locale as Ref<string>).value = lang
  localStorage.setItem("nars_lang", lang)
  document.documentElement.dir = lang === "ar" ? "rtl" : "ltr"
  document.documentElement.lang = lang
}

export function applyInitialLang(): Promise<void> {
  const lang = localStorage.getItem("nars_lang") || "en"
  return setLang(lang)
}
