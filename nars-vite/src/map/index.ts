// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────
// Initialises the Leaflet map, registers draw events, and exposes phase
// navigation. Heavy sub-concerns live in dedicated modules:
//
//   draw-control.ts  — buildDrawControl / updateLayerEditability
//   draw-events.ts   — all pm:* and map event wiring
//   create-handler.ts — pm:create business logic
//   loader.ts        — loadFromDatabase / loadUserAndCommune

import { watch }                                               from 'vue'
import { t, applyInitialLang, currentLang }                   from '../i18n'
import { PHASES }                                             from '../phases'
import { store, featureLayers }                               from '../store'
import { checkDistrictCoverage }                              from '../validation'
import type { LayerEntry }                                    from '../types'
import { ctx }                                                from './state'
import { refreshLayerVisibility, refreshAllEdgeLabels }        from './labels'
import { displayCommuneBoundary }                             from './geometry'
import { disableSnapping, installSnapInterceptors }           from './snapping'
import { buildDrawControl }                                   from './draw-control'
import { registerDrawEvents }                                 from './draw-events'

declare const L: typeof import('leaflet')

// ─── RE-EXPORTS ───────────────────────────────────────────────────────────────
// Public API consumed by Vue components and main.ts.

export { displayCommuneBoundary }                           from './geometry'
export { bindContextMenu }                                  from './context-menu'
export { fetchRoadSide, computeBisNumber }                  from './features'
export { createEntranceIcon, areaStyle }                    from './styles'
export { createPolygonEdgeLabel, createAreaPerimeterLabel } from './labels'
export { loadFromDatabase, loadUserAndCommune }             from './loader'

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

export async function initMap(): Promise<void> {
    ctx.map = L.map('map', { zoomControl: false }).setView([28.0, 2.5], 5)

    // Disable Geoman's built-in snap indicator — NARS uses custom snapping
    // logic (snapping.ts). Leaving this on causes Geoman's dot to jump randomly
    // to nearby features during draw mode.
    ;(ctx.map as any).pm?.setGlobalOptions?.({ snappable: false })

    await applyInitialLang()

    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', { attribution: 'Tiles © Esri' })
    const street    = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',  { attribution: '© OpenStreetMap contributors' })
    const carto     = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    const dark      = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',  { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    satellite.addTo(ctx.map)

    const baseLayerLabelKeys = ['layer_satellite', 'layer_street', 'layer_light', 'layer_dark'] as const

    const updateLayerControlLabels = () => {
        if (!ctx.layerControl) return
        const container = ctx.layerControl.getContainer?.()
        if (!container) return
        const labels = Array.from(container.querySelectorAll('label'))
        for (let i = 0; i < baseLayerLabelKeys.length; i++) {
            const span = labels[i]?.querySelector('span')
            if (span) span.textContent = t(baseLayerLabelKeys[i])
        }
    }

    const buildLayerControl = () => {
        if (!ctx.map) return
        if (ctx.layerControl) ctx.map.removeControl(ctx.layerControl)
        ctx.layerControl = L.control.layers({
            [t('layer_satellite')]: satellite,
            [t('layer_street')]:    street,
            [t('layer_light')]:     carto,
            [t('layer_dark')]:      dark,
        }, undefined, { position: 'bottomleft' }).addTo(ctx.map)
        updateLayerControlLabels()
    }

    buildLayerControl()
    ;(window as any).__narsUpdateLayerControl = () => updateLayerControlLabels()
    watch(currentLang, () => updateLayerControlLabels())

    ctx.drawnItems            = new L.FeatureGroup().addTo(ctx.map)
    ctx.displayOverlayLayer   = L.layerGroup().addTo(ctx.map)
    ctx.roadsDisplayLayer     = L.layerGroup().addTo(ctx.map)
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

// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────

export async function navigatePhase(direction: number): Promise<void> {
    const target = store.currentPhase + direction
    if (target < 0 || target >= PHASES.length) return

    if (direction > 0) {
        const from = PHASES[store.currentPhase]
        if (from.key === 'areas'        && featureLayers.areas.length === 0)
            { alert(t('alert_at_least_one_urban_area')); return }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) { alert(t('alert_coverage_error', { message: coverage.message })); return }
        }
        if (from.key === 'roads'          && featureLayers.roads.length === 0)
            { alert(t('alert_at_least_one_road')); return }
        if (from.key === 'houseEntrances' && featureLayers.houseEntrances.length === 0)
            { alert(t('alert_at_least_one_entrance')); return }

        // Compute road directions when leaving the Roads phase so the full
        // network topology is known.
        if (from.key === 'roads') {
            const { computeAndApplyRoadDirections } = await import('./road-directions')
            await computeAndApplyRoadDirections()
        }
    }

    setPhase(target)
}

export async function goToPhase(target: number): Promise<void> {
    if (target === store.currentPhase) return
    if (target > store.currentPhase) {
        for (let i = store.currentPhase; i < target; i++) {
            const before = store.currentPhase
            await navigatePhase(1)
            if (store.currentPhase === before) return
        }
    } else {
        setPhase(target)
    }
}

export function setPhase(index: number): void {
    store.currentPhase = index
    const phase = PHASES[index]
    buildDrawControl(phase)

    if (phase.key === 'namingPanels') {
        try { (ctx.map as any).pm.disableDraw() } catch {}
        try {
            if ((featureLayers.namingPanels?.length ?? 0) === 0)
                import('./naming-panels').then(m => m.generateNamingPanels())
        } catch (err) { console.error('Auto-generate naming panels error:', err) }
    }

    disableSnapping()
    refreshLayerVisibility()
    setTimeout(refreshLayerVisibility, 50)
}
