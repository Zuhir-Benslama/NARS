// ─── MODAL STORE ──────────────────────────────────────────────────────────────
// Pinia store for the feature modal state.

import { defineStore } from "pinia"
import type { ModalState, RoadOption, EntranceOption, ModalResult, FeatureData } from "../types"

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
  state: (): ModalState => createDefaultModalState(),

  getters: {
    isModalVisible: (state) => state.visible,
    isEditMode: (state) => state.isEdit,
  },

  actions: {
    /** Open the modal for creating a new feature in the given phase. */
    openCreate(phaseIndex: number, extras?: { radius?: number }) {
      Object.assign(this, {
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
      Object.assign(this, {
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
      // Clear any stale queued promises (prevents orphaned entries)
      _modalQueue.length = 0
      this.visible = false
    },

    /** Reset all modal fields to defaults (without closing). */
    resetFields() {
      const defaults = createDefaultModalState()
      Object.assign(this, defaults)
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
// Uses a queue to prevent race conditions when multiple modals are opened
// in rapid succession — each caller gets its own Promise.

const _modalQueue: Array<{
  resolve: (result: ModalResult | null) => void
}> = []

export function awaitModalResult(): Promise<ModalResult | null> {
  return new Promise((resolve) => {
    _modalQueue.push({ resolve })
  })
}

function resolveModalPromise(result: ModalResult | null): void {
  const modalStore = useModalStore()
  modalStore.currentModalFeatureId = null
  // Drain all pending promises — prevents orphaned entries from stale modals
  while (_modalQueue.length > 0) {
    const pending = _modalQueue.shift()!
    pending.resolve(result)
  }
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
