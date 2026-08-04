import { defineStore } from "pinia"
import type { LayerEntry, LatLng } from "../types"
import type { LngLatTuple } from "@geoman-io/maplibre-geoman-free"

export const useEditStore = defineStore("edit", {
  state: () => ({
    isEditMode: false,
    activeGeomanFeatureId: null as string | null,
    activeEditEntry: null as LayerEntry | null,
    activeEditCoordsSnapshot: null as LatLng[] | null,
    draggedVertexIndex: null as number | null,
    origSetLngLat: null as ((lngLat: LngLatTuple) => void) | null,
  }),

  actions: {
    setIsEditMode(v: boolean): void {
      this.isEditMode = v
    },
    setActiveGeomanFeatureId(id: string | null): void {
      this.activeGeomanFeatureId = id
    },
    setActiveEditEntry(entry: LayerEntry | null): void {
      this.activeEditEntry = entry
    },
    setActiveEditCoordsSnapshot(snapshot: LatLng[] | null): void {
      this.activeEditCoordsSnapshot = snapshot
    },
    setDraggedVertexIndex(index: number | null): void {
      this.draggedVertexIndex = index
    },
    setOrigSetLngLat(fn: ((lngLat: LngLatTuple) => void) | null): void {
      this.origSetLngLat = fn
    },
  },
})
