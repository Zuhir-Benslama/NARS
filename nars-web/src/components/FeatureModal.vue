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

      <!-- Name (localized) -->
      <fieldset class="modal-field-group">
        <legend class="modal-group-label">
          {{ t("label_name") }}
          <span class="req">*</span>
        </legend>
        <span v-if="isMainUrban" class="modal-field-note">
          {{ t("modal_note_main_urban") }}
        </span>
        <span v-if="isZoneWithTypeName" class="modal-field-note">{{
          t("modal_note_zone_type_name")
        }}</span>
        <span v-if="isRoundabouts" class="modal-field-note">{{
          t("modal_note_plain_roundabouts_name")
        }}</span>
        <div class="modal-field">
          <label>{{ t("label_name_ar") }}</label>
          <input
            v-model="modalStore.labelAr"
            type="text"
            :class="[
              'modal-input',
              { 'modal-input-readonly': isMainUrban || isZoneWithTypeName || isRoundabouts },
            ]"
            :placeholder="
              isMainUrban || isZoneWithTypeName || isRoundabouts ? '' : t('placeholder_name_ar')
            "
            :readonly="isMainUrban || isZoneWithTypeName || isRoundabouts"
            :disabled="isMainUrban"
            autocomplete="off"
            autofocus
          />
        </div>
        <div class="modal-field">
          <label>{{ t("label_name_fr") }}</label>
          <input
            v-model="modalStore.labelFr"
            type="text"
            :class="[
              'modal-input',
              { 'modal-input-readonly': isMainUrban || isZoneWithTypeName || isRoundabouts },
            ]"
            :placeholder="
              isMainUrban || isZoneWithTypeName || isRoundabouts ? '' : t('placeholder_name_fr')
            "
            :readonly="isMainUrban || isZoneWithTypeName || isRoundabouts"
            :disabled="isMainUrban"
            autocomplete="off"
          />
        </div>
        <div class="modal-field">
          <label>{{ t("label_name_en") }}</label>
          <input
            v-model="modalStore.labelEn"
            type="text"
            :class="[
              'modal-input',
              { 'modal-input-readonly': isMainUrban || isZoneWithTypeName || isRoundabouts },
            ]"
            :placeholder="
              isMainUrban || isZoneWithTypeName || isRoundabouts ? '' : t('placeholder_name_en')
            "
            :readonly="isMainUrban || isZoneWithTypeName || isRoundabouts"
            :disabled="isMainUrban"
            autocomplete="off"
          />
        </div>
      </fieldset>

      <!-- Decision No. + Date — hidden when editing roundabouts -->
      <div v-if="!isRoundabouts" class="modal-row">
        <div class="modal-field">
          <label>
            {{ t("label_decision_no") }}
            <span v-if="decisionRequired" class="req">*</span>
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
            <span v-if="decisionRequired" class="req">*</span>
          </label>
          <input
            v-model="modalStore.decisionDate"
            type="date"
            :class="['modal-input', { error: modalStore.errors.decisionDate }]"
          />
        </div>
      </div>

      <!-- Roundabouts: radius input -->
      <div v-if="isRoundabouts" class="modal-field">
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

      <!-- Roundabouts can be plain city centers or true roundabouts (special numbering) -->
      <div v-if="isRoundabouts" class="modal-field">
        <label>{{ t("label_roundabouts_type") }}</label>
        <select v-model="modalStore.roundaboutsTypeKey" class="modal-input">
          <option
            v-for="c in ROUNDABOUTS_SUBTYPES"
            :key="c.key"
            :value="c.key"
            :disabled="c.key === 'city_center' && modalStore.plainRoundaboutsUnavailable"
          >
            {{ t("featureTypes." + c.key) }}
          </option>
        </select>
        <span
          v-if="isRoundabouts && modalStore.plainRoundaboutsUnavailable"
          class="modal-field-note"
        >
          {{ t("map_plain_roundabouts_exists") }}
        </span>
      </div>

      <!-- Optional width of the numbering band around roundabouts and squares -->
      <div
        v-if="
          (isRoundabouts && modalStore.roundaboutsTypeKey === 'roundabout') ||
          (phase?.key === 'publicSpaces' && modalStore.spaceTypeKey === 'square')
        "
        class="modal-field"
      >
        <label>{{ t("label_numbering_band") }}</label>
        <input
          v-model.number="modalStore.numberingOutsideBandM"
          type="number"
          min="1"
          :class="['modal-input', { error: modalStore.errors.numberingOutsideBandM }]"
          :placeholder="t('placeholder_numbering_band')"
          autocomplete="off"
        />
        <span class="modal-field-note">{{ t("label_numbering_band_note") }}</span>
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
import {
  PHASES,
  DISTRICT_TYPES,
  ROAD_TYPES,
  PUBLIC_SPACE_TYPES,
  ROUNDABOUTS_SUBTYPES,
} from "../phases"
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

const { validate, buildModalResult, isMainUrban, isRoundabouts } = useFeatureValidation(modalStore)

// Roads may exist without an administrative decision (AI-generated), so the
// decision fields are only mandatory for the other phases. Keep the asterisks
// in sync with useFeatureValidation's rule.
const decisionRequired = computed(() => phase.value?.key !== "roads")

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
      modalStore.patchFields({
        label: appStore.communeName,
        labelAr: appStore.communeNameAr,
        labelFr: appStore.communeNameFr,
        labelEn: appStore.communeNameEn,
      })
    } else if (!modalStore.isEdit && modalStore.label === appStore.communeName) {
      modalStore.patchFields({ label: "", labelAr: "", labelFr: "", labelEn: "" })
    }
    if (districtType === "trad_activities_zone" || districtType === "industry_zone") {
      modalStore.patchFields({ label: "" })
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
    onKeydown(e)
  },
  Escape: (e) => {
    onKeydown(e)
  },
})
</script>
