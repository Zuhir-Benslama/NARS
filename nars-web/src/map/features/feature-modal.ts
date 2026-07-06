import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { useLayerStore } from "../../stores/layerStore"
import { checkMainUrbanExists, getRoadSide } from "../../lib/validation"
import type { LayerEntry } from "../../types"

export async function prepareModalExtras(phase: (typeof PHASES)[number]): Promise<void> {
  const modalStore = useModalStore()
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  if (phase.key === "areas") {
    modalStore.mainUrbanExists = await checkMainUrbanExists()
    if (!modalStore.mainUrbanExists) {
      modalStore.label = appStore.communeName
    } else {
      modalStore.label = ""
    }
    modalStore.areaTypeKey = modalStore.mainUrbanExists ? "secondary_urban" : "central_urban"
  }

  if (phase.key === "houseEntrances") {
    modalStore.roadOptions = (state.roads || []).map((r, i) => ({
      idx: i,
      label: r.data.label || `Road ${i + 1}`,
      dbId: String(r.dbId),
    }))
    modalStore.mainEntranceOptions = (state.houseEntrances || [])
      .filter((e: LayerEntry) => e.data.entranceTypeKey === "main_entrance")
      .map((e, i) => ({
        idx: i,
        label: e.data.label || `Entrance ${i + 1}`,
        dbId: String(e.dbId),
      }))
  }
}

let _roadSideToken = 0

export async function fetchRoadSide(
  roadDbId: string,
  geometry?: [number, number][] | null,
  signal?: AbortSignal,
): Promise<void> {
  const modalStore = useModalStore()
  modalStore.entranceSideLoading = true
  modalStore.entranceSide = null
  modalStore.entranceNumber = null

  const token = ++_roadSideToken

  let lat: number | undefined
  let lng: number | undefined

  if (geometry && geometry.length > 0) {
    ;[lng, lat] = geometry[geometry.length - 1]
  }

  if (lat && lng) {
    const result = await getRoadSide(roadDbId, lat, lng, signal)
    if (token !== _roadSideToken) return
    if (result) {
      modalStore.entranceSide = result.side
      modalStore.entranceNumber = result.suggestedNumber
    }
  }

  if (token !== _roadSideToken) return
  modalStore.entranceSideLoading = false
}

export function computeBisNumber(mainEntranceDbId: string): void {
  const layerStore = useLayerStore()
  const st = layerStore.$state
  const count = (st.houseEntrances || []).filter(
    (e: LayerEntry) =>
      e.data.entranceTypeKey === "secondary_entrance" &&
      e.data.mainEntranceDbId === mainEntranceDbId,
  ).length
  const modalStore = useModalStore()
  modalStore.bisNumber = count + 1
  modalStore.label = "BIS" + String(count + 1).padStart(2, "0")
}
