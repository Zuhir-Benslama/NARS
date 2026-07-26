<script setup lang="ts">
import { useI18n } from "vue-i18n"
import StatPill from "./StatPill.vue"

const { t } = useI18n()

export interface CardStat {
  label: string
  value: number
  color?: "default" | "blue" | "green"
}

defineProps<{
  nameFr: string
  nameAr: string
  stats: CardStat[]
  adminLabel?: string
  adminName?: string | null
}>()

defineEmits<{
  drill: []
}>()
</script>

<template>
  <div class="entity-card" @click="$emit('drill')">
    <div class="entity-card-head">
      <span class="entity-name">{{ nameFr }}</span>
      <span class="entity-name-ar">{{ nameAr }}</span>
    </div>
    <div class="entity-stats">
      <StatPill
        v-for="s in stats"
        :key="s.label"
        :label="s.label"
        :value="s.value"
        :color="s.color"
      />
    </div>
    <div v-if="adminLabel" class="entity-admin-row">
      <span class="admin-label">{{ adminLabel }}:</span>
      <span v-if="adminName" class="admin-name">{{ adminName }}</span>
      <span v-else class="admin-missing">{{ t("admin.none_assigned") }}</span>
    </div>
    <button class="drill-btn">{{ t("admin.view_detail") }} →</button>
  </div>
</template>

<style scoped>
.entity-card {
  background: var(--glass-bg);
  border: 1px solid var(--glass-border);
  border-radius: 10px;
  padding: 1rem;
  cursor: pointer;
  transition: box-shadow 0.15s;
}
.entity-card:hover {
  box-shadow: var(--glass-shadow);
  border-color: var(--glass-border);
}
.entity-card-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 0.6rem;
}
.entity-name {
  font-weight: 600;
  font-size: 1rem;
  color: var(--text-primary);
}
.entity-name-ar {
  font-size: 0.85rem;
  color: var(--text-secondary);
  direction: rtl;
}
.entity-stats {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.6rem;
  flex-wrap: wrap;
}
.entity-admin-row {
  font-size: 0.8rem;
  color: var(--text-secondary);
  margin-bottom: 0.6rem;
}
.admin-label {
  font-weight: 600;
  margin-right: 0.3rem;
}
.admin-name {
  color: var(--text-primary);
}
.admin-missing {
  color: var(--danger-color);
  font-style: italic;
}
.drill-btn {
  width: 100%;
  padding: 0.35rem;
  font-size: 0.8rem;
  border: 1px solid var(--glass-border);
  color: var(--text-primary);
  border-radius: 5px;
  background: var(--glass-bg);
  cursor: pointer;
}
.drill-btn:hover {
  background: var(--glass-bg-hover);
}
</style>
