<template>
  <div class="info-panel">
    <div class="info-title">
      {{ t("info_title") }}
    </div>
    <div class="info-item">
      ⬟ {{ t("info_areas") }}:
      <span>{{ counts.areas }}</span>
    </div>
    <div class="info-item">
      📍 {{ t("info_city_center") }}:
      <span>{{ cityCenterStatus }}</span>
    </div>
    <div class="info-item">
      🏘️ {{ t("info_districts") }}:
      <span>{{ counts.districts }}</span>
    </div>
    <div class="info-item">
      🛣️ {{ t("info_roads") }}:
      <span>{{ counts.roads }}</span>
    </div>
    <div class="info-item">
      🚪 {{ t("info_main_entrances") }}:
      <span>{{ counts.mainEntrances }}</span>
    </div>
    <div class="info-item">
      🔢 {{ t("info_secondary_entrances") }}:
      <span>{{ counts.secondaryEntrances }}</span>
    </div>
    <div class="info-item">
      🏛️ {{ t("info_public_buildings") }}:
      <span>{{ counts.publicBuildings }}</span>
    </div>
    <div class="info-item">
      🌳 {{ t("info_public_spaces") }}:
      <span>{{ counts.publicSpaces }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../stores/appStore"

const { t } = useI18n()
const appStore = useAppStore()

const counts = computed(() => appStore.counts)

const cityCenterStatus = computed(() => {
  if (counts.value.cityCenter > 0) return t("info_status_placed")
  return "—"
})
</script>
