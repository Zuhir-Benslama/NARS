// ─── MODAL STORE ──────────────────────────────────────────────────────────────
// Pinia store for the feature modal state.

import { defineStore } from 'pinia'
import type { ModalState, RoadOption, EntranceOption, ModalResult, FeatureData } from '../types'

function createDefaultModalState(): ModalState {
    return {
        visible: false,
        phaseIndex: null,
        isEdit: false,
        editDbId: null,
        label: '',
        decisionNumber: '',
        decisionDate: '',
        errors: {},
        areaTypeKey: 'central_urban',
        mainUrbanExists: false,
        districtTypeKey: 'district',
        roadTypeKey: 'street',
        entranceTypeKey: 'main_entrance',
        roadOptions: [],
        selectedRoadIdx: '',
        entranceSide: null,
        entranceNumber: null,
        entranceSideLoading: false,
        mainEntranceOptions: [],
        selectedMainIdx: '',
        bisNumber: null,
        spaceTypeKey: 'garden',
        sectorKey: 'banking_postal',
        buildingTypeKey: 'bank',
        radius: null,
    }
}

export const useModalStore = defineStore('modal', {
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
                label: existing.label ?? '',
                decisionNumber: existing.decisionNumber ?? '',
                decisionDate: existing.decisionDate ?? '',
                areaTypeKey: existing.areaTypeKey ?? 'central_urban',
                districtTypeKey: existing.districtTypeKey ?? 'district',
                roadTypeKey: existing.roadTypeKey ?? 'street',
                entranceTypeKey:
                    existing.entranceTypeKey ?? (existing.roadDbId != null ? 'main_entrance' : 'secondary_entrance'),
                entranceSide: (existing.side ?? null) as 'left' | 'right' | null,
                entranceNumber: existing.entranceNumber ?? null,
                bisNumber: existing.bisNumber ?? null,
                spaceTypeKey: existing.spaceTypeKey ?? 'garden',
                sectorKey: existing.sectorKey ?? 'banking_postal',
                buildingTypeKey: existing.buildingTypeKey ?? 'bank',
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
            this.selectedRoadIdx = ''
        },

        setMainEntranceOptions(options: EntranceOption[]) {
            this.mainEntranceOptions = options
            this.selectedMainIdx = ''
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

/**
 * The feature ID currently open in the modal, exposed for non-reactive reads
 * (e.g. in draw-complete callbacks that fire outside the Vue reactivity system).
 * Intentionally a plain `let` — components that need reactive tracking should
 * read from the modal store's `editDbId` state instead.
 */
export let currentModalFeatureId: string | null = null

export function awaitModalResult(): Promise<ModalResult | null> {
    return new Promise((resolve) => {
        _modalQueue.push({ resolve })
    })
}

function resolveModalPromise(result: ModalResult | null): void {
    currentModalFeatureId = null
    // Drain all pending promises — prevents orphaned entries from stale modals
    while (_modalQueue.length > 0) {
        const pending = _modalQueue.shift()!
        pending.resolve(result)
    }
}

/** For use within the modal component — sets the current feature being edited. */
export function setCurrentModalFeatureId(id: string | null): void {
    currentModalFeatureId = id
}
