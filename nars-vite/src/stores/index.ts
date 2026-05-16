// ─── STORE INDEX ──────────────────────────────────────────────────────────────
// Centralized exports for all Pinia stores.
//
// Usage:
//   import { useAppStore, useModalStore, useLayerStore } from './stores'

export { useAppStore } from "./appStore"
export {
  useModalStore,
  awaitModalResult,
  setCurrentModalFeatureId,
  currentModalFeatureId,
  openModal,
  openEditModal,
  resolveModal,
} from "./modalStore"
export { useLayerStore, setSelectedFeature, selectedFeatureDbId } from "./layerStore"
export { useFieldStore } from "./fieldStore"
export type { LayerState } from "./layerStore"
