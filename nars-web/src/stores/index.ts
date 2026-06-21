// ─── STORE INDEX ──────────────────────────────────────────────────────────────
// Centralized exports for all Pinia stores.
//
// Usage:
//   import { useAppStore, useModalStore, useLayerStore } from './stores'
//
// Modal helpers (awaitModalResult, openModal, openEditModal, resolveModal) are
// re-exported from modalStore for convenience, not Pinia stores themselves.

export { useAppStore } from "./appStore"
export {
  useModalStore,
  awaitModalResult,
  openModal,
  openEditModal,
  resolveModal,
} from "./modalStore"
export { useLayerStore } from "./layerStore"
export { useSelectionStore } from "./selectionStore"
export { useFieldStore } from "./fieldStore"
export type { LayerState } from "./layerStore"
