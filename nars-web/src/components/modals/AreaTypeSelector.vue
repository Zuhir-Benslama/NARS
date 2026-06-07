<!-- Area Type Selector — used in FeatureModal for the areas phase. -->
<template>
  <div class="modal-field">
    <label>
      Area Type
      <span class="req">*</span>
    </label>
    <select v-model="m.areaTypeKey" class="modal-input">
      <option v-for="a in areaTypeOptions" :key="a.key" :value="a.key">
        {{ a.label }}
      </option>
    </select>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useModalStore } from "../../stores/modalStore"
import { AREA_TYPES } from "../../phases"

const m = useModalStore()

const areaTypeOptions = computed(() =>
  AREA_TYPES.filter((a) => !(a.key === "central_urban" && m.mainUrbanExists)),
)
</script>
