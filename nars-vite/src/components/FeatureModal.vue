<template>
    <div v-show="m.visible" class="modal" @keyup="onKeyup">
        <div class="modal-content">

            <div class="modal-header">{{ headerText }}</div>

            <!-- Hint bar -->
            <div id="modalHint" v-if="phase?.hint">{{ t(phase.hint) }}</div>

            <!-- Name — hidden when editing a house entrance (set at creation, not editable) -->
            <div v-if="!isHouseEntranceEdit" class="modal-field">
                <label>Name <span class="req">*</span></label>
                <input
                    type="text"
                    v-model="m.label"
                    :class="['modal-input', { error: m.errors.label, 'modal-input-readonly': isMainUrban || isZoneWithTypeName || isCityCenter }]"
                    :placeholder="isMainUrban || isZoneWithTypeName || isCityCenter ? '' : 'Feature name...'"
                    :readonly="isMainUrban || isZoneWithTypeName || isCityCenter"
                    :disabled="isMainUrban"
                    autocomplete="off"
                    autofocus
                />
                <span v-if="isMainUrban" class="modal-field-note">
                    The main urban area takes the municipality name.
                </span>
                <span v-if="isZoneWithTypeName" class="modal-field-note">
                    This zone uses its type name.
                </span>
                <span v-if="isCityCenter" class="modal-field-note">
                    The city center is always named "City Center".
                </span>
            </div>

            <!-- Decision No. + Date — hidden when editing a house entrance -->
            <div v-if="!isHouseEntranceEdit" class="modal-row">
                <div class="modal-field">
                    <label>Decision No. <span class="req">*</span></label>
                    <input
                        type="text"
                        v-model="m.decisionNumber"
                        :class="['modal-input', { error: m.errors.decisionNumber }]"
                        placeholder="e.g. 2024/001"
                        autocomplete="off"
                    />
                </div>
                <div class="modal-field">
                    <label>Decision Date <span class="req">*</span></label>
                    <input
                        type="date"
                        v-model="m.decisionDate"
                        :class="['modal-input', { error: m.errors.decisionDate }]"
                    />
                </div>
            </div>

            <!-- ── Phase-specific extras ────────────────────────────────── -->

            <!-- Areas: area type selector -->
            <div v-if="phase?.key === 'areas'" class="modal-field">
                <label>Area Type <span class="req">*</span></label>
                <select v-model="m.areaTypeKey" class="modal-input">
                    <option v-for="a in areaTypeOptions" :key="a.key" :value="a.key">{{ a.label }}</option>
                </select>
            </div>

            <!-- Districts: district type selector -->
            <div v-if="phase?.key === 'districts'" class="modal-field">
                <label>District Type <span class="req">*</span></label>
                <select v-model="m.districtTypeKey" class="modal-input">
                    <option v-for="d in DISTRICT_TYPES" :key="d.key" :value="d.key">{{ d.label }}</option>
                </select>
            </div>

            <!-- Roads: road type selector -->
            <div v-if="phase?.key === 'roads'" class="modal-field">
                <label>Road Type <span class="req">*</span></label>
                <select v-model="m.roadTypeKey" class="modal-input">
                    <option v-for="r in ROAD_TYPES" :key="r.key" :value="r.key">{{ r.label }}</option>
                </select>
            </div>

            <!-- House Entrances: sub-type selector + conditional fields -->
            <template v-if="phase?.key === 'houseEntrances'">

                <!-- Entrance type selector -->
                <div class="modal-field">
                    <label>Entrance Type <span class="req">*</span></label>
                    <select v-model="m.entranceTypeKey" class="modal-input">
                        <option value="main_entrance">Main Entrance</option>
                        <option value="secondary_entrance">Secondary Entrance</option>
                    </select>
                </div>

                <!-- Main entrance: road selector + side detection.
                     Road assignment is fixed at creation — not editable afterwards. -->
                <template v-if="m.entranceTypeKey === 'main_entrance'">
                    <div v-if="!m.isEdit" class="modal-field">
                        <label>Assign to Road <span class="req">*</span></label>
                        <select
                            v-model="m.selectedRoadIdx"
                            :class="['modal-input', { error: m.errors.road }]"
                        >
                            <option value="">— Select a road —</option>
                            <option v-for="r in m.roadOptions" :key="r.idx" :value="r.idx">{{ r.label }}</option>
                        </select>
                    </div>
                    <div v-if="m.selectedRoadIdx !== ''" class="modal-field">
                        <label>
                            Entrance Number
                            <span v-if="sideText" class="modal-side-hint"> — {{ sideText }}</span>
                        </label>
                        <div class="modal-input-row">
                            <input
                                type="number"
                                v-model.number="m.entranceNumber"
                                class="modal-input modal-input-narrow"
                                min="1"
                            />
                            <span v-if="m.entranceSideLoading" class="field-spinner"></span>
                        </div>
                    </div>
                </template>

                <!-- Secondary entrance: main entrance selector + BIS preview -->
                <template v-if="m.entranceTypeKey === 'secondary_entrance'">
                    <div class="modal-field">
                        <label>Assign to Main Entrance <span class="req">*</span></label>
                        <select
                            v-model="m.selectedMainIdx"
                            :class="['modal-input', { error: m.errors.mainEntrance }]"
                        >
                            <option value="">— Select main entrance —</option>
                            <option v-for="e in m.mainEntranceOptions" :key="e.idx" :value="e.idx">{{ e.label }}</option>
                        </select>
                    </div>
                    <div v-if="bisStr" class="modal-field">
                        <label>BIS Number (auto-suggested)</label>
                        <input
                            type="text"
                            :value="bisStr"
                            class="modal-input modal-input-readonly"
                            readonly
                        />
                    </div>
                </template>

            </template>

            <!-- Public Buildings: sector → building cascading selectors -->
            <template v-if="phase?.key === 'publicBuildings'">
                <div class="modal-field">
                    <label>Sector <span class="req">*</span></label>
                    <select v-model="m.sectorKey" class="modal-input">
                        <option v-for="s in PUBLIC_BUILDING_SECTORS" :key="s.key" :value="s.key">{{ s.label }}</option>
                    </select>
                </div>
                <div class="modal-field">
                    <label>Building Type <span class="req">*</span></label>
                    <select v-model="m.buildingTypeKey" class="modal-input">
                        <option v-for="b in currentSectorBuildings" :key="b.key" :value="b.key">{{ b.label }}</option>
                    </select>
                </div>
            </template>

            <!-- Public spaces: space type selector -->
            <div v-if="phase?.key === 'publicSpaces'" class="modal-field">
                <label>Space Type <span class="req">*</span></label>
                <select v-model="m.spaceTypeKey" class="modal-input">
                    <option v-for="s in PUBLIC_SPACE_TYPES" :key="s.key" :value="s.key">{{ s.label }}</option>
                </select>
            </div>

            <!-- Buttons -->
            <div class="modal-buttons">
                <button class="modal-btn modal-btn-save"   @click="onSave">{{ m.isEdit ? 'Update' : 'Save' }}</button>
                <button class="modal-btn modal-btn-cancel" @click="onCancel">Cancel</button>
            </div>

        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, watch }                                          from 'vue'
