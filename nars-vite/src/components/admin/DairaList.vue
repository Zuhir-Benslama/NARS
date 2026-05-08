<template>
  <div class="daira-list">
    <details v-for="daira in dairas" :key="daira.daira_id" class="daira-block" open>
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
import type { DairaReport, UserRole } from "../../types"
import CommuneList from "./CommuneList.vue"
defineProps<{ dairas: DairaReport[]; role: UserRole }>()
const { t } = useI18n()
</script>

<style scoped>
.daira-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.daira-block {
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  overflow: hidden;
}
.daira-block[open] > .daira-header {
  background: #e8f0fe;
}
.daira-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.7rem 1rem;
  background: #f0f4ff;
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
  color: #1976d2;
  transition: transform 0.2s;
}
.daira-block[open] > .daira-header::before {
  transform: rotate(90deg);
}
.daira-name {
  font-weight: 600;
  font-size: 1rem;
  color: #1a237e;
}
.daira-name-ar {
  font-size: 0.85rem;
  color: #555;
  direction: rtl;
  flex: 1;
  text-align: right;
}
.daira-admin-badge {
  font-size: 0.78rem;
  white-space: nowrap;
}
.admin-assigned {
  background: #e8f5e9;
  color: #1b5e20;
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
}
.admin-missing {
  background: #ffebee;
  color: #c62828;
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
  font-style: italic;
}
.commune-count {
  font-size: 0.78rem;
  background: #e3f2fd;
  color: #0d47a1;
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  white-space: nowrap;
}
.daira-body {
  padding: 0.75rem;
}
</style>
