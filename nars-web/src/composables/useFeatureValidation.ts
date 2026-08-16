import { computed } from "vue"
import type { ModalState, ModalResult } from "../types"
import { PHASES } from "../phases"

export function useFeatureValidation(modalStore: ModalState & { phaseIndex: number | null }) {
  const phase = computed(() =>
    modalStore.phaseIndex !== null ? (PHASES[modalStore.phaseIndex] ?? null) : null,
  )

  const isCityCenter = computed(() => phase.value?.key === "cityCenter")

  const isMainUrban = computed(
    () => phase.value?.key === "areas" && modalStore.areaTypeKey === "central_urban",
  )

  function validate(): Record<string, string> {
    const errors: Record<string, string> = {}
    const key = phase.value?.key

    if (!isCityCenter.value) {
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

    return errors
  }

  function buildModalResult(communeName: string): ModalResult {
    const key = phase.value?.key

    if (key === "areas") {
      return {
        type: "areas",
        label: isMainUrban.value ? communeName : modalStore.label.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        areaTypeKey: modalStore.areaTypeKey,
      }
    }

    if (key === "districts") {
      return {
        type: "districts",
        label: modalStore.label.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        districtTypeKey: modalStore.districtTypeKey,
      }
    }

    if (key === "cityCenter") {
      return {
        type: "cityCenter",
        label: modalStore.label.trim(),
        radius: modalStore.radius ?? undefined,
      }
    }

    if (key === "roads") {
      return {
        type: "roads",
        label: modalStore.label.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        roadTypeKey: modalStore.roadTypeKey,
      }
    }

    if (key === "publicBuildings") {
      return {
        type: "publicBuildings",
        label: modalStore.label.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        sectorKey: modalStore.sectorKey,
        buildingTypeKey: modalStore.buildingTypeKey,
      }
    }

    if (key === "publicSpaces") {
      return {
        type: "publicSpaces",
        label: modalStore.label.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        spaceTypeKey: modalStore.spaceTypeKey,
      }
    }

    return {
      type: "namingPanels",
      label: modalStore.label.trim(),
      decisionNumber: modalStore.decisionNumber.trim(),
      decisionDate: modalStore.decisionDate.trim(),
    }
  }

  return { validate, buildModalResult, isMainUrban, isCityCenter }
}