import { useI18n }                                                  from 'vue-i18n'
import { store }                                                    from '../store'
import { PHASES, AREA_TYPES, DISTRICT_TYPES, ROAD_TYPES, PUBLIC_SPACE_TYPES, PUBLIC_BUILDING_SECTORS } from '../phases'
import { resolveModal }                                             from '../store'
import type { FeatureData }                                         from '../types'
import { fetchRoadSide, computeBisNumber }                          from '../map'

const { t } = useI18n()
const m     = computed(() => store.modal)
const phase = computed(() => m.value.phaseIndex !== null ? PHASES[m.value.phaseIndex] ?? null : null)

// ── Computed display helpers ──────────────────────────────────────────────────

const headerText = computed(() => {
    if (!phase.value) return ''
    const name = phase.value.label.replace(/s$/, '')
    return m.value.isEdit ? `Edit ${name} Info` : `Add ${name} Details`
})

const sideText = computed(() => {
    if (!m.value.entranceSide) return ''
    return m.value.entranceSide === 'left'
        ? 'Left side — odd numbers'
        : 'Right side — even numbers'
})

const bisStr = computed(() =>
    m.value.bisNumber ? 'BIS' + String(m.value.bisNumber).padStart(2, '0') : '')

// Available area type options (hide "Main Urban" if it already exists)
const areaTypeOptions = computed(() =>
    AREA_TYPES.filter(a => !(a.key === 'central_urban' && m.value.mainUrbanExists)))

// Whether the current area being created/edited is a main urban area —
// in that case the name is always the municipality name and is not editable.
const isMainUrban = computed(() =>
    phase.value?.key === 'areas' && m.value.areaTypeKey === 'central_urban')

// Whether the current district being created/edited is a Trade Activity Zone or Industrial Zone —
// in that case the name is always the type name and is not editable.
const isZoneWithTypeName = computed(() =>
    phase.value?.key === 'districts' &&
    (m.value.districtTypeKey === 'trad_activities_zone' || m.value.districtTypeKey === 'industry_zone'))

// City center is always named "City Center" — the user cannot change it.
const isCityCenter = computed(() => phase.value?.key === 'cityCenter')

// Editing a house entrance: name, decision fields and road assignment are
// hidden — they were set at creation and must not be changed.
const isHouseEntranceEdit = computed(() =>
    phase.value?.key === 'houseEntrances' && m.value.isEdit)

// ── Watchers ──────────────────────────────────────────────────────────────────

// When road selection changes → fetch side + suggested number.
// In edit mode the side/number are already populated from existing data — skip
// the API call so we don't overwrite them when the selector is pre-selected.
watch(() => m.value.selectedRoadIdx, async (val) => {
    if (val === '' || val === null) return
    if (m.value.isEdit) return
    const roadOption = m.value.roadOptions[Number(val)]
    if (!roadOption) return
    await fetchRoadSide(roadOption.dbId, Number(val))
})

