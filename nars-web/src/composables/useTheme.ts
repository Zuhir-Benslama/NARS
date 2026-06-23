// ─── THEME ────────────────────────────────────────────────────────────────────
// Single source of truth for the app color mode.
// Call initTheme() once in main.ts (before app.mount) so the data-theme
// attribute is applied before the first paint — no flash of wrong theme.
// Import { theme, setTheme } anywhere to read or change the active mode.

import { ref, watch } from "vue"

export type ThemeMode = "light" | "dark" | "auto"

const STORAGE_KEY = "nars_theme"

const VALID_THEMES: readonly ThemeMode[] = ["light", "dark", "auto"]

function parseTheme(raw: string | null): ThemeMode {
  return raw != null && VALID_THEMES.includes(raw as ThemeMode) ? (raw as ThemeMode) : "dark"
}

// Read persisted value, default to dark.
const stored = parseTheme(localStorage.getItem(STORAGE_KEY))
export const theme = ref<ThemeMode>(stored)

function applyTheme(mode: ThemeMode): void {
  if (mode === "auto") {
    document.documentElement.removeAttribute("data-theme")
  } else {
    document.documentElement.setAttribute("data-theme", mode)
  }
}

export function setTheme(mode: ThemeMode): void {
  theme.value = mode
}

// Watches the ref and syncs to DOM + localStorage.
// Called once at startup so the watcher is active for the whole app lifetime.
export function initTheme(): void {
  applyTheme(theme.value) // apply immediately, before mount
  watch(theme, (mode) => {
    localStorage.setItem(STORAGE_KEY, mode)
    applyTheme(mode)
  })
}
