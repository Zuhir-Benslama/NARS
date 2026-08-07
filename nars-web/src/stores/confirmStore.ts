import { defineStore } from "pinia"

let _resolver: ((value: boolean) => void) | null = null

export const useConfirmStore = defineStore("confirm", {
  state: () => ({
    visible: false,
    message: "",
    okText: "Confirm",
  }),

  actions: {
    show(message: string, okText = "Confirm"): Promise<boolean> {
      // If a previous confirm is still pending, resolve it as rejected
      if (_resolver) {
        _resolver(false)
        _resolver = null
      }
      this.visible = true
      this.message = message
      this.okText = okText
      return new Promise<boolean>((resolve) => {
        _resolver = resolve
      })
    },

    confirm(): void {
      this.visible = false
      _resolver?.(true)
      _resolver = null
    },

    cancel(): void {
      this.visible = false
      _resolver?.(false)
      _resolver = null
    },
  },
})

/**
 * Reset the confirm promise bridge. Call during HMR disposal or test cleanup
 * to prevent a stale pending resolver from leaking across reloads.
 */
export function resetConfirmBridge(): void {
  if (_resolver) {
    _resolver(false)
    _resolver = null
  }
}
