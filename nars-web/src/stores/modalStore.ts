// ─── MODAL STORE ──────────────────────────────────────────────────────────────
// Pinia store for the feature modal state.

import { defineStore } from "pinia"
import type { ModalState, ModalResult, FeatureData } from "../types"
import { debugWarn } from "../utils/debug"

function createDefaultModalState(): ModalState {
  return {
    visible: false,
    phaseIndex: null,
    isEdit: false,
    editDbId: null,
    label: "",
    labelAr: "",
    labelFr: "",
    labelEn: "",
    decisionNumber: "",
    decisionDate: "",
    errors: {},
    areaTypeKey: "central_urban",
    mainUrbanExists: false,
    districtTypeKey: "district",
    roadTypeKey: "street",
    spaceTypeKey: "garden",
    sectorKey: "banking_postal",
    buildingTypeKey: "bank",
    radius: null,
    cityCenterTypeKey: "city_center",
    plainCityCenterUnavailable: false,
    numberingOutsideBandM: null,
    currentModalFeatureId: null,
  }
}

export const useModalStore = defineStore("modal", {
  state: (): ModalState => ({
    ...createDefaultModalState(),
  }),

  getters: {
    isModalVisible: (state) => state.visible,
    isEditMode: (state) => state.isEdit,
  },

  actions: {
    /** Open the modal for creating a new feature in the given phase. */
    openCreate(
      phaseIndex: number,
      extras?: { radius?: number; plainCityCenterUnavailable?: boolean },
    ) {
      const plainCityCenterUnavailable = extras?.plainCityCenterUnavailable ?? false
      this.$patch({
        ...createDefaultModalState(),
        visible: true,
        phaseIndex,
        isEdit: false,
        editDbId: null,
        radius: extras?.radius ?? null,
        plainCityCenterUnavailable,
        ...(plainCityCenterUnavailable ? { cityCenterTypeKey: "roundabout" } : {}),
      })
    },

    /** Open the modal for editing an existing feature. */
    openEdit(phaseIndex: number, dbId: string, existing: FeatureData) {
      this.$patch({
        ...createDefaultModalState(),
        visible: true,
        phaseIndex,
        isEdit: true,
        editDbId: dbId,
        label: existing.label ?? "",
        labelAr: existing.labelAr ?? "",
        labelFr: existing.labelFr ?? "",
        labelEn: existing.labelEn ?? "",
        decisionNumber: existing.decisionNumber ?? "",
        decisionDate: existing.decisionDate ?? "",
        areaTypeKey: existing.areaTypeKey ?? "central_urban",
        districtTypeKey: existing.districtTypeKey ?? "district",
        roadTypeKey: existing.roadTypeKey ?? "street",
        spaceTypeKey: existing.spaceTypeKey ?? "garden",
        sectorKey: existing.sectorKey ?? "banking_postal",
        buildingTypeKey: existing.buildingTypeKey ?? "bank",
        radius: existing.radius ?? null,
        cityCenterTypeKey:
          (existing as { cityCenterTypeKey?: string }).cityCenterTypeKey ?? "city_center",
        numberingOutsideBandM:
          (existing as { numberingOutsideBandM?: number }).numberingOutsideBandM ?? null,
      })
    },

    /** Close the modal and optionally resolve with a result. */
    close(result: ModalResult | null = null): void {
      // Resolves the pending modal promise externally
      resolveModalPromise(result)
      this.visible = false
    },

    patchFields(fields: Partial<ModalState>) {
      this.$patch(fields)
    },
  },
})

// ─── MODAL PROMISE BRIDGE ────────────────────────────────────────────────────
// Allows callers to await the modal result.
// Since the modal is a singleton, a single pending promise is sufficient.

let _modalResolver: ((result: ModalResult | null) => void) | null = null
let _modalResult: ModalResult | null = null

export function awaitModalResult(): Promise<ModalResult | null> {
  if (_modalResult !== null) {
    const result = _modalResult
    _modalResult = null
    return Promise.resolve(result)
  }
  if (_modalResolver !== null) {
    // Resolve the orphaned promise so callers don't hang forever
    debugWarn("[MODAL] awaitModalResult called while a modal is already pending")
    _modalResolver(null)
    _modalResolver = null
  }
  return new Promise((resolve) => {
    _modalResolver = resolve
  })
}

function resolveModalPromise(result: ModalResult | null): void {
  const modalStore = useModalStore()
  modalStore.$patch({ currentModalFeatureId: null })
  if (_modalResolver) {
    _modalResolver(result)
    _modalResolver = null
  } else {
    _modalResult = result
  }
}

/**
 * Reset the modal promise bridge state. Call during HMR disposal or test
 * cleanup to prevent stale resolvers from leaking across reloads.
 */
export function resetModalBridge(): void {
  if (_modalResolver) {
    _modalResolver(null)
    _modalResolver = null
  }
  _modalResult = null
}

/** Reset the modal store state (without touching the promise bridge). */
export function resetModalStore(): void {
  useModalStore().$reset()
}

// ─── LEGACY HELPER FUNCTIONS ───────────────────────────────────────────────────
// Moved from the legacy store proxy layer. These wrap modal store actions with
// awaitModalResult() for backward compatibility.

import { PHASES } from "../phases"
import { t } from "../i18n"
import { plainCityCenterPlacementError } from "../lib/city-center"
import { useLayerStore } from "./layerStore"

export function openModal(
  phaseIndex: number,
  featureId: string,
  extras?: { radius?: number; plainCityCenterUnavailable?: boolean },
): Promise<ModalResult | null> {
  const modalStore = useModalStore()
  modalStore.openCreate(phaseIndex, extras)
  // Set after openCreate (which resets state via createDefaultModalState)
  modalStore.currentModalFeatureId = featureId
  const phase = PHASES[phaseIndex]
  if (phase?.key === "cityCenter") {
    modalStore.label = t("phase_cityCenter_label")
  }
  return awaitModalResult()
}

export function openEditModal(
  phaseIndex: number,
  dbId: string,
  existing: FeatureData,
): Promise<ModalResult | null> {
  const modalStore = useModalStore()
  modalStore.openEdit(phaseIndex, dbId, existing)
  const phase = PHASES[phaseIndex]
  if (
    phase?.key === "cityCenter" &&
    (existing as { cityCenterTypeKey?: string }).cityCenterTypeKey === "roundabout"
  ) {
    // A roundabout can only become a plain city center if the urban area's
    // slot is still free; reflect that in the subtype dropdown.
    const state = useLayerStore().$state
    const unavailable =
      plainCityCenterPlacementError(
        { lat: (existing as { lat?: number }).lat, lng: (existing as { lng?: number }).lng },
        state.areas ?? [],
        state.cityCenter ?? [],
        dbId,
      ) === "urban_area_has_city_center"
    modalStore.patchFields({ plainCityCenterUnavailable: unavailable })
  }
  return awaitModalResult()
}

export function resolveModal(result: ModalResult | null): void {
  const modalStore = useModalStore()
  modalStore.close(result)
}
