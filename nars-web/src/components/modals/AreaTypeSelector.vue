<!-- Area Type Selector — used in FeatureModal for the areas phase. -->
<template>
  <div class="modal-field">
    <label>
      {{ t("label_area_type") }}
      <span class="req">*</span>
    </label>
    <select v-model="m.areaTypeKey" class="modal-input">
      <option v-for="a in areaTypeOptions" :key="a.key" :value="a.key">
        {{ t("featureTypes." + a.key) }}
      </option>
    </select>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useModalStore } from "../../stores/modalStore"
import { AREA_TYPES } from "../../phases"

const { t } = useI18n()
const m = useModalStore()

const areaTypeOptions = computed(() =>
  AREA_TYPES.filter((a) => !(a.key === "central_urban" && m.mainUrbanExists)),
)
</script>
