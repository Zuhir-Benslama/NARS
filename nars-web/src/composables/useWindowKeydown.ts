// ─── WINDOW KEYDOWN COMPOSABLE ──────────────────────────────────────────────
// Extracts the common onMounted/onUnmounted window keydown listener pattern.
// Usage:
//   useWindowKeydown({
//     Escape: () => close(),
//     Enter: (e) => { if (condition) submit() },
//   })
//
// Listeners are only active when enabled (defaults to true).

import { onMounted, onUnmounted, type Ref } from "vue"

type KeyHandler = (e: KeyboardEvent) => void

export function useWindowKeydown(
  keyMap: Record<string, KeyHandler>,
  enabled?: Ref<boolean>,
): void {
  function handler(e: KeyboardEvent) {
    if (enabled && !enabled.value) return
    const fn = keyMap[e.key]
    if (fn) fn(e)
  }

  onMounted(() => window.addEventListener("keydown", handler))
  onUnmounted(() => window.removeEventListener("keydown", handler))
}
