// ─── CONTEXT MENU (ORCHESTRATOR) ──────────────────────────────────────────────
// Shows context menus for features and map background.
// Re-exports actions for backward compatibility.

import { useAppStore } from "../../stores/appStore"
import { PHASES } from "../../phases"
import { t } from "../../i18n"
import { isSnappingEnabled } from "../draw/draw-complete"
import { toggleSnapping } from "../snapping/snapping"
import { showToast } from "../../lib/toast"
import { useLayerStore } from "../../stores/layerStore"
import { setHouseNumbers } from "../house-numbering"
import { setReferenceRoad, clearReferenceRoad, setReferenceEntrance } from "../house-entrances"
import { generateNamingPanels } from "../naming-panels"
import { computeAndApplyRoadDirections, updateEndpointMarkers } from "../roads/road-directions"
import { generateRoadsFromUrbanAreas } from "../generate/generate-roads"
import { generateDistrictsFromUrbanAreas } from "../generate/generate-districts"
import { useContextMenuStore, type CtxMenuItem } from "../../stores/contextMenuStore"
import { startDraftEdit } from "../drafts/draft-edit"
import { reviewDraft } from "../drafts/review-actions"
import {
  enableEditGeometry,
  editFeatureInfo,
  removeFeature,
  removeAllRoads,
  findLayerEntryByDbId,
} from "./ctx-menu-actions"

export {
  enableEditGeometry,
  editFeatureInfo,
  removeFeature,
  removeAllRoads,
  findLayerEntryByDbId,
  computeAndApplyRoadDirections,
  updateEndpointMarkers,
}

interface DrawContextEvent {
  originalEvent?: MouseEvent
  point: { x: number; y: number }
}

function buildSnapToggleItem(): CtxMenuItem {
  const snapOn = isSnappingEnabled()
  return {
    label: snapOn ? "\u2298 " + t("map_disable_snapping") : "\u229E " + t("map_enable_snapping"),
    onClick: () => {
      const e = toggleSnapping()
      showToast(t(e ? "map_snapping_enabled" : "map_snapping_disabled"), "info")
    },
  }
}

function buildFeatureMenuItems(dbId: string, phaseKey: string): CtxMenuItem[] {
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const currentPhase = PHASES[useAppStore().currentPhase]
  const currentPhaseKey = currentPhase?.key ?? ""
  const isRoad = phaseKey === "roads"
  const isRoadsPhase = currentPhaseKey === "roads"
  const isDistrictsPhase = currentPhaseKey === "districts"
  const isHouseEntrancesPhase = currentPhaseKey === "houseEntrances"
  const roadInHousePhase = isRoad && isHouseEntrancesPhase
  const isCurrentPhase = phaseKey === currentPhaseKey
  // Per-phase rule: a feature may only be edited/removed while its OWN phase is
  // the active one. Areas used to be exempt (`|| isArea`), which let the urban
  // area be destroyed from any phase — see alert_areas_uneditable_in_districts.
  // Roads are off-limits in the house entrances phase, and house entrances are
  // driven by their own reference-road/entrance flow rather than this menu.
  const canEdit = isCurrentPhase && !roadInHousePhase && phaseKey !== "houseEntrances"
  const isCityCenter = phaseKey === "cityCenter"
  // Genuine phase mismatch (as opposed to "this feature type has no edit path
  // here"): show the actions disabled with the reason, so the rule is
  // discoverable instead of the items silently missing.
  const phaseMismatch = !isCurrentPhase && !isCityCenter && phaseKey !== "houseEntrances"
  const phaseLabel = t(PHASES.find((p) => p.key === phaseKey)?.label ?? "")
  const isMainEntrance =
    phaseKey === "houseEntrances" &&
    (state.houseEntrances?.some(
      (e) => e.dbId === dbId && e.data.entranceTypeKey === "main_entrance",
    ) ??
      false)

  if (isCityCenter && currentPhaseKey !== "cityCenter") {
    return [
      {
        label: t("ctx_cc_lock"),
        onClick: () => showToast(t("ctx_cc_lock_msg"), "info"),
      },
    ]
  }

  const items: CtxMenuItem[] = []

  const blockedEdit = t("alert_switch_phase_to_edit", { phase: phaseLabel })
  const blockedRemove = t("alert_switch_phase_to_remove", { phase: phaseLabel })

  if (!isCityCenter) {
    if (canEdit) {
      items.push({ label: t("ctx_edit_geom"), onClick: () => enableEditGeometry(dbId) })
    } else if (phaseMismatch) {
      items.push({ label: t("ctx_edit_geom"), disabled: true, disabledReason: blockedEdit })
    }
  }
  if (canEdit) {
    items.push({
      label: t("ctx_edit_info"),
      onClick: () => editFeatureInfo(dbId),
    })
  } else if (phaseMismatch) {
    items.push({ label: t("ctx_edit_info"), disabled: true, disabledReason: blockedEdit })
  }
  if (canEdit) {
    items.push({
      label: t("ctx_remove"),
      danger: true,
      onClick: () => removeFeature(dbId),
    })
  } else if (phaseMismatch) {
    items.push({
      label: t("ctx_remove"),
      danger: true,
      disabled: true,
      disabledReason: blockedRemove,
    })
  }

  if (isRoad && isRoadsPhase) {
    items.push({
      label: t("ctx_road_dir"),
      onClick: () => computeAndApplyRoadDirections(),
    })
  }

  if (isRoadsPhase) {
    items.push({
      label: t("ctx_generate_roads"),
      onClick: () => void generateRoadsFromUrbanAreas(),
    })
    items.push({
      label: t("ctx_remove_all_roads"),
      danger: true,
      onClick: () => void removeAllRoads(),
    })
  }

  // Districts are cut from data the user has already mapped (urban areas +
  // boulevards/avenues), so the phase has its own action: it posts straight to
  // the standalone districts endpoint and never re-runs road segmentation.
  if (isDistrictsPhase) {
    items.push({
      label: t("ctx_generate_districts"),
      onClick: () => void generateDistrictsFromUrbanAreas(),
    })
  }

  const isCurrentRef = isRoad && dbId === useAppStore().referenceRoadDbId
  if (isRoad && isHouseEntrancesPhase && !isCurrentRef) {
    items.push({
      label: t("ctx_road_ref"),
      onClick: () => setReferenceRoad(dbId),
    })
  }
  if (isRoad && isHouseEntrancesPhase && isCurrentRef) {
    items.push({
      label: t("ctx_road_ref_remove"),
      onClick: () => clearReferenceRoad(),
    })
  }
  if (isMainEntrance && isHouseEntrancesPhase) {
    items.push({
      label: t("ctx_ent_ref"),
      onClick: () => setReferenceEntrance(dbId),
    })
  }

  items.push(buildSnapToggleItem())
  return items
}

