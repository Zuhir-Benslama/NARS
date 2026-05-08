// ─── BOUNDARY INTERACTION ─────────────────────────────────────────────────────
// Click/hover handlers and context menu for commune boundary features.

import maplibregl from "maplibre-gl"
import { showToast } from "../lib/toast"
import { escapeHtml } from "../utils/sanitize"

let boundaryEventsRegistered = false

export function addBoundaryClickEvents(map: maplibregl.Map): void {
  if (boundaryEventsRegistered) return
  boundaryEventsRegistered = true

  map.on("click", "nars-boundaries", (e: maplibregl.MapLayerMouseEvent) => {
    const name = escapeHtml(e.features?.[0]?.properties?.communeName || "Commune")
    new maplibregl.Popup({ closeButton: true, closeOnClick: true })
      .setLngLat(e.lngLat)
      .setHTML(`<strong>${name}</strong><br><small>Commune Boundary</small>`)
      .addTo(map)
  })

  map.on("mouseenter", "nars-boundaries", () => {
    map.getCanvas().style.setProperty("cursor", "pointer", "important")
  })

  map.on("mouseleave", "nars-boundaries", () => {
    map.getCanvas().style.removeProperty("cursor")
  })

  map.on("contextmenu", "nars-boundaries", (e: maplibregl.MapLayerMouseEvent) => {
    e.preventDefault()
    e.originalEvent?.preventDefault()
    showBoundaryContextMenu(
      e.point.x,
      e.point.y,
      e.features?.[0]?.properties?.communeName || "Commune",
    )
  })
}

function showBoundaryContextMenu(x: number, y: number, communeName: string): void {
  document.getElementById("nars-boundary-ctx-menu")?.remove()

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
  copyItem.textContent = "\uD83D\uDCCB Copy Name"
  menu.appendChild(copyItem)

  document.body.appendChild(menu)

  menu.style.left = (x + 180 > window.innerWidth ? x - 180 : x) + "px"
  menu.style.top = (y + 100 > window.innerHeight ? y - 100 : y) + "px"

  const hide = () => {
    menu.remove()
    document.removeEventListener("click", hide)
  }
  setTimeout(() => document.addEventListener("click", hide), 100)

  copyItem.onclick = (e) => {
    e.stopPropagation()
    navigator.clipboard.writeText(communeName)
    showToast(`Copied: ${communeName}`, "success")
    hide()
  }
}
