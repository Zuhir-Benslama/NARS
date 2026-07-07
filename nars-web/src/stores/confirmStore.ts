import { defineStore } from "pinia"

export const useConfirmStore = defineStore("confirm", {
  state: () => ({
    visible: false,
    message: "",
    okText: "Confirm",
    resolve: null as ((value: boolean) => void) | null,
  }),

  actions: {
    show(message: string, okText = "Confirm"): Promise<boolean> {
      return new Promise((resolve) => {
        this.message = message
        this.okText = okText
        this.resolve = resolve
        this.visible = true
      })
    },

    confirm(): void {
      this.visible = false
      this.resolve?.(true)
      this.resolve = null
    },

    cancel(): void {
      this.visible = false
      this.resolve?.(false)
      this.resolve = null
    },
  },
})
