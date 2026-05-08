<!-- Building Type Selector — used in FeatureModal for public buildings phase. -->
<template>
  <div>
    <div class="modal-field">
      <label>
        Sector
        <span class="req">*</span>
      </label>
      <select v-model="m.sectorKey" class="modal-input">
        <option v-for="s in PUBLIC_BUILDING_SECTORS" :key="s.key" :value="s.key">
          {{ s.label }}
        </option>
      </select>
    </div>
    <div class="modal-field">
      <label>
        Building Type
        <span class="req">*</span>
      </label>
      <select v-model="m.buildingTypeKey" class="modal-input">
        <option v-for="b in currentSectorBuildings" :key="b.key" :value="b.key">
          {{ b.label }}
        </option>
      </select>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { store } from "../../store"
import { PUBLIC_BUILDING_SECTORS } from "../../phases"

const m = store.modal

const currentSectorBuildings = computed(() => {
  const sector = PUBLIC_BUILDING_SECTORS.find((s) => s.key === m.sectorKey)
  return sector?.buildings ?? []
})
</script>
