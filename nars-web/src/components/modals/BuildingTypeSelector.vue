<!-- Building Type Selector — used in FeatureModal for public buildings phase. -->
<template>
  <div>
    <div class="modal-field">
      <label>
        {{ t("label_sector") }}
        <span class="req">*</span>
      </label>
      <select v-model="m.sectorKey" class="modal-input">
        <option v-for="s in PUBLIC_BUILDING_SECTORS" :key="s.key" :value="s.key">
          {{ t("featureTypes." + s.key) }}
        </option>
      </select>
    </div>
    <div class="modal-field">
      <label>
        {{ t("label_building_type") }}
        <span class="req">*</span>
      </label>
      <select v-model="m.buildingTypeKey" class="modal-input">
        <option v-for="b in currentSectorBuildings" :key="b.key" :value="b.key">
          {{ t("featureTypes." + b.key) }}
        </option>
      </select>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useModalStore } from "../../stores/modalStore"
import { PUBLIC_BUILDING_SECTORS } from "../../phases"

const { t } = useI18n()
const m = useModalStore()

const currentSectorBuildings = computed(() => {
  const sector = PUBLIC_BUILDING_SECTORS.find((s) => s.key === m.sectorKey)
  return sector?.buildings ?? []
})
</script>
