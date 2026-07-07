import { defineStore } from "pinia"
import type { LayerEntry, FeatureTypeKey } from "../types"

interface DeletedFeature {
  entry: LayerEntry
  phaseKey: FeatureTypeKey
}

export const useUndoStore = defineStore("undo", {
  state: () => ({
    undoStack: [] as DeletedFeature[],
  }),

  getters: {
    hasUndo: (state) => state.undoStack.length > 0,
    undoLabel: (state) => {
      const last = state.undoStack[state.undoStack.length - 1]
      return last ? `Restore "${last.entry.data.label}"` : null
    },
  },

  actions: {
    recordDelete(entry: LayerEntry, phaseKey: FeatureTypeKey): void {
      this.undoStack.push({ entry, phaseKey })
    },
    popUndo(): DeletedFeature | undefined {
      return this.undoStack.pop()
    },
  },
})
