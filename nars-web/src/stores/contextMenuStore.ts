import { defineStore } from "pinia"

export interface CtxMenuItem {
  label?: string
  danger?: boolean
  separator?: boolean
  /** Rendered inert (muted, not clickable) but still listed, so a rule the
   *  user is temporarily blocked by stays discoverable instead of vanishing. */
  disabled?: boolean
  /** Why the item is disabled, shown inline next to the label. */
  disabledReason?: string
  onClick?: () => void
}

export const useContextMenuStore = defineStore("contextMenu", {
  state: () => ({
    visible: false,
    x: 0,
    y: 0,
    items: [] as CtxMenuItem[],
  }),

  actions: {
    show(x: number, y: number, items: CtxMenuItem[]): void {
      this.x = x
      this.y = y
      this.items = [...items]
      this.visible = true
    },
    hide(): void {
      this.visible = false
      this.items = []
    },
    reset(): void {
      this.visible = false
      this.x = 0
      this.y = 0
      this.items = []
    },
  },
})

export function resetContextMenuStore(): void {
  useContextMenuStore().reset()
}
