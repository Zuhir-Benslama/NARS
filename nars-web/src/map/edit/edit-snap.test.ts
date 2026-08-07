import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"

const mockSetLngLat = vi.fn()
const mockProject = vi.fn()
const mockSnapPointForEdit = vi.fn()

function buildMockCtx() {
  return {
    map: { project: mockProject } as any,
    geoman: {
      markerPointer: {
        marker: { setLngLat: mockSetLngLat },
      },
    } as any,
  }
}

const mockCtx = buildMockCtx()

vi.mock("../core/state", () => ({
  getCtx: () => mockCtx,
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
  const fresh = buildMockCtx()
  mockCtx.map = fresh.map
  mockCtx.geoman = fresh.geoman
  await reloadModule()
})

describe("edit-snap", () => {
  describe("patchMarkerPointerSnap", () => {
    it("saves original setLngLat and replaces it", async () => {
      const { useSnapStore } = await import("../../stores/snapStore")
      const store = useSnapStore()
      expect(store.origMarkerSetLngLat).toBeNull()

      patchMarkerPointerSnap("entry-1")

      expect(store.origMarkerSetLngLat).not.toBeNull()
      expect(store.origMarkerSetLngLat).not.toBe(mockSetLngLat)
    })

    it("does not patch if marker pointer is missing", async () => {
      mockCtx.geoman = undefined

      const { useSnapStore } = await import("../../stores/snapStore")
      const store = useSnapStore()
      patchMarkerPointerSnap("entry-1")
      expect(store.origMarkerSetLngLat).toBeNull()
    })

    it("does not patch if already patched", async () => {
      const { useSnapStore } = await import("../../stores/snapStore")
      const store = useSnapStore()
      const savedFn = vi.fn()
      store.origMarkerSetLngLat = savedFn as never

      patchMarkerPointerSnap("entry-1")

      expect(store.origMarkerSetLngLat).toBe(savedFn)
    })

    it("patched function snaps position when snap found", async () => {
      patchMarkerPointerSnap("entry-1")
      const patchedFn = (mockCtx.geoman as any).markerPointer.marker.setLngLat

      mockSnapPointForEdit.mockReturnValue({ lat: 36.5, lng: 127.5 })
      patchedFn([10, 20])

      expect(mockProject).toHaveBeenCalledWith([10, 20])
      expect(mockSnapPointForEdit).toHaveBeenCalledWith(100, 200, "entry-1")
      expect(mockSetLngLat).toHaveBeenCalledWith([127.5, 36.5])
    })

    it("patched function uses original position when no snap found", async () => {
      patchMarkerPointerSnap("entry-1")
      const patchedFn = (mockCtx.geoman as any).markerPointer.marker.setLngLat

      mockSnapPointForEdit.mockReturnValue(null)
      patchedFn([10, 20])

      expect(mockSetLngLat).toHaveBeenCalledWith([10, 20])
    })
  })

  describe("unpatchMarkerPointerSnap", () => {
    it("restores original setLngLat and clears store", async () => {
      const { useSnapStore } = await import("../../stores/snapStore")
      const store = useSnapStore()

      patchMarkerPointerSnap("entry-1")

      unpatchMarkerPointerSnap()

      const restoredFn = (mockCtx.geoman as any).markerPointer.marker.setLngLat
      restoredFn([99, 88])
      expect(mockSetLngLat).toHaveBeenCalledWith([99, 88])
      expect(store.origMarkerSetLngLat).toBeNull()
    })

    it("always clears store.origMarkerSetLngLat even when no marker pointer", async () => {
      mockCtx.geoman = undefined

      const { useSnapStore } = await import("../../stores/snapStore")
      const store = useSnapStore()
      store.origMarkerSetLngLat = vi.fn() as never

      unpatchMarkerPointerSnap()

      expect(store.origMarkerSetLngLat).toBeNull()
    })
  })
})
