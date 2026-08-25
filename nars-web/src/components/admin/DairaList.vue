<template>
  <div class="daira-list">
    <details v-for="daira in dairas" :key="daira.daira_id" class="daira-block">
      <summary class="daira-header">
        <span class="daira-name">{{ daira.daira_name_fr }}</span>
        <span class="daira-name-ar">{{ daira.daira_name_ar }}</span>
        <span class="daira-admin-badge">
          <span v-if="daira.daira_admin" class="admin-assigned">
            {{ t("admin.daira_admin") }}: {{ daira.daira_admin.name }}
          </span>
          <span v-else class="admin-missing">{{ t("admin.no_daira_admin") }}</span>
        </span>
        <span class="commune-count">{{ daira.communes.length }} {{ t("admin.communes") }}</span>
      </summary>
      <div class="daira-body">
        <CommuneList :communes="daira.communes" />
      </div>
    </details>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from "vue-i18n"
import type { DairaReport } from "../../types"
import CommuneList from "./CommuneList.vue"
defineProps<{ dairas: DairaReport[] }>()
const { t } = useI18n()
</script>

<style scoped>
.daira-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.daira-block {
  border: 1px solid var(--glass-border);
  border-radius: 8px;
  overflow: hidden;
}
.daira-block[open] > .daira-header {
  background: var(--glass-bg-hover);
}
.daira-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.7rem 1rem;
  background: var(--glass-bg);
  cursor: pointer;
  list-style: none;
  user-select: none;
}
.daira-header::-webkit-details-marker {
  display: none;
}
.daira-header::before {
  content: "▶";
  font-size: 0.65rem;
  color: var(--text-primary);
  transition: transform 0.2s;
}
.daira-block[open] > .daira-header::before {
  transform: rotate(90deg);
}
.daira-name {
  font-weight: 600;
  font-size: 1rem;
  color: var(--text-primary);
}
.daira-name-ar {
  font-size: 0.85rem;
  color: var(--text-secondary);
  direction: rtl;
  flex: 1;
  text-align: right;
}
.daira-admin-badge {
  font-size: 0.78rem;
  white-space: nowrap;
}
.admin-assigned {
  background: var(--success-bg);
  color: var(--success-color);
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
}
.admin-missing {
  background: var(--danger-bg);
  color: var(--danger-color);
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
  font-style: italic;
}
.commune-count {
  font-size: 0.78rem;
  background: var(--glass-bg);
  color: var(--text-primary);
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  white-space: nowrap;
}
.daira-body {
  padding: 0.75rem;
}
</style>
