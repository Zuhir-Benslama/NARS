// ─── MODAL STORE ──────────────────────────────────────────────────────────────
// Pinia store for the feature modal state.

import { defineStore } from "pinia"
import type { ModalState, RoadOption, EntranceOption, ModalResult, FeatureData } from "../types"
import { debugWarn } from "../utils/debug"

function createDefaultModalState(): ModalState {
  return {
    visible: false,
    phaseIndex: null,
    isEdit: false,
    editDbId: null,
    label: "",
    decisionNumber: "",
    decisionDate: "",
    errors: {},
    areaTypeKey: "central_urban",
    mainUrbanExists: false,
    districtTypeKey: "district",
    roadTypeKey: "street",
    entranceTypeKey: "main_entrance",
    roadOptions: [],
    selectedRoadIdx: "",
    entranceSide: null,
    entranceNumber: null,
    entranceSideLoading: false,
    mainEntranceOptions: [],
    selectedMainIdx: "",
    bisNumber: null,
    spaceTypeKey: "garden",
    sectorKey: "banking_postal",
    buildingTypeKey: "bank",
    radius: null,
    currentModalFeatureId: null,
  }
}

export const useModalStore = defineStore("modal", {
  state: (): ModalState & { roadSideToken: number } => ({
    ...createDefaultModalState(),
    roadSideToken: 0,
  }),

  getters: {
    isModalVisible: (state) => state.visible,
    isEditMode: (state) => state.isEdit,
  },

  actions: {
    /** Open the modal for creating a new feature in the given phase. */
    openCreate(phaseIndex: number, extras?: { radius?: number }) {
      this.$patch({
        ...createDefaultModalState(),
        visible: true,
        phaseIndex,
        isEdit: false,
        editDbId: null,
        radius: extras?.radius ?? null,
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
        decisionNumber: existing.decisionNumber ?? "",
        decisionDate: existing.decisionDate ?? "",
        areaTypeKey: existing.areaTypeKey ?? "central_urban",
        districtTypeKey: existing.districtTypeKey ?? "district",
        roadTypeKey: existing.roadTypeKey ?? "street",
        entranceTypeKey:
          existing.entranceTypeKey ??
          (existing.roadDbId != null ? "main_entrance" : "secondary_entrance"),
        entranceSide: (existing.side ?? null) as "left" | "right" | null,
        entranceNumber: existing.entranceNumber ?? null,
        bisNumber: existing.bisNumber ?? null,
        spaceTypeKey: existing.spaceTypeKey ?? "garden",
        sectorKey: existing.sectorKey ?? "banking_postal",
        buildingTypeKey: existing.buildingTypeKey ?? "bank",
        radius: existing.radius ?? null,
      })
    },

    /** Close the modal and optionally resolve with a result. */
    close(result: ModalResult | null = null): void {
      // Resolves the pending modal promise externally
      resolveModalPromise(result)
      this.visible = false
    },

    /** Reset all modal fields to defaults (without closing). */
    resetFields() {
      this.$patch(createDefaultModalState())
    },

    setRoadOptions(options: RoadOption[]) {
      this.roadOptions = options
      this.selectedRoadIdx = ""
    },

    setMainEntranceOptions(options: EntranceOption[]) {
      this.mainEntranceOptions = options
      this.selectedMainIdx = ""
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
  modalStore.currentModalFeatureId = null
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

// ─── LEGACY HELPER FUNCTIONS ───────────────────────────────────────────────────
// Moved from the legacy store proxy layer. These wrap modal store actions with
// awaitModalResult() for backward compatibility.

import { PHASES } from "../phases"
import { t } from "../i18n"

export function openModal(
  phaseIndex: number,
  featureId: string,
  extras?: { radius?: number },
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
  return awaitModalResult()
}

export function resolveModal(result: ModalResult | null): void {
  const modalStore = useModalStore()
  modalStore.close(result)
}
