// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────

import { t } from '../i18n'
import { PHASES } from '../phases'
import { store } from '../store'
import { useLayerStore } from '../stores/layerStore'
import type { LayerState } from '../stores/layerStore'
import { showToast } from '../toast'
import { checkDistrictCoverage } from '../validation'
import { buildDrawControl } from './draw-control'
import { setDrawingPhase } from './draw-complete'
import { refreshLayerVisibility } from './labels'
import { computeAndApplyRoadDirections } from './road-directions'
import { savePhase } from './phase-storage'

export async function navigatePhase(direction: number): Promise<void> {
    const target = store.currentPhase + direction
    if (target < 0 || target >= PHASES.length) return

    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState

    if (direction > 0) {
        const from = PHASES[store.currentPhase]

        if (from.key === 'areas' && (state.areas?.length ?? 0) === 0) {
            showToast(t('alert_at_least_one_urban_area'), 'error')
            return
        }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) {
                showToast(t('alert_coverage_error', { message: coverage.message }), 'error')
                return
            }
        }
        if (from.key === 'roads' && (state.roads?.length ?? 0) === 0) {
            showToast(t('alert_at_least_one_road'), 'error')
            return
        }
        if (from.key === 'houseEntrances' && (state.houseEntrances?.length ?? 0) === 0) {
            showToast(t('alert_at_least_one_entrance'), 'error')
            return
        }

        // When leaving roads phase, auto-orient road directions
        if (from.key === 'roads' && (state.roads?.length ?? 0) > 0) {
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
    const communeId = (store.user as { commune?: { id?: number | string } } | null | undefined)?.commune?.id ?? null
    savePhase(index, communeId)
    const phase = PHASES[index]
    setDrawingPhase(phase ?? null)
    if (phase) {
        buildDrawControl(phase)
    }
    refreshLayerVisibility()
}
