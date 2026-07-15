import { defineStore } from "pinia"
import { UI_CONFIG } from "../config"

export type ToastType = "success" | "error" | "info"

export interface ToastItem {
  id: number
  message: string
  type: ToastType
}

export const useToastStore = defineStore("toast", {
  state: () => ({
    toasts: [] as ToastItem[],
    nextId: 0,
    timers: {} as Record<number, ReturnType<typeof setTimeout>>,
  }),

  actions: {
    addToast(message: string, type: ToastType): number {
      const id = ++this.nextId
      this.toasts.push({ id, message, type })
      this.timers[id] = setTimeout(() => {
        this.removeToast(id)
      }, UI_CONFIG.toastDuration)
      return id
    },

    removeToast(id: number): void {
      if (this.timers[id]) {
        clearTimeout(this.timers[id])
        delete this.timers[id]
      }
      const idx = this.toasts.findIndex((t) => t.id === id)
      if (idx !== -1) this.toasts.splice(idx, 1)
    },

    clearAll(): void {
      Object.values(this.timers).forEach(clearTimeout)
      this.timers = {}
      this.toasts = []
    },

    reset(): void {
      this.clearAll()
    },
  },
})
