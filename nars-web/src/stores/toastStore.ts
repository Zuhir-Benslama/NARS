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
  }),

  actions: {
    addToast(message: string, type: ToastType): number {
      const id = ++this.nextId
      this.toasts.push({ id, message, type })
      setTimeout(() => {
        this.removeToast(id)
      }, UI_CONFIG.toastDuration)
      return id
    },

    removeToast(id: number): void {
      const idx = this.toasts.findIndex((t) => t.id === id)
      if (idx !== -1) this.toasts.splice(idx, 1)
    },
  },
})
