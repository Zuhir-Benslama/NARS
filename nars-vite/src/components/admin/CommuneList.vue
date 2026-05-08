<template>
  <div class="commune-list">
    <div v-for="commune in communes" :key="commune.commune_id" class="commune-block">
      <div class="commune-header">
        <span class="commune-name">{{ commune.commune_name_fr }}</span>
        <span class="commune-name-ar">{{ commune.commune_name_ar }}</span>
        <span class="user-count">{{ commune.users.length }} {{ t("admin.users") }}</span>
      </div>

      <div v-if="commune.users.length === 0" class="no-users">
        {{ t("admin.no_users") }}
      </div>

      <table v-else class="stats-table">
        <thead>
          <tr>
            <th>{{ t("admin.user") }}</th>
            <th>{{ t("admin.areas") }}</th>
            <th>{{ t("admin.districts") }}</th>
            <th>{{ t("admin.city_center") }}</th>
            <th>{{ t("admin.roads") }}</th>
            <th>{{ t("admin.entrances") }}</th>
            <th>{{ t("admin.pub_bldg") }}</th>
            <th>{{ t("admin.pub_space") }}</th>
            <th>{{ t("admin.panels") }}</th>
            <th class="total-col">{{ t("admin.total") }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in commune.users" :key="u.user_id" :class="{ 'row-complete': u.total > 0 }">
            <td class="user-cell">
              <span class="uname">{{ u.username }}</span>
              <span class="uname-full">{{ u.name }}</span>
            </td>
            <td>{{ u.areas }}</td>
            <td>{{ u.districts }}</td>
            <td>{{ u.city_centers }}</td>
            <td>{{ u.roads }}</td>
            <td>{{ u.house_entrances }}</td>
            <td>{{ u.public_buildings }}</td>
            <td>{{ u.public_spaces }}</td>
            <td>{{ u.naming_panels }}</td>
            <td class="total-col">{{ u.total }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from "vue-i18n"
import type { CommuneReport } from "../../types"
defineProps<{ communes: CommuneReport[] }>()
const { t } = useI18n()
</script>

<style scoped>
.commune-list {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}
.commune-block {
  background: #fff;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  overflow: hidden;
}
.commune-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.65rem 1rem;
  background: #f8f9fa;
  border-bottom: 1px solid #e0e0e0;
}
.commune-name {
  font-weight: 600;
  font-size: 0.95rem;
  color: #111;
}
.commune-name-ar {
  font-size: 0.85rem;
  color: #555;
  direction: rtl;
  flex: 1;
  text-align: right;
}
.user-count {
  font-size: 0.78rem;
  background: #e3f2fd;
  color: #0d47a1;
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  white-space: nowrap;
}
.no-users {
  padding: 0.75rem 1rem;
  font-size: 0.85rem;
  color: #888;
  font-style: italic;
}
.stats-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}
.stats-table th {
  background: #1976d2;
  color: #fff;
  font-weight: 500;
  padding: 0.4rem 0.6rem;
  text-align: center;
  white-space: nowrap;
}
.stats-table th:first-child {
  text-align: left;
}
.stats-table td {
  padding: 0.4rem 0.6rem;
  text-align: center;
  border-bottom: 1px solid #f0f0f0;
  color: #333;
}
.stats-table td:first-child {
  text-align: left;
}
.stats-table tbody tr:hover {
  background: #f5f9ff;
}
.stats-table tbody tr:last-child td {
  border-bottom: none;
}
.row-complete td {
  color: #1b5e20;
}
.total-col {
  font-weight: 700;
  background: #fafafa;
}
.user-cell {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.uname {
  font-weight: 600;
}
.uname-full {
  font-size: 0.75rem;
  color: #666;
}
</style>
