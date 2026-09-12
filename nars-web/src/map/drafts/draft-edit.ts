// ─── DRAFT EDIT ───────────────────────────────────────────────────────────────
// Geoman geometry editing for pending AI drafts. Reuses the shared edit-mode
// plumbing (global edit mode, edit store, snapping, save/cancel button) while
// committing the change to /api/draft-features/{id} instead of /api/features.
//
// The active draft being edited is tracked in the drafts store
// (activeDraftEditId); edit-commit.commitEditMode dispatches here when it is
// set. Escape and the edit-mode context menu keep routing through
// cancelEditMode, which also dispatches here.

import { useDraftsStore } from "../../stores/draftsStore"
import type { GeoJsonImportFeature } from "@geoman-io/maplibre-geoman-free"
import { getCtx } from "../core/state"
import { ensureGeoman } from "../map-init"
import { useEditStore } from "../../stores/editStore"
import { getUserMessageKey } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import { t } from "../../i18n"
import { debugError, debugWarn } from "../../utils/debug"
import { updateDraftGeometry } from "../../api/drafts"
import { setActiveGeomanFeatureId, disableEditMode } from "../edit/edit-state"
import {
  disableCrosshair,
  disableSnapping,
  setSnapExclude,
  setEditModeActive,
} from "../snapping/snapping"
import { patchMarkerPointerSnap } from "../edit/edit-snap"

function draftGeometry(
  draftId: string,
): { type: "LineString" | "Polygon"; coordinates: unknown } | null {
  const draftsStore = useDraftsStore()
  const draft = draftsStore.drafts.find((d) => d.id === draftId)
  if (!draft) return null
  try {
    return JSON.parse(draft.geometryGeoJson) as {
      type: "LineString" | "Polygon"
      coordinates: unknown
    }
  } catch {
    return null
  }
}

export async function startDraftEdit(draftId: string): Promise<void> {
  await ensureGeoman()
  const { geoman } = getCtx()
  if (!geoman) return

  const geometry = draftGeometry(draftId)
  if (!geometry || (geometry.type !== "LineString" && geometry.type !== "Polygon")) {
    showToast(t("map_draft_edit_unavailable"), "error")
    return
  }

  const feature: GeoJSON.Feature = {
    type: "Feature",
    geometry: geometry as GeoJSON.Geometry,
    properties: { shape: geometry.type === "LineString" ? "line" : "polygon", draftId },
  }

  try {
    const result = await geoman.features.importGeoJson(feature as unknown as GeoJsonImportFeature, {
      overwrite: true,
    })
    const added = (result as { addedFeatures?: Array<{ id?: string }> } | undefined)
      ?.addedFeatures?.[0]
    setActiveGeomanFeatureId(added?.id ?? null)
  } catch (err) {
    debugError("[DRAFTS] importGeoJson failed:", err)
    return
  }

  const draftsStore = useDraftsStore()
  draftsStore.setActiveDraftEditId(draftId)
  draftsStore.setSelectedDraftId(draftId)

  disableCrosshair()
  disableSnapping()
  await geoman.enableGlobalEditMode()
  setEditModeActive(true)
  useEditStore().setIsEditMode(true)
  setSnapExclude(null)
  patchMarkerPointerSnap(null)
  showToast(t("map_edit_mode_hint"), "info")
}

async function readEditedGeometry(): Promise<GeoJSON.Geometry | null> {
  const editStore = useEditStore()
  const geomanId = editStore.activeGeomanFeatureId
  const { geoman } = getCtx()
  if (!geoman || !geomanId) return null
  try {
    const all = (await geoman.features.getAll()) as {
      features?: Array<{ id?: string; geometry?: unknown }>
    }
    const f = all.features?.find((x) => x.id === geomanId)
    const geometry = f?.geometry
    if (!geometry || typeof geometry !== "object" || !("type" in geometry)) return null
    return geometry as GeoJSON.Geometry
  } catch (err) {
    debugWarn("[DRAFTS] Failed to read edited geometry:", err)
    return null
  }
}

function normalizeEditedGeometry(
  geometry: GeoJSON.Geometry,
  expected: "LineString" | "Polygon",
): string | null {
  if (geometry.type === expected) return JSON.stringify(geometry)
  // Geoman can report a LineString for a ring while a Polygon is expected.
  if (expected === "Polygon" && geometry.type === "LineString") {
    const coords = geometry.coordinates as [number, number][]
    if (coords.length < 3) return null
    const ring =
      coords[0][0] === coords[coords.length - 1][0] && coords[0][1] === coords[coords.length - 1][1]
        ? coords
        : [...coords, coords[0]]
    return JSON.stringify({ type: "Polygon", coordinates: [ring] })
  }
  return null
}

async function removeGeomanFeature(): Promise<void> {
  const geomanId = useEditStore().activeGeomanFeatureId
  const { geoman } = getCtx()
  if (!geoman || !geomanId) return
  try {
    await geoman.features.delete(geomanId)
  } catch {
    // Feature may already be gone
  }
  setActiveGeomanFeatureId(null)
}

export async function commitDraftEdit(): Promise<void> {
  const draftsStore = useDraftsStore()
  const draftId = draftsStore.activeDraftEditId
  if (!draftId) return

  const draft = draftsStore.drafts.find((d) => d.id === draftId)
  if (!draft) {
    disableDraftEdit()
    return
  }
  const expected = draft.featureType === "road" ? "LineString" : "Polygon"

  const geometry = await readEditedGeometry()
  const geometryGeoJson = geometry ? normalizeEditedGeometry(geometry, expected) : null
  if (!geometryGeoJson) {
    showToast(t("map_geometry_save_failed"), "error")
    await cancelDraftEdit()
    return
  }

  try {
    const res = await updateDraftGeometry(draftId, geometryGeoJson)
    if (!res.ok) {
      mapDraftEditError(res.status)
      await cancelDraftEdit()
      return
    }
    draftsStore.replaceDraftGeometry(draftId, geometryGeoJson)
    showToast(t("map_geometry_saved"), "success")
    await removeGeomanFeature()
    disableDraftEdit()
  } catch (err) {
    debugError("[DRAFTS] Commit edit failed:", err)
    showToast(t("map_save_failed", { error: t(getUserMessageKey(err)) }), "error")
  }
}

export async function cancelDraftEdit(): Promise<void> {
  await removeGeomanFeature()
  disableDraftEdit()
  showToast(t("map_edit_cancelled"), "info")
}

function disableDraftEdit(): void {
  const draftsStore = useDraftsStore()
  draftsStore.setActiveDraftEditId(null)
  disableEditMode()
}

function mapDraftEditError(status: number): void {
  switch (status) {
    case 422:
      showToast(t("map_draft_rules_not_met"), "error")
      break
    case 400:
      showToast(t("map_draft_invalid_geometry"), "error")
      break
    case 409:
      showToast(t("map_draft_not_pending"), "error")
      break
    case 404:
      showToast(t("map_draft_not_found"), "error")
      break
    default:
      showToast(t("map_geometry_save_failed"), "error")
  }
}
