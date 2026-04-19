// ─── STORE COMPATIBILITY LAYER ───────────────────────────────────────────────
// Legacy exports redirect to Pinia stores.
// Migrate component imports to use Pinia stores directly when convenient.
//   import { useAppStore } from './stores/appStore'
//   import { useModalStore } from './stores/modalStore'
//   import { useLayerStore } from './stores/layerStore'

import { useAppStore } from './stores/appStore'
import { useModalStore, awaitModalResult, setCurrentModalFeatureId } from './stores/modalStore'
import { useLayerStore } from './stores/layerStore'
import type { AppStore, ModalResult, LayerEntry } from './types'
import type { LayerState } from './stores/layerStore'
import { PHASES } from './phases'
import { t } from './i18n'

// ─── REACTIVE STORE (backward-compatible — redirects to Pinia) ───────────────
// Typed Proxy that safely reads/writes Pinia store state without unsafe casts.

export const store: AppStore = new Proxy({} as AppStore, {
    get(_, prop: keyof AppStore) {
        const appStore = useAppStore()
        const modalStore = useModalStore()

        // Modal properties
        if (prop === 'modal') return modalStore.$state

        // App store properties — typed access without `as unknown`
        const state = appStore.$state as AppStore
        if (prop in state) return state[prop]

        return undefined
    },
    set(_, prop: keyof AppStore, value: AppStore[keyof AppStore]) {
        const appStore = useAppStore()
        const modalStore = useModalStore()

        if (prop === 'modal') {
            Object.assign(modalStore, value)
            return true
        }

        const state = appStore.$state as unknown as Record<string, unknown>
        if (prop in state) {
            state[prop as string] = value
            return true
        }

        return false
    },
})

// ─── SELECTION ────────────────────────────────────────────────────────────────

export { selectedFeatureDbId, setSelectedFeature } from './stores/layerStore'

// ─── MODAL BRIDGE ─────────────────────────────────────────────────────────────

export { awaitModalResult, setCurrentModalFeatureId, currentModalFeatureId } from './stores/modalStore'

export function openModal(
    phaseIndex: number,
    featureId: string,
    extras?: { radius?: number },
): Promise<ModalResult | null> {
    const modalStore = useModalStore()

    setCurrentModalFeatureId(featureId)
    modalStore.openCreate(phaseIndex, extras)

    // Phase-specific defaults (city center always named "City Center")
    const phase = PHASES[phaseIndex]
    if (phase?.key === 'cityCenter') {
        modalStore.label = t('phase_cityCenter_label')
    }

    return awaitModalResult()
}

export function openEditModal(
    phaseIndex: number,
    dbId: string,
    existing: import('./types').FeatureData,
): Promise<ModalResult | null> {
    const modalStore = useModalStore()

    modalStore.openEdit(phaseIndex, dbId, existing)
    return awaitModalResult()
}

export function resolveModal(result: ModalResult | null): void {
    const modalStore = useModalStore()
    modalStore.close(result)
}

// ─── FEATURE LAYERS (backward-compatible — redirects to Pinia) ───────────────

// Legacy non-reactive export — now proxies the Pinia layer store state with typed access.
export const featureLayers: Record<string, LayerEntry[]> = new Proxy({} as Record<string, LayerEntry[]>, {
    get(_, prop: keyof LayerState | string) {
        const layerStore = useLayerStore()
        const state = layerStore.$state as LayerState
        return (state[prop as keyof LayerState] as LayerEntry[]) ?? []
    },
    set(_, prop: keyof LayerState | string, value: LayerEntry[]) {
        const layerStore = useLayerStore()
        const state = layerStore.$state as unknown as Record<string, unknown>
        if (prop in state) {
            state[prop as string] = value
            return true
        }
        return false
    },
})

// ── Sync helper (legacy — counts are now computed in Pinia) ──────────────────

export function syncCounts(): void {
    const appStore = useAppStore()
    const layerStore = useLayerStore()

    appStore.counts = {
        areas: layerStore.areaCount,
        cityCenter: layerStore.cityCenterCount,
        districts: layerStore.districtCount,
        roads: layerStore.roadCount,
        mainEntrances: layerStore.mainEntranceCount,
        secondaryEntrances: layerStore.secondaryEntranceCount,
        publicBuildings: layerStore.publicBuildingCount,
        publicSpaces: layerStore.publicSpaceCount,
        namingPanels: layerStore.namingPanelCount,
    }
}
