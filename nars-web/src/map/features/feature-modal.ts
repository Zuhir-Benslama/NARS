import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { useLayerStore } from "../../stores/layerStore"
import { checkMainUrbanExists, getRoadSide } from "../../lib/validation"
export async function prepareModalExtras(phase: (typeof PHASES)[number]): Promise<void> {
  const modalStore = useModalStore()
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  if (phase.key === "areas") {
    const mainUrbanExists = await checkMainUrbanExists()
    modalStore.patchFields({
      mainUrbanExists,
      areaTypeKey: mainUrbanExists ? "secondary_urban" : "central_urban",
      label: mainUrbanExists ? "" : appStore.communeName,
    })
  }

  if (phase.key === "houseEntrances") {
    modalStore.setRoadOptions(
      (state.roads || []).map((r, i) => ({
        idx: i,
        label: r.data.label || `Road ${i + 1}`,
        dbId: String(r.dbId),
      })),
    )
    modalStore.setMainEntranceOptions(
      (state.houseEntrances || [])
        .filter((e) => e.data.entranceTypeKey === "main_entrance")
        .map((e, i) => ({
          idx: i,
          label: e.data.label || `Entrance ${i + 1}`,
          dbId: String(e.dbId),
        })),
    )
  }
}

export async function fetchRoadSide(
  roadDbId: string,
  geometry?: [number, number][] | null,
  signal?: AbortSignal,
): Promise<void> {
  const modalStore = useModalStore()
  modalStore.patchFields({ entranceSideLoading: true, entranceSide: null, entranceNumber: null })

  const token = ++modalStore.roadSideToken

  let lat: number | undefined
  let lng: number | undefined

  if (geometry && geometry.length > 0) {
    ;[lng, lat] = geometry[geometry.length - 1]
  }

  if (lat != null && lng != null) {
    const result = await getRoadSide(roadDbId, lat, lng, signal)
    if (token !== modalStore.roadSideToken) return
    if (result) {
      modalStore.patchFields({ entranceSide: result.side, entranceNumber: result.suggestedNumber })
    }
  }

  if (token !== modalStore.roadSideToken) return
  modalStore.patchFields({ entranceSideLoading: false })
}

export function computeBisNumber(mainEntranceDbId: string): void {
  const layerStore = useLayerStore()
  const st = layerStore.$state
  const count = (st.houseEntrances || []).filter(
    (e) =>
      e.data.entranceTypeKey === "secondary_entrance" &&
      e.data.mainEntranceDbId === mainEntranceDbId,
  ).length
  const modalStore = useModalStore()
  modalStore.patchFields({
    bisNumber: count + 1,
    label: "BIS" + String(count + 1).padStart(2, "0"),
  })
}
