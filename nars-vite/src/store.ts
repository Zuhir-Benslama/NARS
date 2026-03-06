import { reactive } from 'vue'
import type * as L from 'leaflet'
import { PHASES }   from './phases'
import type { AppStore, LayerEntry, ModalResult } from './types'

// ─── NON-REACTIVE LAYER STORE ─────────────────────────────────────────────────
// Leaflet layers must NEVER enter Vue's reactive() proxy.

export const featureLayers: Record<string, LayerEntry[]> = {
    areas:               [],
    cityCenter:          [],
    districts:           [],
    roads:               [],
    mainEntrances:       [],
    secondaryEntrances:  [],
    publicBuildings:     [],
    publicSpaces:        [],
}

// The Leaflet layer currently being processed inside the open modal.
export let currentModalLayer: L.Layer | null = null

// ─── MODAL BRIDGE ─────────────────────────────────────────────────────────────

let _modalResolve: ((result: ModalResult | null) => void) | null = null

export function openModal(phaseIndex: number, layer: L.Layer): Promise<ModalResult | null> {
    PHASES[phaseIndex] // validate index (throws if out of bounds)

    return new Promise((resolve) => {
        _modalResolve     = resolve
        currentModalLayer = layer

        Object.assign(store.modal, {
            visible:             true,
            phaseIndex,
            isEdit:              false,
            editDbId:            null,
            label:               '',
            decisionNumber:      '',
            decisionDate:        '',
            errors:              {},
            areaTypeKey:         'central_urban',
            mainUrbanExists:     false,
            districtTypeKey:     'district',
            roadTypeKey:         'street',
            roadOptions:         [],
            selectedRoadIdx:     '',
            entranceSide:        null,
            entranceNumber:      null,
            entranceSideLoading: false,
            mainEntranceOptions: [],
            selectedMainIdx:     '',
            bisNumber:           null,
            spaceTypeKey:        'garden',
        })
    })
}

export function openEditModal(phaseIndex: number, dbId: number, existing: import('./types').FeatureData): Promise<ModalResult | null> {
    return new Promise((resolve) => {
        _modalResolve     = resolve
        currentModalLayer = null

        Object.assign(store.modal, {
            visible:         true,
            phaseIndex,
            isEdit:          true,
            editDbId:        dbId,
            label:           existing.label        ?? '',
            decisionNumber:  existing.decisionNumber ?? '',
            decisionDate:    existing.decisionDate   ?? '',
            errors:          {},
            areaTypeKey:     existing.areaTypeKey    ?? 'central_urban',
            mainUrbanExists: false,
            districtTypeKey: existing.districtTypeKey ?? 'district',
            roadTypeKey:     existing.roadTypeKey     ?? 'street',
            roadOptions:         [],
            selectedRoadIdx:     '',
            entranceSide:        null,
            entranceNumber:      null,
            entranceSideLoading: false,
            mainEntranceOptions: [],
            selectedMainIdx:     '',
            bisNumber:           null,
            spaceTypeKey:    existing.spaceTypeKey ?? 'garden',
        })
    })
}

export function resolveModal(result: ModalResult | null): void {
    store.modal.visible = false
    if (_modalResolve) {
        _modalResolve(result)
        _modalResolve = null
    }
}

// ─── REACTIVE STORE ───────────────────────────────────────────────────────────

export const store = reactive<AppStore>({
    currentPhase: 0,

    counts: {
        areas: 0, cityCenter: 0, districts: 0, roads: 0,
        mainEntrances: 0, secondaryEntrances: 0, publicBuildings: 0, publicSpaces: 0,
    },

    cityCenterMode:   null,
    cityCenterLatLng: null,

    user:             null,
    municipalityName: '',

    cityCenterDialogVisible: false,

    modal: {
        visible:             false,
        phaseIndex:          null,
        isEdit:              false,
        editDbId:            null,
        label:               '',
        decisionNumber:      '',
        decisionDate:        '',
        errors:              {},
        areaTypeKey:         'central_urban',
        mainUrbanExists:     false,
        districtTypeKey:     'district',
        roadTypeKey:         'street',
        roadOptions:         [],
        selectedRoadIdx:     '',
        entranceSide:        null,
        entranceNumber:      null,
        entranceSideLoading: false,
        mainEntranceOptions: [],
        selectedMainIdx:     '',
        bisNumber:           null,
        spaceTypeKey:        'garden',
    },
})

// ── Sync helper ───────────────────────────────────────────────────────────────

export function syncCounts(): void {
    for (const key of Object.keys(store.counts) as (keyof typeof store.counts)[])
        store.counts[key] = featureLayers[key]?.length ?? 0
}
