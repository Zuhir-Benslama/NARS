import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useUndoStore } from "./undoStore"
import type { LayerEntry } from "../types"

function makeEntry(id: number): LayerEntry {
  return {
    id: `feat-${id}`,
    dbId: `db-${id}`,
    type: "polygon",
    data: {
      type: "areas",
      label: `Area ${id}`,
      decisionNumber: "",
      decisionDate: "",
      areaTypeKey: "central_urban",
    },
  } as LayerEntry
}

describe("useUndoStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("caps the undo stack at 100 entries, dropping the oldest", () => {
    const store = useUndoStore()
    for (let i = 0; i < 150; i++) {
      store.recordDelete(makeEntry(i), "areas")
    }
    expect(store.undoStack.length).toBe(100)
    expect(store.undoStack[0].entry.id).toBe("feat-50")
    expect(store.undoStack[99].entry.id).toBe("feat-149")
  })
})
