<template>
    <div v-show="m.visible" class="modal" @keyup="onKeyup">
        <div class="modal-content">

            <div class="modal-header">{{ headerText }}</div>

            <!-- Hint bar -->
            <div id="modalHint" v-if="phase?.hint">{{ phase.hint }}</div>

            <!-- Name -->
            <div class="modal-field">
                <label>Name <span class="req">*</span></label>
                <input
                    type="text"
                    v-model="m.label"
                    :class="['modal-input', { error: m.errors.label }]"
                    placeholder="Feature name..."
                    autocomplete="off"
                    autofocus
                />
            </div>

            <!-- Decision No. + Date (side by side) -->
            <div class="modal-row">
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

                <!-- Main entrance: road selector + side detection -->
                <template v-if="m.entranceTypeKey === 'main_entrance'">
                    <div class="modal-field">
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
import { store }                                                    from '../store'
import { PHASES, AREA_TYPES, DISTRICT_TYPES, ROAD_TYPES, PUBLIC_SPACE_TYPES } from '../phases'
import { resolveModal }                                             from '../store'
import type { FeatureData }                                         from '../types'
import { fetchRoadSide, computeBisNumber }                          from '../map'

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

// ── Watchers ──────────────────────────────────────────────────────────────────

// When road selection changes → fetch side + suggested number
watch(() => m.value.selectedRoadIdx, async (val) => {
    if (val === '' || val === null) return
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

// When area type changes → auto-fill municipality name (only for new features)
watch(() => m.value.areaTypeKey, (val) => {
    if (m.value.isEdit) return
    if (val === 'central_urban' && !m.value.label && store.municipalityName)
        m.value.label = store.municipalityName
})

// ── Validation + submit ───────────────────────────────────────────────────────

function validate() {
    const errors: Record<string, string> = {}
    if (!m.value.label.trim())          errors.label          = 'Required'
    if (!m.value.decisionNumber.trim()) errors.decisionNumber = 'Required'
    if (!m.value.decisionDate.trim())   errors.decisionDate   = 'Required'

    const key = phase.value?.key
    if (key === 'houseEntrances' && m.value.entranceTypeKey === 'main_entrance'      && m.value.selectedRoadIdx  === '') errors.road        = 'Required'
    if (key === 'houseEntrances' && m.value.entranceTypeKey === 'secondary_entrance' && m.value.selectedMainIdx  === '') errors.mainEntrance = 'Required'

    store.modal.errors = errors
    return Object.keys(errors).length === 0
}

function onSave() {
    if (!validate()) return
    const key = phase.value?.key
    const result: Partial<FeatureData> = {
        label:          m.value.label.trim(),
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
