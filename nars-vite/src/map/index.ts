// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────
// Initialises the Leaflet map, layer groups, and base tiles, then delegates to
// the extracted sub-modules.
//
// All heavy logic was moved out during the v1.1-Beta extraction:
//   draw-control.ts   — buildDrawControl, updateLayerEditability
//   draw-events.ts    — registerDrawEvents (all pm:* handlers)
//   create-handler.ts — handlePmCreate, validatePlacement, discardCreatedLayer
//   phase-nav.ts      — navigatePhase, goToPhase, setPhase
//
// This file is intentionally thin: init + public re-exports.

import { applyInitialLang }        from '../i18n'
import { PHASES }                  from '../phases'
import { ctx }                     from './state'
import { initRotationControls }    from './rotation'
import { buildDrawControl }        from './draw-control'
import { registerDrawEvents }      from './draw-events'
import { refreshAllEdgeLabels }    from './labels'
import { installSnapInterceptors } from './snapping'

declare const L: typeof import('leaflet')

// ─── RE-EXPORTS — public API consumed by Vue components and main.ts ───────────

export { displayCommuneBoundary }                           from './geometry'
export { bindContextMenu }                                  from './context-menu'
export { fetchRoadSide, computeBisNumber }                  from './features'
export { createEntranceIcon, areaStyle }                    from './styles'
export { createPolygonEdgeLabel, createAreaPerimeterLabel } from './labels'
export { loadFromDatabase, loadUserAndCommune }             from './loader'
export { navigatePhase, goToPhase, setPhase }               from './phase-nav'

// ─── BASE LAYER SWITCHER ──────────────────────────────────────────────────────
// Populated inside initMap(). Exported so TileControl.vue can call it directly
// without the window namespace.

export let setBaseLayer: (key: string) => void = () => {}

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

export async function initMap(): Promise<void> {
    ctx.map = (L as any).map('map', { zoomControl: false, rotate: true, bearing: 0 })
        .setView([28.0, 2.5], 5)
    ctx.map.zoomControl?.remove()

    // Move the Leaflet control container outside leaflet-rotate's wrapper.
    // MutationObserver fires exactly once when the DOM node appears — no race.
    const mapEl = document.getElementById('map')
    if (mapEl) {
        const observer = new MutationObserver(() => {
            const ctrlCont = document.querySelector('.leaflet-control-container') as HTMLElement | null
            const rotWrap  = document.querySelector('.leaflet-rotate-map')         as HTMLElement | null
            if (ctrlCont && rotWrap && rotWrap.contains(ctrlCont)) {
                mapEl.appendChild(ctrlCont)
                ctrlCont.style.position      = 'absolute'
                ctrlCont.style.inset         = '0'
                ctrlCont.style.zIndex        = '1000'
                ctrlCont.style.pointerEvents = 'none'
                observer.disconnect()
            }
        })
        observer.observe(mapEl, { childList: true, subtree: true })
    }

    // Disable Geoman's built-in snap indicator (NARS uses its own snapping.ts).
    ;(ctx.map as any).pm?.setGlobalOptions?.({ snappable: false })

    await applyInitialLang()

    // ── Base tile layers ──────────────────────────────────────────────────────
    const satellite = L.tileLayer(
        'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
        { attribution: 'Tiles © Esri', maxNativeZoom: 19 })
    const street = L.tileLayer(
        'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
        { attribution: '© OpenStreetMap contributors' })
    const carto = L.tileLayer(
        'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
        { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    const dark = L.tileLayer(
        'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
        { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })

    satellite.addTo(ctx.map)
    ctx.satelliteLayer = satellite
    ctx.streetLayer    = street

    const baseLayers: Record<string, L.TileLayer> = { satellite, street, light: carto, dark }
    let activeBaseLayer: L.TileLayer = satellite

    setBaseLayer = (key: string) => {
        const next = baseLayers[key]
        if (!next || next === activeBaseLayer) return
        ctx.map.removeLayer(activeBaseLayer)
        ctx.map.addLayer(next)
        activeBaseLayer = next
    }

    initRotationControls()

    // ── Layer groups (order = z-index) ────────────────────────────────────────
    ctx.drawnItems            = new L.FeatureGroup().addTo(ctx.map)
    ctx.displayOverlayLayer   = L.layerGroup().addTo(ctx.map)
    ctx.roadsDisplayLayer     = L.layerGroup().addTo(ctx.map)   // roads always live here
    ctx.lineEndpointLayer     = L.layerGroup().addTo(ctx.map)
    ctx.scatteredLayer        = L.layerGroup().addTo(ctx.map)
    ctx.perimeterLabelLayer   = L.layerGroup().addTo(ctx.map)
    ctx.polygonEdgeLabelLayer = L.layerGroup().addTo(ctx.map)
    ctx.boundariesLayer       = null

    ctx.map.on('zoomend', refreshAllEdgeLabels)
    installSnapInterceptors()

    buildDrawControl(PHASES[0])
    registerDrawEvents()
}
