// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────
// Uses Geoman's native enableDraw() for the current phase's geometry type.
// Mirrors the reference implementation in nars-vite/src/map/draw-control.ts

import { ctx } from './state'
import { debugLog, debugError } from '../utils/debug'
import type { DrawModeName } from '@geoman-io/maplibre-geoman-free'

type PhaseConfig = { key: string; drawType: string; color: string }

// Map NARS drawType strings to Geoman shape names
const DRAW_TYPE_MAP: Record<string, DrawModeName> = {
    polygon: 'polygon',
    polyline: 'line',
    marker: 'marker',
    circle: 'circle',
}

// Track the last phase key we built for — skip if unchanged
let lastPhaseKey: string | null = null
let modeSwitchToken = 0

export function buildDrawControl(phase: PhaseConfig): void {
    const gm = ctx.geoman
    if (!gm) return

    const shapeName = DRAW_TYPE_MAP[phase.drawType]
    if (!shapeName) return

    debugLog('[DRAW CONTROL] Phase:', phase.key, '| drawType:', phase.drawType, '| shape:', shapeName)

    // Check if we're already in the correct draw mode.
    // Note: getActiveDrawModes may not exist on all Geoman versions; falls back to [].
    const activeModes = gm.getActiveDrawModes?.() || []
    if (lastPhaseKey === phase.key && activeModes.length > 0 && activeModes[0] === shapeName) {
        debugLog('[DRAW CONTROL] Already in correct draw mode:', shapeName)
        return
    }

    lastPhaseKey = phase.key
    const token = ++modeSwitchToken

    void (async () => {
        // Disable current draw mode only if one is active
        if (activeModes.length > 0) {
            debugLog('[DRAW CONTROL] Disabling current draw mode:', activeModes)
            try {
                await gm.disableDraw()
            } catch (err) {
                debugError('[DRAW CONTROL] Failed to disable draw mode:', err)
            }
        }

        // Use a short delay to let Geoman settle before enabling new draw mode.
        await new Promise((resolve) => setTimeout(resolve, 50))

        // Double-check we haven't been superseded.
        if (token !== modeSwitchToken || lastPhaseKey !== phase.key) return

        // Re-read ctx.geoman in case it was replaced or nulled after the timeout was scheduled.
        const currentGm = ctx.geoman
        if (!currentGm) return

        // Enable the correct draw mode for this phase.
        debugLog('[DRAW CONTROL] Enabling draw mode:', shapeName)
        currentGm
            .enableDraw(shapeName)
            .then(() => debugLog('[DRAW CONTROL] Enabled draw mode:', shapeName))
            .catch((err) => debugError('[DRAW CONTROL] Failed to enable draw mode:', shapeName, err))
    })()
}