export function showContextMenu(x: number, y: number, dbId: string, phaseKey: string): void {
  useContextMenuStore().show(x, y, buildFeatureMenuItems(dbId, phaseKey))
}

// ─── DRAFT CONTEXT MENU ───────────────────────────────────────────────────────
// Separate builder for AI draft hits (source "drafts"). Drafts are not phase
// features: editing reuses the geoman edit mode and commits through the draft
// endpoint, while accept/reject/delete act on the review queue.

export function showDraftContextMenu(
  x: number,
  y: number,
  draftId: string,
  featureType: string,
): void {
  const items: CtxMenuItem[] = []

  if (featureType === "road" || featureType === "building") {
    items.push({
      label: t("ctx_draft_edit_geom"),
      onClick: () => startDraftEdit(draftId),
    })
    items.push({
      label: t("ctx_draft_accept"),
      onClick: () => void reviewDraft(draftId, "accept"),
    })
    items.push({
      label: t("ctx_draft_reject"),
      danger: true,
      onClick: () => void reviewDraft(draftId, "reject"),
    })
    items.push({
      label: t("ctx_draft_delete"),
      danger: true,
      onClick: () => void reviewDraft(draftId, "delete"),
    })
  }

  items.push({ separator: true })
  items.push(buildSnapToggleItem())
  useContextMenuStore().show(x, y, items)
}

export function bindContextMenu(e: DrawContextEvent, dbId: string, phaseKey: string): void {
  showContextMenu(
    e.originalEvent?.clientX || e.point.x,
    e.originalEvent?.clientY || e.point.y,
    dbId,
    phaseKey,
  )
}

export async function showMapContextMenu(
  x: number,
  y: number,
  phase: (typeof PHASES)[number],
): Promise<void> {
  const items: CtxMenuItem[] = []

  if (phase.key === "roads") {
    items.push({
      label: t("ctx_generate_roads"),
      onClick: () => void generateRoadsFromUrbanAreas(),
    })
    items.push({
      label: t("ctx_road_dir"),
      onClick: () => computeAndApplyRoadDirections(),
    })
    items.push({
      label: t("ctx_remove_all_roads"),
      danger: true,
      onClick: () => void removeAllRoads(),
    })
  } else if (phase.key === "districts") {
    items.push({
      label: t("ctx_generate_districts"),
      onClick: () => void generateDistrictsFromUrbanAreas(),
    })
  } else if (phase.key === "houseEntrances") {
    items.push({
      label: t("ctx_house_nums"),
      onClick: () => setHouseNumbers(),
    })
  } else if (phase.key === "namingPanels") {
    items.push({
      label: t("ctx_set_naming_panels"),
      onClick: () => generateNamingPanels(),
    })
  }
  if (items.length > 0) {
    items.push({ separator: true })
  }
  items.push(buildSnapToggleItem())

  useContextMenuStore().show(x, y, items)
}
