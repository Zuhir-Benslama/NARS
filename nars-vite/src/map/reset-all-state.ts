// ─── RESET ALL MODULE STATE ───────────────────────────────────────────────────
// Barrel that resets all module-level mutable state. Used in test setup
// (beforeEach) to ensure clean test isolation, and during HMR to prevent
// stale state from persisting across hot reloads.

import { resetDrawState } from "./draw/draw-state"
import { resetEditState } from "./edit/edit-state"
import { resetUndoStack } from "./undo"
import { resetSnapState } from "./snapping/snapping"
import { resetDrawControl } from "./draw/draw-control"
import { resetBoundaryEvents } from "./map-boundary"
import { resetRotation } from "./rotation"
import { resetMapState } from "./core/state"

export function resetAllState(): void {
  resetDrawState()
  resetEditState()
  resetUndoStack()
  resetSnapState()
  resetDrawControl()
  resetBoundaryEvents()
  resetRotation()
  resetMapState()
}
