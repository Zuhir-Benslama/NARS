// ─── TOAST NOTIFICATIONS ──────────────────────────────────────────────────────
// Lightweight non-blocking replacement for alert().
// Push notifications into a Pinia store that a Vue component (ToastContainer)
// renders reactively.  Works from both .vue components and imperative .ts files.

import { useToastStore } from "../stores/toastStore"
import { useConfirmStore } from "../stores/confirmStore"
import type { ToastType } from "../stores/toastStore"

export type { ToastType }

export function showToast(message: string, type: ToastType = "info"): number {
  try {
    return useToastStore().addToast(message, type)
  } catch {
    // Pinia not ready (e.g. during app bootstrap) — silently ignore;
    // the component will pick up toasts once mounted.
    return 0
  }
}

// ─── CONFIRM DIALOG ──────────────────────────────────────────────────────────
// Promise-based replacement for blocking window.confirm().
// Shows a Vue-driven dialog component (ConfirmDialog) via Pinia store.

export function showConfirm(message: string, okText = "Confirm"): Promise<boolean> {
  if (!document.body) {
    return new Promise((resolve) => {
      document.addEventListener(
        "DOMContentLoaded",
        () => {
          showConfirm(message, okText).then(resolve)
        },
        { once: true },
      )
    })
  }

  try {
    return useConfirmStore().show(message, okText)
  } catch {
    return Promise.resolve(false)
  }
}
