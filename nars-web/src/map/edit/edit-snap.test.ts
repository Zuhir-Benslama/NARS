import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"

const mockSetLngLat = vi.fn()
const mockProject = vi.fn()
const mockSnapPointForEdit = vi.fn()

function buildMockCtx() {
  return {
    map: { project: mockProject },
    geoman: {
      markerPointer: {
        marker: { setLngLat: mockSetLngLat },
      },
    },
  }
}

vi.mock("../core/state", () => ({
  ctx: buildMockCtx(),
}))

vi.mock("../snapping/snapping", () => ({
  snapPointForEdit: mockSnapPointForEdit,
}))

let patchMarkerPointerSnap: (editEntryId: string | null) => void
let unpatchMarkerPointerSnap: () => void

async function reloadModule() {
  const mod = await import("./edit-snap")
  patchMarkerPointerSnap = mod.patchMarkerPointerSnap
  unpatchMarkerPointerSnap = mod.unpatchMarkerPointerSnap
}

beforeEach(async () => {
  setActivePinia(createPinia())
  mockSetLngLat.mockReset()
  mockProject.mockReset()
  mockSnapPointForEdit.mockReset()
  mockProject.mockReturnValue({ x: 100, y: 200 })
  const ctxMod = await import("../core/state")
  const fresh = buildMockCtx()
  ctxMod.ctx.map = fresh.map
  ctxMod.ctx.geoman = fresh.geoman
  await reloadModule()
})

describe("edit-snap", () => {
  describe("patchMarkerPointerSnap", () => {
    it("saves original setLngLat and replaces it", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()
      expect(store.origSetLngLat).toBeNull()

      patchMarkerPointerSnap("entry-1")

      expect(store.origSetLngLat).not.toBeNull()
      expect(store.origSetLngLat).not.toBe(mockSetLngLat)
    })

    it("does not patch if marker pointer is missing", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = undefined

      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()
      patchMarkerPointerSnap("entry-1")
      expect(store.origSetLngLat).toBeNull()
    })

    it("does not patch if already patched", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()
      const savedFn = vi.fn()
      store.origSetLngLat = savedFn as never

      patchMarkerPointerSnap("entry-1")

      expect(store.origSetLngLat).toBe(savedFn)
    })

    it("patched function snaps position when snap found", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()

      patchMarkerPointerSnap("entry-1")
      const ctxMod = await import("../core/state")
      const patchedFn = ctxMod.ctx.geoman!.markerPointer!.marker.setLngLat

      mockSnapPointForEdit.mockReturnValue({ lat: 36.5, lng: 127.5 })
      patchedFn([10, 20])

      expect(mockProject).toHaveBeenCalledWith([10, 20])
      expect(mockSnapPointForEdit).toHaveBeenCalledWith(100, 200, "entry-1")
      expect(mockSetLngLat).toHaveBeenCalledWith([127.5, 36.5])
    })

    it("patched function uses original position when no snap found", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()

      patchMarkerPointerSnap("entry-1")
      const ctxMod = await import("../core/state")
      const patchedFn = ctxMod.ctx.geoman!.markerPointer!.marker.setLngLat

      mockSnapPointForEdit.mockReturnValue(null)
      patchedFn([10, 20])

      expect(mockSetLngLat).toHaveBeenCalledWith([10, 20])
    })
  })

  describe("unpatchMarkerPointerSnap", () => {
    it("restores original setLngLat and clears store", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()

      patchMarkerPointerSnap("entry-1")
      const ctxMod = await import("../core/state")

      unpatchMarkerPointerSnap()

      const restoredFn = ctxMod.ctx.geoman!.markerPointer!.marker.setLngLat
      restoredFn([99, 88])
      expect(mockSetLngLat).toHaveBeenCalledWith([99, 88])
      expect(store.origSetLngLat).toBeNull()
    })

    it("always clears store.origSetLngLat even when no marker pointer", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = undefined

      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()
      store.origSetLngLat = vi.fn() as never

      unpatchMarkerPointerSnap()

      expect(store.origSetLngLat).toBeNull()
    })
  })
})
