// ─── DRAW MODAL HELPERS ───────────────────────────────────────────────────────
// Prepares and opens the feature modal for newly drawn shapes.
// Handles reference-driven house entrance logic and roundabouts radius extraction.

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { openModal } from "../../stores/modalStore"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { showToast } from "../../lib/toast"
import { getRoadSide } from "../../lib/validation"
import { roundaboutsPointFromGeometry, plainRoundaboutsPlacementError } from "../../lib/roundabouts"
import { t } from "../../i18n"
import type { ModalResult } from "../../types"
import { prepareModalExtras } from "../features/feature-modal"
export async function openModalForFeature(
  phase: (typeof PHASES)[number],
  featureId: string,
  geometry: GeoJSON.Geometry,
): Promise<ModalResult | null> {
  if (phase.key === "houseEntrances") {
    return openHouseEntranceModal(geometry)
  }

  const isRoundabouts = phase.key === "roundabouts"
  const state = useLayerStore().$state
  const radius =
    isRoundabouts && geometry.type === "Point"
      ? ((geometry as GeoJSON.Point & { radius?: number }).radius ?? null)
      : null

  // The geometry is already drawn when the modal opens. Only suppress the
  // plain option when an existing center occupies this urban area; placement
  // outside any urban area is still reported by the draw-time validation.
  const placementError = isRoundabouts
    ? plainRoundaboutsPlacementError(
        roundaboutsPointFromGeometry(geometry),
        state.areas ?? [],
        state.roundabouts ?? [],
      )
    : null
  const plainRoundaboutsUnavailable = placementError === "urban_area_has_plain_roundabouts"

  const extras = isRoundabouts
    ? { ...(radius ? { radius } : {}), plainRoundaboutsUnavailable }
    : undefined

  // Open modal first (initializes state via openCreate), then populate extras
  // so prepareModalExtras' values are not wiped by createDefaultModalState.
  const result = openModal(phase.index, featureId, extras)
  await prepareModalExtras(phase)
  return result
}

async function openHouseEntranceModal(geometry: GeoJSON.Geometry): Promise<ModalResult | null> {
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  if (appStore.referenceEntranceDbId != null) {
    return openSecondaryEntranceModal(state)
  }

  if (appStore.referenceRoadDbId != null) {
    return openMainEntranceModal(state, geometry)
  }

  showToast(t("alert_no_reference_set"), "error")
  return null
}

function openSecondaryEntranceModal(state: LayerState): ModalResult | null {
  const appStore = useAppStore()
  const mainEntry = (state.houseEntrances || []).find(
    (e) => e.dbId === appStore.referenceEntranceDbId,
  )
  if (!mainEntry) {
    showToast(t("alert_ref_entrance_not_found"), "error")
    return null
  }

  const bisCount = (state.houseEntrances || []).filter(
    (e) =>
      e.data.entranceTypeKey === "secondary_entrance" &&
      e.data.mainEntranceDbId === appStore.referenceEntranceDbId,
  ).length
  const bisNumber = bisCount + 1

  return {
    type: "houseEntrances",
    label: "BIS" + String(bisNumber).padStart(2, "0"),
    entranceTypeKey: "secondary_entrance",
    mainEntranceDbId: appStore.referenceEntranceDbId ?? undefined,
    mainEntranceLabel: mainEntry.data.label,
    bisNumber,
  }
}

async function openMainEntranceModal(
  state: LayerState,
  geometry: GeoJSON.Geometry,
): Promise<ModalResult | null> {
  const appStore = useAppStore()
  const roadEntry = (state.roads || []).find((r) => r.dbId === appStore.referenceRoadDbId)
  if (!roadEntry) {
    showToast(t("alert_ref_road_not_found"), "error")
    return null
  }

  let side: "left" | "right" = "left"
  if (geometry.type === "Point" && appStore.referenceRoadDbId) {
    const lat = geometry.coordinates[1]
    const lng = geometry.coordinates[0]
    const sideResult = await getRoadSide(appStore.referenceRoadDbId, lat, lng)
    side = sideResult?.side ?? "left"
  }

  return {
    type: "houseEntrances",
    label: "?",
    entranceTypeKey: "main_entrance",
    roadDbId: appStore.referenceRoadDbId ?? undefined,
    roadLabel: roadEntry.data.label,
    side,
    entranceNumber: undefined,
  }
}
