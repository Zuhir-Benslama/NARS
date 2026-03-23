// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────
// navigatePhase — step forward/back with pre-flight guards (coverage, min items)
// goToPhase     — jump directly to a target phase (used by PhaseBar clicks)
// setPhase      — low-level: update store, rebuild draw control, refresh visibility
//
// Extracted from index.ts.

import { t }                                        from '../i18n'
import { PHASES }                                   from '../phases'
import { store, featureLayers }                     from '../store'
import { checkDistrictCoverage }                    from '../validation'
import { ctx }                                      from './state'
import { buildDrawControl }                         from './draw-control'
import { disableSnapping }                          from './snapping'
import { refreshLayerVisibility }                   from './labels'

declare const L: typeof import('leaflet')

// ── navigatePhase ─────────────────────────────────────────────────────────────
// Steps the phase by +1 or -1. Forward navigation runs pre-flight guards
// (min feature counts, district coverage, road-direction computation).

export async function navigatePhase(direction: number): Promise<void> {
    const target = store.currentPhase + direction
    if (target < 0 || target >= PHASES.length) return

    if (direction > 0) {
        const from = PHASES[store.currentPhase]

        if (from.key === 'areas' && featureLayers.areas.length === 0) {
            alert(t('alert_at_least_one_urban_area')); return
        }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) { alert(t('alert_coverage_error', { message: coverage.message })); return }
        }
        if (from.key === 'roads' && featureLayers.roads.length === 0) {
            alert(t('alert_at_least_one_road')); return
        }
        if (from.key === 'houseEntrances' && featureLayers.houseEntrances.length === 0) {
            alert(t('alert_at_least_one_entrance')); return
        }

        // Compute road directions when leaving the Roads phase.
        // Done here — not during drawing — so the full network topology is known.
        if (from.key === 'roads') {
            const { computeAndApplyRoadDirections } = await import('./road-directions')
            await computeAndApplyRoadDirections()
        }
    }

    setPhase(target)
}

// ── goToPhase ─────────────────────────────────────────────────────────────────
// Jumps directly to a target phase index. Going forward runs each intermediate
// navigatePhase guard in sequence; going backward jumps directly (no guards).

export async function goToPhase(target: number): Promise<void> {
    if (target === store.currentPhase) return
    if (target > store.currentPhase) {
        for (let i = store.currentPhase; i < target; i++) {
            const before = store.currentPhase
            await navigatePhase(1)
            // Guard fired — abort if the phase didn't advance
            if (store.currentPhase === before) return
        }
    } else {
        setPhase(target)
    }
}

// ── setPhase ──────────────────────────────────────────────────────────────────
// Low-level phase setter: updates the store, rebuilds Geoman draw control,
// handles Naming Panels cursor/lock, and refreshes layer visibility.

export function setPhase(index: number): void {
    store.currentPhase = index
    const phase = PHASES[index]
    buildDrawControl(phase)

    if (phase.key === 'namingPanels') {
        // Naming Panels is display-only — fully disable all Geoman interactions.
        try { (ctx.map as any).pm.disableDraw() } catch { /* ignore */ }
        try { (ctx.map as any).pm.disableGlobalEditMode() } catch { /* ignore */ }
        try { (ctx.map as any).pm.disableGlobalDragMode() } catch { /* ignore */ }
        try { (ctx.map as any).pm.disableGlobalRemovalMode() } catch { /* ignore */ }
        try { (ctx.map as any).pm.setGlobalOptions({ pmIgnore: true }) } catch { /* ignore */ }

        // Set grab cursor after a tick so it overrides any Geoman cursor changes.
        setTimeout(() => {
            try { (ctx.map as any).pm.disableDraw() } catch { /* ignore */ }
            ctx.map.getContainer().style.cursor = 'grab'
        }, 50)

        ctx.map.getContainer().addEventListener('mousedown', () => {
            if (PHASES[store.currentPhase]?.key === 'namingPanels')
                ctx.map.getContainer().style.cursor = 'grabbing'
        })
        ctx.map.getContainer().addEventListener('mouseup', () => {
            if (PHASES[store.currentPhase]?.key === 'namingPanels')
                ctx.map.getContainer().style.cursor = 'grab'
        })

        // Auto-generate naming panels once when entering this phase.
        if ((featureLayers.namingPanels?.length ?? 0) === 0) {
            import('./naming-panels')
                .then(m => m.generateNamingPanels())
                .catch(err => console.error('Auto-generate naming panels error:', err))
        }
    } else {
        // Restore Geoman and cursor when leaving the Naming Panels phase.
        try { (ctx.map as any).pm.setGlobalOptions({ pmIgnore: false }) } catch { /* ignore */ }
        ctx.map.getContainer().style.cursor = ''
    }

    disableSnapping()
    refreshLayerVisibility()
    // Re-run after Geoman's deferred pm.enableDraw settles — it can re-process
    // layers and override the visibility set in the first call.
    setTimeout(refreshLayerVisibility, 50)
}
