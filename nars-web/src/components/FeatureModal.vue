<template>
  <div v-show="modalStore.visible" class="modal">
    <div class="modal-content">
      <div class="modal-header">
        {{ headerText }}
      </div>

      <!-- Hint bar -->
      <div v-if="phase?.hint" id="modalHint">
        {{ t(phase.hint) }}
      </div>

      <!-- Name — hidden when editing a house entrance (set at creation, not editable) -->
      <div v-if="!isHouseEntranceEdit" class="modal-field">
        <label>
          Name
          <span class="req">*</span>
        </label>
        <input
          v-model="modalStore.label"
          type="text"
          :class="[
            'modal-input',
            {
              error: modalStore.errors.label,
              'modal-input-readonly': isMainUrban || isZoneWithTypeName || isCityCenter,
            },
          ]"
          :placeholder="isMainUrban || isZoneWithTypeName || isCityCenter ? '' : 'Feature name...'"
          :readonly="isMainUrban || isZoneWithTypeName || isCityCenter"
          :disabled="isMainUrban"
          autocomplete="off"
          autofocus
        />
        <span v-if="isMainUrban" class="modal-field-note">
          The main urban area takes the municipality name.
        </span>
        <span v-if="isZoneWithTypeName" class="modal-field-note"
          >This zone uses its type name.</span
        >
        <span v-if="isCityCenter" class="modal-field-note"
          >The city center is always named "City Center".</span
        >
      </div>

      <!-- Decision No. + Date — hidden when editing a house entrance or city center -->
      <div v-if="!isHouseEntranceEdit && !isCityCenter" class="modal-row">
        <div class="modal-field">
          <label>
            Decision No.
            <span class="req">*</span>
          </label>
          <input
            v-model="modalStore.decisionNumber"
            type="text"
            :class="['modal-input', { error: modalStore.errors.decisionNumber }]"
            placeholder="e.g. 2024/001"
            autocomplete="off"
          />
        </div>
        <div class="modal-field">
          <label>
            Decision Date
            <span class="req">*</span>
          </label>
          <input
            v-model="modalStore.decisionDate"
            type="date"
            :class="['modal-input', { error: modalStore.errors.decisionDate }]"
          />
        </div>
      </div>

      <!-- City Center: radius input -->
      <div v-if="isCityCenter" class="modal-field">
        <label>
          Radius (meters)
          <span class="req">*</span>
        </label>
        <input
          v-model.number="modalStore.radius"
          type="number"
          :class="['modal-input', { error: modalStore.errors.radius }]"
          placeholder="e.g. 200"
          min="5"
          autocomplete="off"
        />
        <span class="modal-field-note">Minimum radius: 5 meters</span>
      </div>

      <!-- ── Phase-specific extras ────────────────────────────────── -->

      <!-- Areas: area type selector (extracted) -->
      <AreaTypeSelector v-if="phase?.key === 'areas'" />

      <!-- Districts: district type selector -->
      <div v-if="phase?.key === 'districts'" class="modal-field">
        <label>
          District Type
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.districtTypeKey" class="modal-input">
          <option v-for="d in DISTRICT_TYPES" :key="d.key" :value="d.key">
            {{ d.label }}
          </option>
        </select>
      </div>

      <!-- Roads: road type selector -->
      <div v-if="phase?.key === 'roads'" class="modal-field">
        <label>
          Road Type
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.roadTypeKey" class="modal-input">
          <option v-for="r in ROAD_TYPES" :key="r.key" :value="r.key">
            {{ r.label }}
          </option>
        </select>
      </div>

      <!-- House Entrances: sub-type selector + conditional fields (extracted) -->
      <RoadAssignmentSelector v-if="phase?.key === 'houseEntrances'" />

      <!-- Public Buildings: sector → building cascading selectors (extracted) -->
      <BuildingTypeSelector v-if="phase?.key === 'publicBuildings'" />

      <!-- Public spaces: space type selector -->
      <div v-if="phase?.key === 'publicSpaces'" class="modal-field">
        <label>
          Space Type
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.spaceTypeKey" class="modal-input">
          <option v-for="s in PUBLIC_SPACE_TYPES" :key="s.key" :value="s.key">
            {{ s.label }}
          </option>
        </select>
      </div>

      <!-- Buttons -->
      <div class="modal-buttons">
        <button class="modal-btn modal-btn-save" @click="onSave">
          {{ modalStore.isEdit ? "Update" : "Save" }}
        </button>
        <button class="modal-btn modal-btn-cancel" @click="onCancel">Cancel</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, watch, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { PHASES, DISTRICT_TYPES, ROAD_TYPES, PUBLIC_SPACE_TYPES } from "../phases"
