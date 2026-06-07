// ─── STORE COMPATIBILITY LAYER ───────────────────────────────────────────────
// Re-exports from Pinia stores for backward compatibility.
// New code should import directly from '../stores' instead.
//
// ⚠ DEPRECATED — will be removed by 2026-Q3.
//    Last remaining consumers of this file must migrate to '../stores'.
//    See TODO.md for details.

export {
  useAppStore,
  useModalStore,
  useLayerStore,
  useSelectionStore,
  awaitModalResult,
  setCurrentModalFeatureId,
  currentModalFeatureId,
} from "../stores"

export type { LayerState } from "../stores"
