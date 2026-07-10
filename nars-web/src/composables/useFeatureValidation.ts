import { computed } from "vue"
import type { ModalState } from "../types"
import type { FeatureData } from "../types"
import { PHASES } from "../phases"

export function useFeatureValidation(modalStore: ModalState & { phaseIndex: number | null }) {
  const phase = computed(() =>
    modalStore.phaseIndex !== null ? (PHASES[modalStore.phaseIndex] ?? null) : null,
  )

  const isHouseEntranceEdit = computed(
    () => phase.value?.key === "houseEntrances" && modalStore.isEdit,
  )

  const isCityCenter = computed(() => phase.value?.key === "cityCenter")

  const isMainUrban = computed(
    () => phase.value?.key === "areas" && modalStore.areaTypeKey === "central_urban",
  )

  function validate(): Record<string, string> {
    const errors: Record<string, string> = {}
    const key = phase.value?.key

    if (!isHouseEntranceEdit.value && !isCityCenter.value) {
      const labelRequired =
        !(
          key === "districts" &&
          (modalStore.districtTypeKey === "trad_activities_zone" ||
            modalStore.districtTypeKey === "industry_zone")
        ) && !(key === "areas" && modalStore.areaTypeKey === "central_urban")
      if (labelRequired && !modalStore.label.trim()) errors.label = "Required"
      if (!modalStore.decisionNumber.trim()) errors.decisionNumber = "Required"
      if (!modalStore.decisionDate.trim()) errors.decisionDate = "Required"
    }

    if (key === "cityCenter") {
      const radius = modalStore.radius
      if (!radius || Number.isNaN(radius) || radius < 5) {
        errors.radius = "Must be at least 5 meters"
      } else if (radius > 50000) {
        errors.radius = "Must not exceed 50 km"
      }
    }

    if (!modalStore.isEdit) {
      if (
        key === "houseEntrances" &&
        modalStore.entranceTypeKey === "main_entrance" &&
        modalStore.selectedRoadIdx === ""
      )
        errors.road = "Required"
      if (
        key === "houseEntrances" &&
        modalStore.entranceTypeKey === "secondary_entrance" &&
        modalStore.selectedMainIdx === ""
      )
        errors.mainEntrance = "Required"
    }

    return errors
  }

  function buildModalResult(communeName: string): Partial<FeatureData> {
    const key = phase.value?.key
    const result: Partial<FeatureData> = {
      label: isMainUrban.value ? communeName : modalStore.label.trim(),
      decisionNumber: modalStore.decisionNumber.trim(),
      decisionDate: modalStore.decisionDate.trim(),
    }

    if (key === "areas") {
      result.areaTypeKey = modalStore.areaTypeKey
    } else if (key === "districts") {
      result.districtTypeKey = modalStore.districtTypeKey
    } else if (key === "roads") {
      result.roadTypeKey = modalStore.roadTypeKey
    } else if (key === "houseEntrances") {
      result.entranceTypeKey = modalStore.entranceTypeKey
      if (modalStore.entranceTypeKey === "main_entrance") {
        const roadOption = modalStore.roadOptions[Number(modalStore.selectedRoadIdx)]
        result.roadDbId = roadOption?.dbId
        result.roadLabel = roadOption?.label
        result.side = modalStore.entranceSide ?? undefined
        result.entranceNumber = modalStore.entranceNumber ?? undefined
      } else {
        const mainOption = modalStore.mainEntranceOptions[Number(modalStore.selectedMainIdx)]
        result.mainEntranceDbId = mainOption?.dbId
        result.mainEntranceLabel = mainOption?.label
        result.bisNumber = modalStore.bisNumber ?? undefined
      }
    } else if (key === "publicBuildings") {
      result.sectorKey = modalStore.sectorKey
      result.buildingTypeKey = modalStore.buildingTypeKey
    } else if (key === "publicSpaces") {
      result.spaceTypeKey = modalStore.spaceTypeKey
    } else if (key === "cityCenter") {
      result.radius = modalStore.radius ?? undefined
    }

    return result
  }

  return { validate, buildModalResult, isMainUrban, isCityCenter, isHouseEntranceEdit }
}
