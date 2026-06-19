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
import type { LayerState } from "../../stores/layerStore"
import { setHouseNumbers } from "../house-numbering"
import { setReferenceRoad, clearReferenceRoad, setReferenceEntrance } from "../house-entrances"
import { generateNamingPanels } from "../naming-panels"
import { computeAndApplyRoadDirections, updateEndpointMarkers } from "../roads/road-directions"
import { useContextMenuStore, type CtxMenuItem } from "../../stores/contextMenuStore"
import {
  enableEditGeometry,
  editFeatureInfo,
  removeFeature,
  findLayerEntryByDbId,
} from "./ctx-menu-actions"

export {
  enableEditGeometry,
  editFeatureInfo,
  removeFeature,
  findLayerEntryByDbId,
  computeAndApplyRoadDirections,
  updateEndpointMarkers,
}

interface DrawContextEvent {
  originalEvent?: MouseEvent
  point: { x: number; y: number }
}

export function showContextMenu(x: number, y: number, dbId: string, phaseKey: string): void {
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState
  const currentPhase = PHASES[useAppStore().currentPhase]
  const currentPhaseKey = currentPhase?.key ?? ""
  const isRoad = phaseKey === "roads"
  const isRoadsPhase = currentPhaseKey === "roads"
  const isHouseEntrancesPhase = currentPhaseKey === "houseEntrances"
  const roadInHousePhase = isRoad && isHouseEntrancesPhase
  const isCurrentPhase = phaseKey === currentPhaseKey
  const isArea = phaseKey === "areas"
  const canEdit = (isCurrentPhase || isArea) && !roadInHousePhase && phaseKey !== "houseEntrances"
  const isCityCenter = phaseKey === "cityCenter"
  const isMainEntrance =
    phaseKey === "houseEntrances" &&
    (state.houseEntrances?.some(
      (e) => e.dbId === dbId && e.data.entranceTypeKey === "main_entrance",
    ) ??
      false)

  if (isCityCenter && currentPhaseKey !== "cityCenter") {
    useContextMenuStore().show(x, y, [
      {
        label: t("ctx_cc_lock"),
        onClick: () => showToast(t("ctx_cc_lock_msg"), "info"),
      },
    ])
    return
  }

  const items: CtxMenuItem[] = []

  if (canEdit && !isCityCenter) {
    items.push({
      label: t("ctx_edit_geom"),
      onClick: () => enableEditGeometry(dbId),
    })
  }
  if (canEdit) {
    items.push({
      label: t("ctx_edit_info"),
      onClick: () => editFeatureInfo(dbId),
    })
  }
  if (canEdit) {
    items.push({
      label: t("ctx_remove"),
      danger: true,
      onClick: () => removeFeature(dbId),
    })
  }

  if (isRoad && isRoadsPhase) {
    items.push({
      label: t("ctx_road_dir"),
      onClick: () => computeAndApplyRoadDirections(),
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

  const snapOn = isSnappingEnabled()
  items.push({
    label: snapOn ? "\u2298 Disable Snapping" : "\u229E Enable Snapping",
    onClick: () => {
      const e = toggleSnapping()
      showToast(`Snapping ${e ? "enabled" : "disabled"}`, "info")
    },
  })

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
      label: t("ctx_road_dir"),
      onClick: () => computeAndApplyRoadDirections(),
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
  const snapOn = isSnappingEnabled()
  items.push({
    label: snapOn ? "\u2298 Disable Snapping" : "\u229E Enable Snapping",
    onClick: () => {
      const e = toggleSnapping()
      showToast(`Snapping ${e ? "enabled" : "disabled"}`, "info")
    },
  })

  useContextMenuStore().show(x, y, items)
}
