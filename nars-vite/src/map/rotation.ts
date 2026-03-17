// ─── MAP ROTATION ─────────────────────────────────────────────────────────────
// Adds two rotation buttons (↺ ↻) and a compass reset button to the map.
// Uses leaflet-rotate plugin which handles tile reprojection and marker
// counter-rotation automatically.

import { ctx } from './state'
import { t }   from '../i18n'

declare const L: typeof import('leaflet')

let currentBearing = 0   // degrees, clockwise from north
const STEP = 5          // degrees per button press

function setBearing(deg: number): void {
    currentBearing = ((deg % 360) + 360) % 360
    ;(ctx.map as any).setBearing(currentBearing)
    updateCompass()
}

function updateCompass(): void {
    // bearing updated
}

export function initRotationControls(): void {
    if (!(ctx.map as any).setBearing) {
        console.warn('leaflet-rotate not loaded — rotation controls skipped')
        return
    }

    // ── Build control container ───────────────────────────────────────────────
    const RotationControl = L.Control.extend({
        options: { position: 'bottomright' },
        onAdd() {
            const wrap = L.DomUtil.create('div', 'nars-rotation-control leaflet-bar')
            L.DomEvent.disableClickPropagation(wrap)
            L.DomEvent.disableScrollPropagation(wrap)

            // Rotate counter-clockwise
            const ccw = L.DomUtil.create('button', 'nars-map-btn', wrap)
            ccw.title = t('rotate_ccw')
            ccw.innerHTML = '↺'
            L.DomEvent.on(ccw, 'click', () => setBearing(currentBearing - STEP))

            // Rotate clockwise
            const cw = L.DomUtil.create('button', 'nars-map-btn', wrap)
            cw.title = t('rotate_cw')
            cw.innerHTML = '↻'
            L.DomEvent.on(cw, 'click', () => setBearing(currentBearing + STEP))

            return wrap
        },
    })

    new RotationControl().addTo(ctx.map)
}
