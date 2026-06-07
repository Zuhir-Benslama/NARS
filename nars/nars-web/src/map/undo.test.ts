import { describe, it, expect, beforeEach } from "vitest"
import type { LayerEntry } from "../types"
import type { FeatureData } from "../types"
import { resetUndoStack, hasUndo, getUndoLabel, recordDelete } from "./undo"

function makeEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "id-1",
    dbId: "db-1",
    type: "polygon",
    data: { type: "areas", label: "Test Area" } as FeatureData,
    ...overrides,
  }
}

describe("undo stack", () => {
  beforeEach(() => {
    resetUndoStack()
  })

  describe("resetUndoStack", () => {
    it("clears the stack", () => {
      recordDelete(makeEntry(), "areas")
      expect(hasUndo()).toBe(true)
      resetUndoStack()
      expect(hasUndo()).toBe(false)
    })
  })

  describe("hasUndo", () => {
    it("returns false on empty stack", () => {
      expect(hasUndo()).toBe(false)
    })

    it("returns true after recording a delete", () => {
      recordDelete(makeEntry(), "areas")
      expect(hasUndo()).toBe(true)
    })
  })

  describe("getUndoLabel", () => {
    it("returns null on empty stack", () => {
      expect(getUndoLabel()).toBeNull()
    })

    it("returns label with entry name", () => {
      recordDelete(makeEntry({ data: { type: "areas", label: "Zone A" } as FeatureData }), "areas")
      expect(getUndoLabel()).toBe('Restore "Zone A"')
    })
  })

  describe("recordDelete", () => {
    it("pushes entry onto the stack", () => {
      const entry = makeEntry({ id: "my-id", dbId: "my-db" })
      recordDelete(entry, "roads")
      expect(hasUndo()).toBe(true)
      expect(getUndoLabel()).toContain("Test Area")
    })

    it("supports multiple entries (LIFO)", () => {
      recordDelete(makeEntry({ data: { type: "areas", label: "First" } as FeatureData }), "areas")
      recordDelete(makeEntry({ data: { type: "roads", label: "Second" } as FeatureData }), "roads")
      expect(getUndoLabel()).toContain("Second")
    })
  })
})
