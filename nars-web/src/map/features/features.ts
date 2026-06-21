// ─── FEATURE DATA, SAVE & MODAL HELPERS ──────────────────────────────────────
// Backward-compatibility re-exports from focused submodules.
//
// Consumers should migrate to the specific submodule:
//   - map/features/feature-data.ts  (buildFeatureData, toApiSaveShape)
//   - map/features/feature-persistence.ts  (saveToDatabase)
//   - map/features/feature-modal.ts  (prepareModalExtras, fetchRoadSide, computeBisNumber)

export { buildFeatureData, toApiSaveShape } from "./feature-data"
export { saveToDatabase } from "./feature-persistence"
export { prepareModalExtras, fetchRoadSide, computeBisNumber } from "./feature-modal"
