import { computed } from "vue"
import type { ModalState, ModalResult } from "../types"
import { PHASES } from "../phases"
import { CITY_CENTER_CONFIG } from "../config"
import { cityCenterRadiusError } from "../lib/city-center"
import { t } from "../i18n"
import {
  AREA_TYPES,
  DISTRICT_TYPES,
  ROAD_TYPES,
  PUBLIC_SPACE_TYPES,
  PUBLIC_BUILDING_SECTORS,
} from "../types/feature-types"

function typeLabelFor(key: string | undefined, fallback: string): string {
  if (!key) return fallback
  const resolved = t(`featureTypes.${key}`)
  if (resolved && resolved !== `featureTypes.${key}`) return resolved
  // Fallback to the static English catalog label (or the key itself) when i18n
  // has not resolved the key (e.g. tests / untranslated locales).
  const allTypes = [
    ...AREA_TYPES,
    ...DISTRICT_TYPES,
    ...ROAD_TYPES,
    ...PUBLIC_SPACE_TYPES,
    ...PUBLIC_BUILDING_SECTORS.flatMap((s) => s.buildings),
  ]
  return allTypes.find((x) => x.key === key)?.label ?? fallback
}

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

  function buildModalResult(communeName: string): ModalResult {
    const key = phase.value?.key

    if (key === "areas") {
      const typeLabel = typeLabelFor(modalStore.areaTypeKey, t("phase_areas_label"))
      return {
        type: "areas",
        label: isMainUrban.value
          ? `${typeLabel} ${communeName}`.trim()
          : `${typeLabel} ${modalStore.label.trim()}`.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        areaTypeKey: modalStore.areaTypeKey,
      }
    }

    if (key === "districts") {
      const typeLabel = typeLabelFor(modalStore.districtTypeKey, t("phase_districts_label"))
      return {
        type: "districts",
        label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        districtTypeKey: modalStore.districtTypeKey,
      }
    }

    if (key === "cityCenter") {
      const typeLabel = t("phase_cityCenter_label")
      return {
        type: "cityCenter",
        label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
        radius: modalStore.radius ?? undefined,
      }
    }

    if (key === "roads") {
      const typeLabel = typeLabelFor(modalStore.roadTypeKey, t("phase_roads_label"))
      return {
        type: "roads",
        label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        roadTypeKey: modalStore.roadTypeKey,
      }
    }

    if (key === "publicBuildings") {
      const typeLabel = typeLabelFor(modalStore.buildingTypeKey, t("phase_publicBuildings_label"))
      return {
        type: "publicBuildings",
        label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        sectorKey: modalStore.sectorKey,
        buildingTypeKey: modalStore.buildingTypeKey,
      }
    }

    if (key === "publicSpaces") {
      const typeLabel = typeLabelFor(modalStore.spaceTypeKey, t("phase_publicSpaces_label"))
      return {
        type: "publicSpaces",
        label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
        decisionNumber: modalStore.decisionNumber.trim(),
        decisionDate: modalStore.decisionDate.trim(),
        spaceTypeKey: modalStore.spaceTypeKey,
      }
    }

    const typeLabel = t("phase_namingPanels_label")
    return {
      type: "namingPanels",
      label: `${typeLabel} ${modalStore.label.trim()}`.trim(),
      decisionNumber: modalStore.decisionNumber.trim(),
      decisionDate: modalStore.decisionDate.trim(),
    }
  }

  return { validate, buildModalResult, isMainUrban, isCityCenter }
}
