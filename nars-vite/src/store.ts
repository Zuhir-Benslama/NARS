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
    houseEntrances:      [],
    publicBuildings:     [],
    publicSpaces:        [],
    namingPanels:        [],
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
            label:               PHASES[phaseIndex]?.key === 'cityCenter' ? 'City Center' : '',
            decisionNumber:      '',
            decisionDate:        '',
            errors:              {},
            // areaTypeKey and mainUrbanExists are set by prepareModalExtras before
            // openModal is called — preserve them rather than resetting to defaults.
            areaTypeKey:         store.modal.mainUrbanExists ? 'secondary_urban' : 'central_urban',
            mainUrbanExists:     store.modal.mainUrbanExists,
            districtTypeKey:     'district',
            roadTypeKey:         'street',
            entranceTypeKey:     'main_entrance',
            roadOptions:         [],
            selectedRoadIdx:     '',
            entranceSide:        null,
            entranceNumber:      null,
            entranceSideLoading: false,
            mainEntranceOptions: [],
            selectedMainIdx:     '',
            bisNumber:           null,
            spaceTypeKey:        'garden',
            sectorKey:           'banking_postal',
            buildingTypeKey:     'bank',
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
            entranceTypeKey: existing.entranceTypeKey ?? (existing.roadDbId != null ? 'main_entrance' : 'secondary_entrance'),
            roadOptions:         [],
            selectedRoadIdx:     '',
            entranceSide:        (existing.side           ?? null) as 'left' | 'right' | null,
            entranceNumber:      existing.entranceNumber  ?? null,
            entranceSideLoading: false,
            mainEntranceOptions: [],
            selectedMainIdx:     '',
            bisNumber:           existing.bisNumber       ?? null,
            spaceTypeKey:    existing.spaceTypeKey ?? 'garden',
            sectorKey:       existing.sectorKey       ?? 'banking_postal',
            buildingTypeKey: existing.buildingTypeKey ?? 'bank',
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
    referenceRoadDbId:     null,
    referenceEntranceDbId: null,
    municipalityName: '',


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
        entranceTypeKey:     'main_entrance',
        roadOptions:         [],
        selectedRoadIdx:     '',
        entranceSide:        null,
        entranceNumber:      null,
        entranceSideLoading: false,
        mainEntranceOptions: [],
        selectedMainIdx:     '',
        bisNumber:           null,
        spaceTypeKey:        'garden',
        sectorKey:           'banking_postal',
        buildingTypeKey:     'bank',
    },
})

// ── Sync helper ───────────────────────────────────────────────────────────────

export function syncCounts(): void {
    store.counts.areas              = featureLayers.areas?.length             ?? 0
    store.counts.cityCenter         = featureLayers.cityCenter?.length        ?? 0
    store.counts.districts          = featureLayers.districts?.length         ?? 0
    store.counts.roads              = featureLayers.roads?.length             ?? 0
    store.counts.mainEntrances      = featureLayers.houseEntrances?.filter((e: LayerEntry) => e.data.entranceTypeKey === 'main_entrance').length      ?? 0
    store.counts.secondaryEntrances = featureLayers.houseEntrances?.filter((e: LayerEntry) => e.data.entranceTypeKey === 'secondary_entrance').length ?? 0
    store.counts.publicBuildings    = featureLayers.publicBuildings?.length   ?? 0
    store.counts.publicSpaces       = featureLayers.publicSpaces?.length      ?? 0
}
