<!-- Road Assignment Selector — used in FeatureModal for main entrances. -->
<template>
  <template v-if="m.entranceTypeKey === 'main_entrance'">
    <div v-if="!m.isEdit" class="modal-field">
      <label>
        Assign to Road
        <span class="req">*</span>
      </label>
      <select v-model="m.selectedRoadIdx" :class="['modal-input', { error: m.errors.road }]">
        <option value="">— Select a road —</option>
        <option v-for="r in m.roadOptions" :key="r.idx" :value="r.idx">
          {{ r.label }}
        </option>
      </select>
    </div>
    <div v-if="m.selectedRoadIdx !== ''" class="modal-field">
      <label>
        Entrance Number
        <span v-if="sideText" class="modal-side-hint">— {{ sideText }}</span>
      </label>
      <div class="modal-input-row">
        <input
          v-model.number="m.entranceNumber"
          type="number"
          class="modal-input modal-input-narrow"
          min="1"
        />
        <span v-if="m.entranceSideLoading" class="field-spinner" />
      </div>
    </div>
  </template>

  <template v-if="m.entranceTypeKey === 'secondary_entrance'">
    <div class="modal-field">
      <label>
        Assign to Main Entrance
        <span class="req">*</span>
      </label>
      <select
        v-model="m.selectedMainIdx"
        :class="['modal-input', { error: m.errors.mainEntrance }]"
      >
        <option value="">— Select main entrance —</option>
        <option v-for="e in m.mainEntranceOptions" :key="e.idx" :value="e.idx">
          {{ e.label }}
        </option>
      </select>
    </div>
    <div v-if="bisStr" class="modal-field">
      <label>BIS Number (auto-suggested)</label>
      <input type="text" :value="bisStr" class="modal-input modal-input-readonly" readonly />
    </div>
  </template>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useModalStore } from "../../stores/modalStore"

const m = useModalStore()

const sideText = computed(() => {
  if (!m.entranceSide) return ""
  return m.entranceSide === "left" ? "Left side — odd numbers" : "Right side — even numbers"
})

const bisStr = computed(() => (m.bisNumber ? "BIS" + String(m.bisNumber).padStart(2, "0") : ""))
</script>
