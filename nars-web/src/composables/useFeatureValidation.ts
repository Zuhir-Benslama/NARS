import { computed } from "vue"
import type { ModalState, ModalResult } from "../types"
import { PHASES } from "../phases"
import { CITY_CENTER_CONFIG } from "../config"
import { cityCenterRadiusError } from "../lib/city-center"

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
      const hasName = [
        modalStore.label,
        modalStore.labelAr,
        modalStore.labelFr,
        modalStore.labelEn,
      ].some((v) => (v ?? "").trim() !== "")
      if (labelRequired && !hasName) errors.label = "Required"
      // Roads can be AI-generated, which carries no administrative decision —
      // the backend materializes them with blank decision fields
      // (RoadGenerationService/DraftGeometry.ToRoadData). Requiring them made
      // every generated road unsaveable, so they stay optional for roads only.
      if (key !== "roads") {
        if (!modalStore.decisionNumber.trim()) errors.decisionNumber = "Required"
        if (!modalStore.decisionDate.trim()) errors.decisionDate = "Required"
      }
    }

    if (key === "cityCenter") {
      // Shared rule with draw-save's toast validation (lib/city-center.ts).
      const radiusError = cityCenterRadiusError(modalStore.radius)
      if (radiusError === "too_small") {
        errors.radius = `Must be at least ${CITY_CENTER_CONFIG.minRadiusM} meters`
      } else if (radiusError === "too_large") {
        errors.radius = `Must not exceed ${CITY_CENTER_CONFIG.maxRadiusM / 1000} km`
      }
    }

    return errors
  }

  function buildModalResult(_communeName: string): ModalResult {
    const key = phase.value?.key

    if (key === "areas") {
      return {
        type: "areas",
        label: (
          modalStore.labelEn ||
          modalStore.labelFr ||
          modalStore.labelAr ||
          modalStore.label ||
          ""
        ).trim(),
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        areaTypeKey: modalStore.areaTypeKey,
      }
    }

    if (key === "districts") {
      return {
        type: "districts",
        label: (
          modalStore.labelEn ||
          modalStore.labelFr ||
          modalStore.labelAr ||
          modalStore.label ||
          ""
        ).trim(),
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        districtTypeKey: modalStore.districtTypeKey,
      }
    }

    if (key === "cityCenter") {
      return {
        type: "cityCenter",
        label: "",
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        radius: modalStore.radius ?? undefined,
      }
    }

    if (key === "roads") {
      return {
        type: "roads",
        label: (
          modalStore.labelEn ||
          modalStore.labelFr ||
          modalStore.labelAr ||
          modalStore.label ||
          ""
        ).trim(),
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        roadTypeKey: modalStore.roadTypeKey,
      }
    }

    if (key === "publicBuildings") {
      return {
        type: "publicBuildings",
        label: (
          modalStore.labelEn ||
          modalStore.labelFr ||
          modalStore.labelAr ||
          modalStore.label ||
          ""
        ).trim(),
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        sectorKey: modalStore.sectorKey,
        buildingTypeKey: modalStore.buildingTypeKey,
      }
    }

    if (key === "publicSpaces") {
      return {
        type: "publicSpaces",
        label: (
          modalStore.labelEn ||
          modalStore.labelFr ||
          modalStore.labelAr ||
          modalStore.label ||
          ""
        ).trim(),
        labelAr:
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelFr:
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        labelEn:
          (modalStore.labelEn ?? "").trim() ||
          (modalStore.labelFr ?? "").trim() ||
          (modalStore.labelAr ?? "").trim() ||
          (modalStore.label ?? "").trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        spaceTypeKey: modalStore.spaceTypeKey,
      }
    }

    return {
      type: "namingPanels",
      label: (
        modalStore.label ||
        modalStore.labelEn ||
        modalStore.labelFr ||
        modalStore.labelAr ||
        ""
      ).trim(),
      labelAr: (modalStore.labelAr ?? "").trim(),
      labelFr: (modalStore.labelFr ?? "").trim(),
      labelEn: (modalStore.labelEn ?? "").trim(),
      decisionNumber: modalStore.decisionNumber.trim(),
      decisionDate: modalStore.decisionDate.trim(),
    }
  }

  return { validate, buildModalResult, isMainUrban, isCityCenter }
}
