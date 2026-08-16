<template>
  <div
    v-if="modalStore.visible"
    ref="modalRef"
    class="modal"
    role="dialog"
    aria-modal="true"
    aria-labelledby="modalHeader"
  >
    <div class="modal-content">
      <div id="modalHeader" class="modal-header">
        {{ headerText }}
      </div>

      <!-- Hint bar -->
      <div v-if="phase?.hint" id="modalHint">
        {{ t(phase.hint) }}
      </div>

      <!-- Name -->
      <div class="modal-field">
        <label>
          {{ t("label_name") }}
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
          :placeholder="
            isMainUrban || isZoneWithTypeName || isCityCenter ? '' : t('placeholder_feature_name')
          "
          :readonly="isMainUrban || isZoneWithTypeName || isCityCenter"
          :disabled="isMainUrban"
          autocomplete="off"
          autofocus
        />
        <span v-if="isMainUrban" class="modal-field-note">
          {{ t("modal_note_main_urban") }}
        </span>
        <span v-if="isZoneWithTypeName" class="modal-field-note">{{
          t("modal_note_zone_type_name")
        }}</span>
        <span v-if="isCityCenter" class="modal-field-note">{{
          t("modal_note_city_center_name")
        }}</span>
      </div>

      <!-- Decision No. + Date — hidden when editing a city center -->
      <div v-if="!isCityCenter" class="modal-row">
        <div class="modal-field">
          <label>
            {{ t("label_decision_no") }}
            <span class="req">*</span>
          </label>
          <input
            v-model="modalStore.decisionNumber"
            type="text"
            :class="['modal-input', { error: modalStore.errors.decisionNumber }]"
            :placeholder="t('placeholder_decision_no')"
            autocomplete="off"
          />
        </div>
        <div class="modal-field">
          <label>
            {{ t("label_decision_date") }}
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
          {{ t("label_radius") }}
          <span class="req">*</span>
        </label>
        <input
          v-model.number="modalStore.radius"
          type="number"
          :class="['modal-input', { error: modalStore.errors.radius }]"
          :placeholder="t('placeholder_radius')"
          min="5"
          autocomplete="off"
        />
        <span class="modal-field-note">{{ t("label_radius_note") }}</span>
      </div>

      <!-- ── Phase-specific extras ────────────────────────────────── -->

      <!-- Areas: area type selector (extracted) -->
      <AreaTypeSelector v-if="phase?.key === 'areas'" />

      <!-- Districts: district type selector -->
      <div v-if="phase?.key === 'districts'" class="modal-field">
        <label>
          {{ t("label_district_type") }}
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.districtTypeKey" class="modal-input">
          <option v-for="d in DISTRICT_TYPES" :key="d.key" :value="d.key">
            {{ t("featureTypes." + d.key) }}
          </option>
        </select>
      </div>

      <!-- Roads: road type selector -->
      <div v-if="phase?.key === 'roads'" class="modal-field">
        <label>
          {{ t("label_road_type") }}
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.roadTypeKey" class="modal-input">
          <option v-for="r in ROAD_TYPES" :key="r.key" :value="r.key">
            {{ t("featureTypes." + r.key) }}
          </option>
        </select>
      </div>

      <!-- Public Buildings: sector → building cascading selectors (extracted) -->
      <BuildingTypeSelector v-if="phase?.key === 'publicBuildings'" />

      <!-- Public spaces: space type selector -->
      <div v-if="phase?.key === 'publicSpaces'" class="modal-field">
        <label>
          {{ t("label_space_type") }}
          <span class="req">*</span>
        </label>
        <select v-model="modalStore.spaceTypeKey" class="modal-input">
          <option v-for="s in PUBLIC_SPACE_TYPES" :key="s.key" :value="s.key">
            {{ t("featureTypes." + s.key) }}
          </option>
        </select>
      </div>

      <!-- Buttons -->
      <div class="modal-buttons">
        <button class="modal-btn modal-btn-save" @click="onSave">
          {{ modalStore.isEdit ? t("btn_update") : t("btn_save") }}
        </button>
        <button class="modal-btn modal-btn-cancel" @click="onCancel">{{ t("btn_cancel") }}</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, watch, ref } from "vue"
import { useI18n } from "vue-i18n"
import { PHASES, DISTRICT_TYPES, ROAD_TYPES, PUBLIC_SPACE_TYPES } from "../phases"
import { useAppStore } from "../stores/appStore"
import { useModalStore } from "../stores/modalStore"
import { useFeatureValidation } from "../composables/useFeatureValidation"
import { useFocusTrap } from "../composables/useFocusTrap"
import { useWindowKeydown } from "../composables/useWindowKeydown"
import AreaTypeSelector from "./modals/AreaTypeSelector.vue"
import BuildingTypeSelector from "./modals/BuildingTypeSelector.vue"

const { t } = useI18n()
const appStore = useAppStore()
const modalStore = useModalStore()
const phase = computed(() =>
  modalStore.phaseIndex !== null ? (PHASES[modalStore.phaseIndex] ?? null) : null,
)

const modalRef = ref<HTMLElement | null>(null)
useFocusTrap(modalRef, () => modalStore.visible)

const { validate, buildModalResult, isMainUrban, isCityCenter } = useFeatureValidation(modalStore)

// ── Computed display helpers ──────────────────────────────────────────────────

const headerText = computed(() => {
  if (!phase.value) return ""
  const name = t(phase.value.label)
  return modalStore.isEdit ? t("modal_edit", { name }) : t("modal_add", { name })
})

const isZoneWithTypeName = computed(
  () =>
    phase.value?.key === "districts" &&
    (modalStore.districtTypeKey === "trad_activities_zone" ||
      modalStore.districtTypeKey === "industry_zone"),
)

// ── Watchers ──────────────────────────────────────────────────────────────────

// When area type or district type changes → auto-fill the name.
// The zone type name is localized via t() (featureTypes.*) rather than the
// English catalog label, so the persisted feature name is not i18n-hostile.
watch(
  [() => modalStore.areaTypeKey, () => modalStore.districtTypeKey],
  ([areaType, districtType]) => {
    if (areaType === "central_urban") {
      modalStore.patchFields({ label: appStore.communeName })
    } else if (!modalStore.isEdit && modalStore.label === appStore.communeName) {
      modalStore.patchFields({ label: "" })
    }
    if (districtType === "trad_activities_zone" || districtType === "industry_zone") {
      modalStore.patchFields({ label: t("featureTypes." + districtType) })
    }
  },
)

// ── Validation + submit ───────────────────────────────────────────────────────

function onSave() {
  const errors = validate()
  modalStore.patchFields({ errors })
  if (Object.keys(errors).length > 0) return
  const result = buildModalResult(appStore.communeName)
  modalStore.close(result)
}

function onCancel() {
  modalStore.close(null)
}

// Keyboard shortcuts — listen on window so focus is not required
function onKeydown(e: KeyboardEvent) {
  if (!modalStore.visible) return
  if (e.key === "Enter") {
    const tag = (e.target as HTMLElement)?.tagName
    // Let native Enter activate focused buttons (e.g. Cancel/Save) and native
    // input behavior; only treat Enter as a save shortcut elsewhere.
    if (tag === "SELECT" || tag === "TEXTAREA" || tag === "BUTTON" || tag === "INPUT") return
    e.preventDefault()
    onSave()
  }
  if (e.key === "Escape") {
    e.preventDefault()
    onCancel()
  }
}
useWindowKeydown({
  Enter: (e) => {
    if (modalStore.visible) onKeydown(e)
  },
  Escape: (e) => {
    if (modalStore.visible) onKeydown(e)
  },
})
</script>
