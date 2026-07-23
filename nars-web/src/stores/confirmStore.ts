import { defineStore } from "pinia"

let _resolve: ((value: boolean) => void) | null = null

export const useConfirmStore = defineStore("confirm", {
  state: () => ({
    visible: false,
    message: "",
    okText: "Confirm",
  }),

  actions: {
    show(message: string, okText = "Confirm"): Promise<boolean> {
      return new Promise((resolve) => {
        this.message = message
        this.okText = okText
        _resolve = resolve
        this.visible = true
      })
    },

    confirm(): void {
      this.visible = false
      _resolve?.(true)
      _resolve = null
    },

    cancel(): void {
      this.visible = false
      _resolve?.(false)
      _resolve = null
    },
  },
})
