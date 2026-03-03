import { reactive } from 'vue';
import { PHASES }   from './phases.js';

// ═════════════════════════════════════════════════════════════════════════════
// NON-REACTIVE LAYER STORE
// Leaflet layers must NEVER enter Vue's reactive() proxy — they have complex
// internal state that Vue cannot safely observe.  All Leaflet layer references
// live here; only primitive counts are synced into the reactive store.
// ═════════════════════════════════════════════════════════════════════════════

export const featureLayers = {
    areas:               [],   // [{ layer: L.Polygon, data: {...} }]
    cityCenter:          [],
    districts:           [],
    roads:               [],
    mainEntrances:       [],
    secondaryEntrances:  [],
    publicBuildings:     [],
    publicSpaces:        [],
};

// The Leaflet layer currently being processed inside the open modal.
// Kept as a plain module variable so Vue never proxies it.
export let currentModalLayer = null;

// ═════════════════════════════════════════════════════════════════════════════
// MODAL BRIDGE
// map.js calls openModal() which returns a Promise.
// The FeatureModal Vue component resolves it via resolveModal().
// ═════════════════════════════════════════════════════════════════════════════

let _modalResolve = null;

/**
 * Opens the feature-details modal for the given phase and Leaflet layer.
 * Returns a Promise that resolves with the filled-in form data, or null if cancelled.
 */
export function openModal(phaseIndex, layer) {
    const phase = PHASES[phaseIndex];

    return new Promise(resolve => {
        _modalResolve     = resolve;
        currentModalLayer = layer;

        // Reset the entire modal form state before showing
        Object.assign(store.modal, {
            visible:             true,
            phaseIndex,
            label:               '',
            decisionNumber:      '',
            decisionDate:        '',
            errors:              {},
            // Areas
            areaTypeKey:         'central_urban',
            mainUrbanExists:     false,
            // Districts
            districtTypeKey:     'district',
            // Roads
            roadTypeKey:         'street',
            // Main entrances
            roadOptions:         [],
            selectedRoadIdx:     '',
            entranceSide:        null,
            entranceNumber:      null,
            entranceSideLoading: false,
            // Secondary entrances
            mainEntranceOptions: [],
            selectedMainIdx:     '',
            bisNumber:           null,
            // Public spaces
            spaceTypeKey:        'garden',
        });
    });
}

/**
 * Called by FeatureModal on Save (result = form data object) or Cancel (result = null).
 */
export function resolveModal(result) {
    store.modal.visible = false;
    if (_modalResolve) { _modalResolve(result); _modalResolve = null; }
}

// ═════════════════════════════════════════════════════════════════════════════
// REACTIVE STORE
// Only primitive values and plain objects live here.
// ═════════════════════════════════════════════════════════════════════════════

export const store = reactive({

    // ── Phase navigation ──────────────────────────────────────────────────
    currentPhase: 0,

    // ── Feature counts (synced from featureLayers via syncCounts()) ───────
    counts: {
        areas: 0, cityCenter: 0, districts: 0, roads: 0,
        mainEntrances: 0, secondaryEntrances: 0, publicBuildings: 0, publicSpaces: 0,
    },

    // ── Numbering config ──────────────────────────────────────────────────
    cityCenterMode:   null,   // null | 'city_center' | 'auto'
    cityCenterLatLng: null,   // { lat, lng } — set when city center is placed

    // ── Loaded user + commune info ────────────────────────────────────────
    user:             null,
    municipalityName: '',

    // ── UI state ──────────────────────────────────────────────────────────
    cityCenterDialogVisible: false,

    // ── Modal form state ──────────────────────────────────────────────────
    modal: {
        visible:             false,
        phaseIndex:          null,      // index into PHASES[]

        // Base fields
        label:               '',
        decisionNumber:      '',
        decisionDate:        '',
        errors:              {},        // { fieldName: 'error message' }

        // Phase: areas
        areaTypeKey:         'central_urban',
        mainUrbanExists:     false,     // true = hide "Main Urban" option

        // Phase: districts
        districtTypeKey:     'district',

        // Phase: roads
        roadTypeKey:         'street',

        // Phase: mainEntrances
        roadOptions:         [],        // [{ label, dbId }]
        selectedRoadIdx:     '',        // index into roadOptions
        entranceSide:        null,      // 'left' | 'right'
        entranceNumber:      null,      // suggested number
        entranceSideLoading: false,

        // Phase: secondaryEntrances
        mainEntranceOptions: [],        // [{ label, dbId }]
        selectedMainIdx:     '',        // index into mainEntranceOptions
        bisNumber:           null,      // auto-suggested BIS number

        // Phase: publicSpaces
        spaceTypeKey:        'garden',
    },
});

// ── Sync helper ───────────────────────────────────────────────────────────────
/** Call after every feature add/remove to keep store.counts accurate. */
export function syncCounts() {
    for (const key of Object.keys(store.counts))
        store.counts[key] = featureLayers[key]?.length ?? 0;
}
