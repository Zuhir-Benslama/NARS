// ─── STORE COMPATIBILITY LAYER ───────────────────────────────────────────────
// Re-exports from Pinia stores for backward compatibility.
// New code should import directly from '../stores' instead.

export {
  useAppStore,
  useModalStore,
  useLayerStore,
  awaitModalResult,
  setCurrentModalFeatureId,
  currentModalFeatureId,
  selectedFeatureDbId,
  setSelectedFeature,
} from "../stores"

export type { LayerState } from "../stores"

// ─── LEGACY REACTIVE PROXY ──────────────────────────────────────────────────
// Typed Proxy that reads/writes Pinia store state for legacy code.
// Gradually migrate away from this in favor of direct Pinia store usage.

import { useAppStore } from "../stores/appStore"
import { useModalStore } from "../stores/modalStore"
import { useLayerStore } from "../stores/layerStore"
import { setCurrentModalFeatureId, awaitModalResult } from "../stores/modalStore"
import type { AppStore, ModalResult, LayerEntry, FeatureData } from "../types"
import { PHASES } from "../phases"
import { t } from "../i18n"

/** Cast a Pinia $state to a record for dynamic property access. */
function stateAsRecord<T extends object>(state: T): Record<string, unknown> {
  return state as Record<string, unknown>
}

export const store: AppStore = new Proxy({} as AppStore, {
  get(_, prop: keyof AppStore) {
    const appStore = useAppStore()
    const modalStore = useModalStore()

    if (prop === "modal") return modalStore.$state

    const state = appStore.$state as AppStore
    if (prop in state) return state[prop]

    return undefined
  },
  set(_, prop: keyof AppStore, value: AppStore[keyof AppStore]) {
    const appStore = useAppStore()
    const modalStore = useModalStore()

    if (prop === "modal") {
      Object.assign(modalStore, value)
      return true
    }

    const state = stateAsRecord(appStore.$state)
    if (prop in state) {
      state[prop as string] = value
      return true
    }

    return false
  },
})

export const featureLayers: Record<string, LayerEntry[]> = new Proxy(
  {} as Record<string, LayerEntry[]>,
  {
    get(_, prop: string) {
      const layerStore = useLayerStore()
      const state = stateAsRecord(layerStore.$state)
      return (state[prop] as LayerEntry[]) ?? []
    },
    set(_, prop: string, value: LayerEntry[]) {
      const layerStore = useLayerStore()
      const state = stateAsRecord(layerStore.$state)
      if (prop in state) {
        state[prop] = value
        return true
      }
      return false
    },
  },
)

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

export function openModal(
  phaseIndex: number,
  featureId: string,
  extras?: { radius?: number },
): Promise<ModalResult | null> {
  const modalStore = useModalStore()

  setCurrentModalFeatureId(featureId)
  modalStore.openCreate(phaseIndex, extras)

  const phase = PHASES[phaseIndex]
  if (phase?.key === "cityCenter") {
    modalStore.label = t("phase_cityCenter_label")
  }

  return awaitModalResult()
}

export function openEditModal(
  phaseIndex: number,
  dbId: string,
  existing: FeatureData,
): Promise<ModalResult | null> {
  const modalStore = useModalStore()

  modalStore.openEdit(phaseIndex, dbId, existing)
  return awaitModalResult()
}

export function resolveModal(result: ModalResult | null): void {
  const modalStore = useModalStore()
  modalStore.close(result)
}
