import { onMounted, onUnmounted, type Ref } from "vue"

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

function getFocusable(el: HTMLElement): HTMLElement[] {
  return Array.from(el.querySelectorAll<HTMLElement>(FOCUSABLE))
}

export function useFocusTrap(containerRef: Ref<HTMLElement | null>, isActive: () => boolean): void {
  let previousActiveElement: HTMLElement | null = null
  let observer: MutationObserver | null = null

  function refocus() {
    if (!isActive()) return
    const container = containerRef.value
    if (!container) return
    const focusable = getFocusable(container)
    if (focusable.length > 0 && !container.contains(document.activeElement)) {
      focusable[0].focus()
    }
  }

  function onKeydown(e: KeyboardEvent) {
    if (!isActive() || e.key !== "Tab") return
    const container = containerRef.value
    if (!container) return

    const focusable = getFocusable(container)
    if (focusable.length === 0) {
      e.preventDefault()
      return
    }

    const first = focusable[0]
    const last = focusable[focusable.length - 1]

    if (e.shiftKey) {
      if (document.activeElement === first) {
        e.preventDefault()
        last.focus()
      }
    } else {
      if (document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }
  }

  onMounted(() => {
    if (!isActive()) return
    previousActiveElement = document.activeElement as HTMLElement | null
    const container = containerRef.value
    if (container) {
      const focusable = getFocusable(container)
      if (focusable.length > 0) {
        focusable[0].focus()
      }
      observer = new MutationObserver(refocus)
      observer.observe(container, { childList: true, subtree: true })
    }
    window.addEventListener("keydown", onKeydown)
  })

  onUnmounted(() => {
    window.removeEventListener("keydown", onKeydown)
    observer?.disconnect()
    observer = null
    if (previousActiveElement && previousActiveElement.isConnected) {
      previousActiveElement.focus()
    }
  })
}
