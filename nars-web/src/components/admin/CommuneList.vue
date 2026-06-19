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
              <span v-if="isFieldWorker(u)" class="fw-badge">FW</span>
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
        <tfoot v-if="commune.users.length > 1">
          <tr class="totals-row">
            <td class="totals-label">{{ t("admin.totals") }}</td>
            <td>{{ sum(commune.users, "areas") }}</td>
            <td>{{ sum(commune.users, "districts") }}</td>
            <td>{{ sum(commune.users, "city_centers") }}</td>
            <td>{{ sum(commune.users, "roads") }}</td>
            <td>{{ sum(commune.users, "house_entrances") }}</td>
            <td>{{ sum(commune.users, "public_buildings") }}</td>
            <td>{{ sum(commune.users, "public_spaces") }}</td>
            <td>{{ sum(commune.users, "naming_panels") }}</td>
            <td class="total-col">{{ sum(commune.users, "total") }}</td>
          </tr>
        </tfoot>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from "vue-i18n"
import type { CommuneReport, UserFeatureStats } from "../../types"
defineProps<{ communes: CommuneReport[] }>()
const { t } = useI18n()

function isFieldWorker(u: UserFeatureStats): boolean {
  return u.role === "field_worker"
}

function sum(users: UserFeatureStats[], field: keyof UserFeatureStats): number {
  return users.reduce(
    (acc, u) => acc + (typeof u[field] === "number" ? (u[field] as number) : 0),
    0,
  )
}
</script>

<style scoped>
.commune-list {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}
.commune-block {
  background: var(--glass-bg);
  border: 1px solid var(--glass-border);
  border-radius: 8px;
  overflow: hidden;
}
.commune-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.65rem 1rem;
  background: var(--glass-bg-hover);
  border-bottom: 1px solid var(--glass-border);
}
.commune-name {
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--text-primary);
}
.commune-name-ar {
  font-size: 0.85rem;
  color: var(--text-secondary);
  direction: rtl;
  flex: 1;
  text-align: right;
}
.user-count {
  font-size: 0.78rem;
  background: var(--glass-bg);
  color: var(--text-primary);
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  white-space: nowrap;
}
.no-users {
  padding: 0.75rem 1rem;
  font-size: 0.85rem;
  color: var(--text-muted);
  font-style: italic;
}
.stats-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}
.stats-table th {
  background: rgba(25, 118, 210, 0.2);
  color: var(--text-primary);
  font-weight: 600;
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
  border-bottom: 1px solid var(--glass-border);
  color: var(--text-primary);
}
.stats-table td:first-child {
  text-align: left;
}
.stats-table tbody tr:hover {
  background: var(--glass-bg-hover);
}
.stats-table tbody tr:last-child td {
  border-bottom: none;
}
.row-complete td {
  color: #4caf50;
}
.total-col {
  font-weight: 700;
  background: var(--glass-bg);
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
  color: var(--text-secondary);
}
.fw-badge {
  display: inline-block;
  font-size: 0.6rem;
  font-weight: 700;
  background: #ff9800;
  color: #fff;
  border-radius: 3px;
  padding: 0.05rem 0.35rem;
  margin-left: 0.3rem;
  vertical-align: middle;
  line-height: 1.3;
}
.totals-row td {
  font-weight: 700;
  border-top: 2px solid var(--glass-border);
  background: var(--glass-bg-hover);
  color: var(--text-primary);
}
.totals-label {
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
</style>
