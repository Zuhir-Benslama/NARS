import { defineStore } from "pinia"
import type { LayerEntry, FeatureTypeKey } from "../types"

interface DeletedFeature {
  entry: LayerEntry
  phaseKey: FeatureTypeKey
}

const MAX_UNDO_ENTRIES = 100

/** Undo entries beyond this depth are evicted (oldest first) — see undo.ts. */
export { MAX_UNDO_ENTRIES }

export const useUndoStore = defineStore("undo", {
  state: () => ({
    undoStack: [] as DeletedFeature[],
  }),

  actions: {
    recordDelete(entry: LayerEntry, phaseKey: FeatureTypeKey): void {
      this.undoStack.push({ entry, phaseKey })
      if (this.undoStack.length > MAX_UNDO_ENTRIES) {
        this.undoStack.shift()
      }
    },
    popUndo(): DeletedFeature | undefined {
      return this.undoStack.pop()
    },
    /** Remove the OLDEST entry without deleting any feature (stack overflow). */
    shiftUndo(): DeletedFeature | undefined {
      return this.undoStack.shift()
    },
  },
})
