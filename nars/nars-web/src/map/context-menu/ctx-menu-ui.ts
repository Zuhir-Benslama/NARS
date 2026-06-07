// ─── CONTEXT MENU UI ──────────────────────────────────────────────────────────
// DOM-based context menu element creation, item rendering, and positioning.

interface CtxMenuItem {
  label?: string
  danger?: boolean
  separator?: boolean
  onClick?: () => void
}

let contextMenuEl: HTMLElement | null = null

function createContextMenuEl(): HTMLElement {
  const el = document.createElement("div")
  el.className = "nars-ctx-menu"
  el.style.position = "fixed"
  el.style.left = "-9999px"
  el.style.top = "-9999px"
  el.style.zIndex = "100000"
  el.style.display = "none"
  document.body.appendChild(el)

  const hide = () => {
    el.style.display = "none"
  }
  document.addEventListener("click", (e) => {
    if (!el.contains(e.target as Node)) hide()
  })
  document.addEventListener("contextmenu", (e) => {
    if (!el.contains(e.target as Node)) hide()
  })
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") hide()
  })
  return el
}

function getCtxEl(): HTMLElement {
  if (!contextMenuEl) contextMenuEl = createContextMenuEl()
  return contextMenuEl
}

export function setMenuItems(el: HTMLElement, items: CtxMenuItem[]): void {
  el.innerHTML = ""
  for (const item of items) {
    if (item.separator) {
      const sep = document.createElement("div")
      sep.style.borderTop = "1px solid var(--dropdown-border, #eee)"
      sep.style.margin = "2px 0"
      el.appendChild(sep)
      continue
    }
    const child = document.createElement("div")
    child.className = "nars-ctx-item"
    if (item.danger) child.style.color = "#ef4444"
    child.textContent = item.label!
    child.addEventListener("click", (e) => {
      e.stopPropagation()
      el.style.display = "none"
      item.onClick!()
    })
    el.appendChild(child)
  }
}

export function placeMenu(el: HTMLElement, x: number, y: number): void {
  el.style.left = "0"
  el.style.top = "0"
  el.style.display = "block"
  void el.offsetHeight
  const w = el.offsetWidth || 180,
    h = el.offsetHeight || 100
  el.style.left = (x + w > window.innerWidth ? x - w : x) + "px"
  el.style.top = (y + h > window.innerHeight ? y - h : y) + "px"
}

export { getCtxEl }
export type { CtxMenuItem }