import { useAppStore } from "../stores/appStore"
import { useModalStore } from "../stores/modalStore"
import { fetchRoadSide, computeBisNumber } from "../map"
import { useFeatureValidation } from "../composables/useFeatureValidation"
import AreaTypeSelector from "./modals/AreaTypeSelector.vue"
import RoadAssignmentSelector from "./modals/RoadAssignmentSelector.vue"
import BuildingTypeSelector from "./modals/BuildingTypeSelector.vue"

const { t } = useI18n()
const appStore = useAppStore()
const modalStore = useModalStore()
const phase = computed(() =>
  modalStore.phaseIndex !== null ? (PHASES[modalStore.phaseIndex] ?? null) : null,
)

const { validate, buildModalResult, isMainUrban, isCityCenter, isHouseEntranceEdit } =
  useFeatureValidation(modalStore)

// ── Computed display helpers ──────────────────────────────────────────────────

const headerText = computed(() => {
  if (!phase.value) return ""
  const name = phase.value.label.replace(/s$/, "")
  return modalStore.isEdit ? `Edit ${name} Info` : `Add ${name} Details`
})

const isZoneWithTypeName = computed(
  () =>
    phase.value?.key === "districts" &&
    (modalStore.districtTypeKey === "trad_activities_zone" ||
      modalStore.districtTypeKey === "industry_zone"),
)

// ── Watchers ──────────────────────────────────────────────────────────────────

// When road selection changes → fetch side + suggested number.
// In edit mode the side/number are already populated from existing data — skip
// the API call so we don't overwrite them when the selector is pre-selected.
watch(
  () => modalStore.selectedRoadIdx,
  async (val) => {
    if (val === "" || val === null) return
    if (modalStore.isEdit) return
    const roadOption = modalStore.roadOptions[Number(val)]
    if (!roadOption) return
    await fetchRoadSide(roadOption.dbId)
  },
)

// When main entrance selection changes → compute BIS number
watch(
  () => modalStore.selectedMainIdx,
  (val) => {
    if (val === "" || val === null) return
    const option = modalStore.mainEntranceOptions[Number(val)]
    if (!option) return
    computeBisNumber(option.dbId)
  },
)

// When area type or district type changes → auto-fill the name.
watch(
  [() => modalStore.areaTypeKey, () => modalStore.districtTypeKey],
  ([areaType, districtType]) => {
    if (areaType === "central_urban") {
      modalStore.label = appStore.communeName
    } else if (!modalStore.isEdit && modalStore.label === appStore.municipalityName) {
      modalStore.label = ""
    }
    if (districtType === "trad_activities_zone" || districtType === "industry_zone") {
      const dtype = DISTRICT_TYPES.find((d: { key: string }) => d.key === districtType)
      modalStore.label = dtype?.label ?? ""
    }
  },
)

// ── Validation + submit ───────────────────────────────────────────────────────

function onSave() {
  if (!validate()) return
  const result = buildModalResult(appStore.communeName)
  modalStore.close(result as import("../types").ModalResult)
}

function onCancel() {
  modalStore.close(null)
}

// Keyboard shortcuts — listen on window so focus is not required
function onKeyup(e: KeyboardEvent) {
  if (!modalStore.visible) return
  if (e.key === "Enter") onSave()
  if (e.key === "Escape") onCancel()
}
onMounted(() => window.addEventListener("keyup", onKeyup))
onUnmounted(() => window.removeEventListener("keyup", onKeyup))
</script>