// When main entrance selection changes → compute BIS number
watch(() => m.value.selectedMainIdx, (val) => {
    if (val === '' || val === null) return
    const option = m.value.mainEntranceOptions[Number(val)]
    if (!option) return
    computeBisNumber(option.dbId)
})

// Buildings available for the currently selected sector
const currentSectorBuildings = computed(() => {
    const sector = PUBLIC_BUILDING_SECTORS.find(s => s.key === m.value.sectorKey)
    return sector?.buildings ?? []
})

// When sector changes, reset building to the first option of the new sector
watch(() => m.value.sectorKey, () => {
    const first = currentSectorBuildings.value[0]
    if (first) m.value.buildingTypeKey = first.key
})
// and only clear when switching away if the label was the auto-filled municipality name.
watch(() => m.value.areaTypeKey, (val) => {
    if (val === 'central_urban') {
        // Use municipalityName or fall back to user.commune.name_fr
        const communeName = store.municipalityName
            || (store.user as any)?.commune?.name_fr
            || (store.user as any)?.commune?.name_ar
            || ''
        m.value.label = communeName
    } else if (!m.value.isEdit && m.value.label === store.municipalityName) {
        m.value.label = ''
    }
})

// When district type changes → auto-fill name for zones that use type name
watch(() => m.value.districtTypeKey, (val) => {
    if (val === 'trad_activities_zone' || val === 'industry_zone') {
        const dtype = DISTRICT_TYPES.find(d => d.key === val)
        m.value.label = dtype?.label ?? ''  // Zone uses type name
    }
})

// ── Validation + submit ───────────────────────────────────────────────────────

function validate() {
    const errors: Record<string, string> = {}
    const key = phase.value?.key

    // Name, decision number and date are hidden when editing a house entrance —
    // skip their validation entirely in that case.
    if (!isHouseEntranceEdit.value) {
        const labelRequired = !(key === 'districts' &&
            (m.value.districtTypeKey === 'trad_activities_zone' || m.value.districtTypeKey === 'industry_zone')) &&
            key !== 'cityCenter' &&
            !(key === 'areas' && m.value.areaTypeKey === 'central_urban')
        if (labelRequired && !m.value.label.trim()) errors.label = 'Required'
        if (!m.value.decisionNumber.trim()) errors.decisionNumber = 'Required'
        if (!m.value.decisionDate.trim())   errors.decisionDate   = 'Required'
    }

    // Road / main-entrance selectors are also hidden in edit mode — skip them too.
    if (!m.value.isEdit) {
        if (key === 'houseEntrances' && m.value.entranceTypeKey === 'main_entrance'      && m.value.selectedRoadIdx  === '') errors.road        = 'Required'
        if (key === 'houseEntrances' && m.value.entranceTypeKey === 'secondary_entrance' && m.value.selectedMainIdx  === '') errors.mainEntrance = 'Required'
    }

    store.modal.errors = errors
    return Object.keys(errors).length === 0
}

function onSave() {
    if (!validate()) return
    const key = phase.value?.key
    const result: Partial<FeatureData> = {
        label:          (isMainUrban.value
                            ? (store.municipalityName || (store.user as any)?.commune?.name_fr || '')
                            : m.value.label.trim()),
        decisionNumber: m.value.decisionNumber.trim(),
        decisionDate:   m.value.decisionDate.trim(),
    }

    if (key === 'areas') {
        result.areaTypeKey = m.value.areaTypeKey
    } else if (key === 'districts') {
        result.districtTypeKey = m.value.districtTypeKey
    } else if (key === 'roads') {
        result.roadTypeKey = m.value.roadTypeKey
    } else if (key === 'houseEntrances') {
        result.entranceTypeKey = m.value.entranceTypeKey
        if (m.value.entranceTypeKey === 'main_entrance') {
            const roadOption = m.value.roadOptions[Number(m.value.selectedRoadIdx)]
            result.roadDbId       = roadOption?.dbId
            result.roadLabel      = roadOption?.label
            result.side           = m.value.entranceSide ?? undefined
            result.entranceNumber = m.value.entranceNumber ?? undefined
        } else {
            const mainOption = m.value.mainEntranceOptions[Number(m.value.selectedMainIdx)]
            result.mainEntranceDbId  = mainOption?.dbId
            result.mainEntranceLabel = mainOption?.label
            result.bisNumber         = m.value.bisNumber ?? undefined
        }
    } else if (key === 'publicBuildings') {
        result.sectorKey       = m.value.sectorKey
        result.buildingTypeKey = m.value.buildingTypeKey
    } else if (key === 'publicSpaces') {
        result.spaceTypeKey = m.value.spaceTypeKey
    }

    resolveModal(result as import("../types").ModalResult)
}

function onCancel() { resolveModal(null) }

// Keyboard shortcuts
function onKeyup(e: KeyboardEvent) {
    if (!m.value.visible) return
    if (e.key === 'Enter')  onSave()
    if (e.key === 'Escape') onCancel()
}
</script>
