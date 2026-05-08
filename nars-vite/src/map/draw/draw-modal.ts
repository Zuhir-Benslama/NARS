// ─── DRAW MODAL HELPERS ───────────────────────────────────────────────────────
// Prepares and opens the feature modal for newly drawn shapes.
// Handles reference-driven house entrance logic and city center radius extraction.

import { PHASES } from "../../phases"
import { store, openModal } from "../../store"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { showToast } from "../../lib/toast"
import { getRoadSide } from "../../lib/validation"
import { t } from "../../i18n"
import type { ModalResult } from "../../types"
import { prepareModalExtras } from "../features/features"

export async function openModalForFeature(
  phase: (typeof PHASES)[number],
  featureId: string,
  geometry: GeoJSON.Geometry,
): Promise<ModalResult | null> {
  if (phase.key === "houseEntrances") {
    return openHouseEntranceModal(geometry)
  }

  await prepareModalExtras(phase)

  const radius =
    phase.key === "cityCenter" && geometry.type === "Point"
      ? ((geometry as GeoJSON.Point & { radius?: number }).radius ?? null)
      : null

  return openModal(phase.index, featureId, radius ? { radius } : undefined)
}

async function openHouseEntranceModal(geometry: GeoJSON.Geometry): Promise<ModalResult | null> {
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState

  if (store.referenceEntranceDbId != null) {
    return openSecondaryEntranceModal(state)
  }

  if (store.referenceRoadDbId != null) {
    return openMainEntranceModal(state, geometry)
  }

  showToast(t("alert_no_reference_set"), "error")
  return null
}

function openSecondaryEntranceModal(state: LayerState): ModalResult | null {
  const mainEntry = (state.houseEntrances || []).find((e) => e.dbId === store.referenceEntranceDbId)
  if (!mainEntry) {
    showToast(t("alert_ref_entrance_not_found"), "error")
    return null
  }

  const bisCount = (state.houseEntrances || []).filter(
    (e) =>
      e.data.entranceTypeKey === "secondary_entrance" &&
      e.data.mainEntranceDbId === store.referenceEntranceDbId,
  ).length
  const bisNumber = bisCount + 1

  return {
    label: "BIS" + String(bisNumber).padStart(2, "0"),
    decisionNumber: "",
    decisionDate: "",
    entranceTypeKey: "secondary_entrance",
    mainEntranceDbId: store.referenceEntranceDbId ?? undefined,
    mainEntranceLabel: mainEntry.data.label,
    bisNumber,
  }
}

async function openMainEntranceModal(
  state: LayerState,
  geometry: GeoJSON.Geometry,
): Promise<ModalResult | null> {
  const roadEntry = (state.roads || []).find((r) => r.dbId === store.referenceRoadDbId)
  if (!roadEntry) {
    showToast(t("alert_ref_road_not_found"), "error")
    return null
  }

  let side: "left" | "right" = "left"
  if (geometry.type === "Point" && store.referenceRoadDbId) {
    const lat = geometry.coordinates[1]
    const lng = geometry.coordinates[0]
    const sideResult = await getRoadSide(store.referenceRoadDbId, lat, lng)
    side = sideResult?.side ?? "left"
  }

  return {
    label: "?",
    decisionNumber: "",
    decisionDate: "",
    entranceTypeKey: "main_entrance",
    roadDbId: store.referenceRoadDbId ?? undefined,
    roadLabel: roadEntry.data.label,
    side,
    entranceNumber: undefined,
  }
}
