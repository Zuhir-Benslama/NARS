// ─── BOUNDARY INTERACTION ─────────────────────────────────────────────────────
// Click/hover handlers and context menu for commune boundary features.

import maplibregl from "maplibre-gl"
import { showToast } from "../lib/toast"
import { escapeHtml } from "../utils/sanitize"
import { useAppStore } from "../stores/appStore"
import { t } from "../i18n"

const CTX_MENU_WIDTH = 180
const CTX_MENU_HEIGHT = 100

let _currentBoundaryCleanup: (() => void) | null = null
let _boundaryMap: maplibregl.Map | null = null

// ─── STATE RESET (for testing & HMR) ──────────────────────────────────────────

export function resetBoundaryEvents(): void {
  removeBoundaryClickEvents()
  _currentBoundaryCleanup?.()
  _currentBoundaryCleanup = null
}

export function addBoundaryClickEvents(map: maplibregl.Map): void {
  const store = useAppStore()
  if (store.boundaryEventsRegistered) return
  store.setBoundaryEventsRegistered(true)
  _boundaryMap = map

  map.on("click", "nars-boundaries", onBoundaryClick)
  map.on("mouseenter", "nars-boundaries", onBoundaryEnter)
  map.on("mouseleave", "nars-boundaries", onBoundaryLeave)
  map.on("contextmenu", "nars-boundaries", onBoundaryContextMenu)
}

export function removeBoundaryClickEvents(): void {
  useAppStore().setBoundaryEventsRegistered(false)
  const map = _boundaryMap
  _boundaryMap = null
  if (!map) return
  map.off("click", "nars-boundaries", onBoundaryClick)
  map.off("mouseenter", "nars-boundaries", onBoundaryEnter)
  map.off("mouseleave", "nars-boundaries", onBoundaryLeave)
  map.off("contextmenu", "nars-boundaries", onBoundaryContextMenu)
}

function onBoundaryClick(e: maplibregl.MapLayerMouseEvent): void {
  const name = escapeHtml(e.features?.[0]?.properties?.communeName || t("map_commune_label"))
  new maplibregl.Popup({ closeButton: true, closeOnClick: true })
    .setLngLat(e.lngLat)
    .setHTML(`<strong>${name}</strong><br><small>${t("map_commune_boundary")}</small>`)
    .addTo(_boundaryMap!)
}

function onBoundaryEnter(): void {
  _boundaryMap?.getCanvas().style.setProperty("cursor", "pointer", "important")
}

function onBoundaryLeave(): void {
  _boundaryMap?.getCanvas().style.removeProperty("cursor")
}

function onBoundaryContextMenu(e: maplibregl.MapLayerMouseEvent): void {
  e.preventDefault()
  e.originalEvent?.preventDefault()
  showBoundaryContextMenu(
    e.point.x,
    e.point.y,
    e.features?.[0]?.properties?.communeName || t("map_commune_label"),
  )
}

function showBoundaryContextMenu(x: number, y: number, communeName: string): void {
  _currentBoundaryCleanup?.()
  _currentBoundaryCleanup = null

  const menu = document.createElement("div")
  menu.id = "nars-boundary-ctx-menu"
  menu.className = "nars-ctx-menu"

  const nameItem = document.createElement("div")
  nameItem.className = "nars-ctx-item"
  nameItem.style.fontWeight = "bold"
  nameItem.style.color = "#666"
  nameItem.style.cursor = "default"
  nameItem.textContent = communeName
  menu.appendChild(nameItem)

  const sep = document.createElement("div")
  sep.style.borderTop = "1px solid #eee"
  sep.style.margin = "4px 0"
  menu.appendChild(sep)

  const copyItem = document.createElement("div")
  copyItem.className = "nars-ctx-item"
  copyItem.dataset.action = "copy-name"
  copyItem.textContent = "\uD83D\uDCCB " + t("map_copy_name")
  menu.appendChild(copyItem)

  document.body.appendChild(menu)

  menu.style.left = (x + CTX_MENU_WIDTH > window.innerWidth ? x - CTX_MENU_WIDTH : x) + "px"
  menu.style.top = (y + CTX_MENU_HEIGHT > window.innerHeight ? y - CTX_MENU_HEIGHT : y) + "px"

  const hide = () => {
    menu.remove()
    document.removeEventListener("click", hide)
    document.removeEventListener("contextmenu", hide)
    _currentBoundaryCleanup = null
  }
  _currentBoundaryCleanup = hide
  requestAnimationFrame(() => {
    document.addEventListener("click", hide)
    document.addEventListener("contextmenu", hide)
  })

  copyItem.onclick = (e) => {
    e.stopPropagation()
    navigator.clipboard.writeText(communeName).then(
      () => showToast(t("map_copied_name", { name: communeName }), "success"),
      () => showToast(t("map_copy_failed"), "error"),
    )
    hide()
  }
}
