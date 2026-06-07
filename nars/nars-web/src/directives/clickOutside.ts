import type { Directive, DirectiveBinding } from "vue"

const clickOutsideHandlers = new WeakMap<HTMLElement, (e: MouseEvent) => void>()

export const vClickOutside: Directive<HTMLElement, (e: MouseEvent) => void> = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    const handler = (e: MouseEvent) => {
      if (!el.contains(e.target as Node)) binding.value(e)
    }
    clickOutsideHandlers.set(el, handler)
    document.addEventListener("click", handler)
  },
  unmounted(el: HTMLElement) {
    const handler = clickOutsideHandlers.get(el)
    if (handler) {
      document.removeEventListener("click", handler)
      clickOutsideHandlers.delete(el)
    }
  },
}
