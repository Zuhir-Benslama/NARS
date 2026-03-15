// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────
// Builds and activates Geoman draw mode for a given phase, and stamps pmIgnore
// on all non-current-phase layers so Geoman's global edit mode only touches the
// active phase. Extracted from index.ts to break the loader ↔ index circular dep.

import { ctx, POLYLINE_WEIGHT } from './state'
import { createEntranceIcon }   from './styles'
import { editModeActive }       from './snapping'
import { featureLayers }        from '../store'
import type { LayerEntry }      from '../types'

declare const L: typeof import('leaflet')

type PhaseConfig = { key: string; drawType: string; color: string }

// ─── LAYER EDITABILITY ────────────────────────────────────────────────────────
// Sets pmIgnore:true on every non-current-phase feature so Geoman's global
// edit mode never picks them up, regardless of which LayerGroup they live in.

export function updateLayerEditability(currentPhaseKey: string): void {
    for (const [key, entries] of Object.entries(featureLayers)) {
        const editable = key === currentPhaseKey
        for (const { layer } of entries as LayerEntry[]) {
            ;(layer as any).options.pmIgnore = !editable
            ;(layer as any).pm?.setOptions?.({ pmIgnore: !editable })
        }
    }
}

// ─── BUILD DRAW CONTROL ───────────────────────────────────────────────────────
// Configures Geoman drawing styles for the given phase and auto-starts draw
// mode (unless edit mode is active). No toolbar is rendered — all draw controls
// are programmatic.

export function buildDrawControl(phase: PhaseConfig): void {
    ctx.map.pm.removeControls()

    if (phase.drawType === 'polygon') {
        ctx.map.pm.setGlobalOptions({
            pathOptions: {
                color:       phase.color,
                weight:      2.5,
                fillOpacity: phase.key === 'areas' ? 0 : 0.15,
                dashArray:   phase.key === 'areas' ? '10, 6' : undefined,
            },
            snappable: false,
        } as any)
    } else if (phase.drawType === 'polyline') {
        ctx.map.pm.setGlobalOptions({
            templineStyle: { color: phase.color, weight: POLYLINE_WEIGHT },
            hintlineStyle: { color: phase.color, weight: POLYLINE_WEIGHT },
            snappable: false,
        } as any)
    } else if (phase.drawType === 'circle') {
        ctx.map.pm.setGlobalOptions({
            pathOptions: { color: '#e74c3c', weight: 2, fillColor: '#e74c3c', fillOpacity: 0.15 },
        } as any)
    } else if (phase.drawType === 'marker') {
        const icon = createEntranceIcon('?', phase.color)
        ctx.map.pm.setGlobalOptions({ markerStyle: { icon } } as any)
    }

    if (!editModeActive) {
        if      (phase.drawType === 'polygon')  setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 0)
        else if (phase.drawType === 'polyline') setTimeout(() => (ctx.map as any).pm.enableDraw('Line',    { snappable: false }), 0)
        else if (phase.drawType === 'marker')   setTimeout(() => (ctx.map as any).pm.enableDraw('Marker',  { snappable: false }), 0)
        else if (phase.drawType === 'circle')   setTimeout(() => (ctx.map as any).pm.enableDraw('Circle',  { snappable: false }), 0)
    }

    updateLayerEditability(phase.key)
}
