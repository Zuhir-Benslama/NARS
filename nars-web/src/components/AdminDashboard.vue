<template>
  <div class="admin-dashboard">
    <!-- Header -->
    <div class="admin-header">
      <div class="admin-title">
        <span class="role-badge" :class="roleBadgeClass">{{ roleLabel }}</span>
        <h1>{{ t("admin.dashboard") }}</h1>
      </div>
    </div>

    <div v-if="error" class="admin-error">{{ error }}</div>

    <!-- NATIONAL ADMIN: wilaya summary cards -->
    <template v-if="isNational && nationalData">
      <div class="section-title">
        {{ t("admin.all_wilayas") }} ({{ nationalData.wilayas.length }})
      </div>
      <div class="content-scroll">
        <div class="wilaya-grid">
          <router-link
            v-for="w in nationalData.wilayas"
            :key="w.wilaya_id"
            :to="`/nars/${slugify(w.wilaya_name_fr)}`"
            class="wilaya-card"
          >
            <div class="wilaya-card-head">
              <span class="wilaya-name">{{ w.wilaya_name_fr }}</span>
              <span class="wilaya-name-ar">{{ w.wilaya_name_ar }}</span>
            </div>
            <div class="wilaya-stats">
              <StatPill :label="t('admin.dairas')" :value="w.daira_count" />
              <StatPill :label="t('admin.communes')" :value="w.commune_count" />
              <StatPill :label="t('admin.users')" :value="w.commune_user_count" color="blue" />
            </div>
            <div class="wilaya-admin-row">
              <span class="admin-label">{{ t("admin.wilaya_admin") }}:</span>
              <span v-if="w.wilaya_admin" class="admin-name">{{ w.wilaya_admin.name }}</span>
              <span v-else class="admin-missing">{{ t("admin.none_assigned") }}</span>
            </div>
            <button class="drill-btn">{{ t("admin.view_detail") }} →</button>
          </router-link>
        </div>
      </div>
    </template>

    <!-- WILAYA ADMIN: daira cards grid -->
    <template v-else-if="isWilaya && wilayaData && !selectedDaira">
      <div class="section-title">
        {{ wilayaData.wilaya_name_fr }} — {{ wilayaData.wilaya_name_ar }}
      </div>
      <div class="content-scroll">
        <div class="wilaya-grid">
          <div
            v-for="d in wilayaData.dairas"
            :key="d.daira_id"
            class="wilaya-card"
            @click="selectedDaira = d"
          >
            <div class="wilaya-card-head">
              <span class="wilaya-name">{{ d.daira_name_fr }}</span>
              <span class="wilaya-name-ar">{{ d.daira_name_ar }}</span>
            </div>
            <div class="wilaya-stats">
              <StatPill :label="t('admin.communes')" :value="d.communes.length" />
              <StatPill
                :label="t('admin.users')"
                :value="d.communes.reduce((sum, c) => sum + c.users.length, 0)"
                color="blue"
              />
            </div>
            <div class="wilaya-admin-row">
              <span class="admin-label">{{ t("admin.daira_admin") }}:</span>
              <span v-if="d.daira_admin" class="admin-name">{{ d.daira_admin.name }}</span>
              <span v-else class="admin-missing">{{ t("admin.none_assigned") }}</span>
            </div>
            <button class="drill-btn">{{ t("admin.view_detail") }} →</button>
          </div>
        </div>
      </div>
    </template>

    <!-- WILAYA ADMIN: single daira detail -->
    <template v-else-if="isWilaya && wilayaData && selectedDaira">
      <div class="section-title">
        <button class="back-btn" @click="selectedDaira = null">← {{ t("admin.back") }}</button>
        {{ selectedDaira.daira_name_fr }} — {{ selectedDaira.daira_name_ar }}
      </div>
      <div class="content-scroll">
        <CommuneList :communes="selectedDaira.communes" />
      </div>
    </template>

    <!-- DAIRA ADMIN: commune cards grid -->
    <template v-else-if="isDaira && dairaData && !selectedCommune">
      <div class="section-title">{{ dairaData.daira_name_fr }} — {{ dairaData.daira_name_ar }}</div>
      <div class="content-scroll">
        <div class="wilaya-grid">
          <div
            v-for="c in dairaData.communes"
            :key="c.commune_id"
            class="wilaya-card"
            @click="selectedCommune = c"
          >
            <div class="wilaya-card-head">
              <span class="wilaya-name">{{ c.commune_name_fr }}</span>
              <span class="wilaya-name-ar">{{ c.commune_name_ar }}</span>
            </div>
            <div class="wilaya-stats">
              <StatPill :label="t('admin.users')" :value="c.users.length" color="blue" />
            </div>
            <button class="drill-btn">{{ t("admin.view_detail") }} →</button>
          </div>
        </div>
      </div>
    </template>

    <!-- DAIRA ADMIN: single commune detail -->
    <template v-else-if="isDaira && dairaData && selectedCommune">
      <div class="section-title">
        <button class="back-btn" @click="selectedCommune = null">← {{ t("admin.back") }}</button>
        {{ selectedCommune.commune_name_fr }} — {{ selectedCommune.commune_name_ar }}
      </div>
      <div class="content-scroll">
        <CommuneList :communes="[selectedCommune]" />
      </div>
    </template>

    <div v-if="!loading && !error && !hasData" class="admin-empty">
      {{ t("admin.no_data") }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../api"
import { useAppStore } from "../stores/appStore"
import { slugify } from "../utils/string"
import type { NationalOverview, WilayaReport, DairaReport, CommuneReport, UserRole } from "../types"
import StatPill from "./admin/StatPill.vue"
import CommuneList from "./admin/CommuneList.vue"

const { t } = useI18n()
const appStore = useAppStore()

const loading = ref(false)
const error = ref<string | null>(null)

const nationalData = ref<NationalOverview | null>(null)
const wilayaData = ref<WilayaReport | null>(null)
const dairaData = ref<DairaReport | null>(null)
const selectedDaira = ref<DairaReport | null>(null)
const selectedCommune = ref<CommuneReport | null>(null)
const userRole = computed<UserRole>(() => appStore.user?.role ?? "commune_user")
const isNational = computed(() => userRole.value === "national_admin")
const isWilaya = computed(() => userRole.value === "wilaya_admin")
const isDaira = computed(() => userRole.value === "daira_admin")
const hasData = computed(
  () => nationalData.value !== null || wilayaData.value !== null || dairaData.value !== null,
)

const roleLabel = computed(
  () =>
    ({
      national_admin: t("admin.role.national"),
      wilaya_admin: t("admin.role.wilaya"),
      daira_admin: t("admin.role.daira"),
      commune_user: t("admin.role.commune"),
      field_worker: t("admin.role.field_worker"),
    })[userRole.value] ?? userRole.value,
)

const roleBadgeClass = computed(
  () =>
    ({
      national_admin: "badge-national",
      wilaya_admin: "badge-wilaya",
      daira_admin: "badge-daira",
      commune_user: "badge-commune",
      field_worker: "badge-commune",
    })[userRole.value] ?? "",
)

async function loadOverview() {
  loading.value = true
  error.value = null
  nationalData.value = null
  wilayaData.value = null
  dairaData.value = null

  try {
    const res = await apiFetch("/api/admin/overview")
    const data = await res.json()

    if (isNational.value) nationalData.value = data as NationalOverview
    else if (isWilaya.value) wilayaData.value = data as WilayaReport
    else if (isDaira.value) dairaData.value = data as DairaReport
  } catch (err) {
    error.value = err instanceof Error ? err.message : t("admin.load_error")
  } finally {
    loading.value = false
  }
}

onMounted(loadOverview)
</script>

<style scoped>
.admin-dashboard {
  padding: 1.5rem;
  max-width: 1400px;
  margin: 0 auto;
  font-family: var(--font-sans, sans-serif);
  height: 100dvh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
}
.content-scroll {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}
.admin-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1.5rem;
}
.admin-title {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.admin-title h1 {
  font-size: 1.4rem;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}
.role-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
}
.badge-national {
  background: #0d47a1;
  color: #fff;
}
.badge-wilaya {
  background: #1565c0;
  color: #fff;
}
.badge-daira {
  background: #1976d2;
  color: #fff;
}
.badge-commune {
  background: #64b5f6;
  color: #0d47a1;
}
.admin-error {
  background: #ffebee;
  border: 1px solid #ef9a9a;
  border-radius: 6px;
  padding: 0.75rem 1rem;
  color: #c62828;
  margin-bottom: 1rem;
  font-size: 0.875rem;
}
.admin-empty {
  text-align: center;
  color: var(--text-muted);
  padding: 2rem;
  font-size: 0.95rem;
}
.section-title {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 1.25rem 0 0.75rem;
  border-bottom: 2px solid #1976d2;
  padding-bottom: 0.4rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.wilaya-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}
.wilaya-card {
  background: var(--glass-bg);
  border: 1px solid var(--glass-border);
  border-radius: 10px;
  padding: 1rem;
  cursor: pointer;
  transition: box-shadow 0.15s;
}
.wilaya-card:hover {
  box-shadow: var(--glass-shadow);
  border-color: var(--glass-border);
}
.wilaya-card-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 0.6rem;
}
.wilaya-name {
  font-weight: 600;
  font-size: 1rem;
  color: var(--text-primary);
}
.wilaya-name-ar {
  font-size: 0.85rem;
  color: var(--text-secondary);
  direction: rtl;
}
.wilaya-stats {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.6rem;
  flex-wrap: wrap;
}
.wilaya-admin-row {
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
  color: #e53935;
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
.back-btn {
  padding: 0.35rem 0.7rem;
  font-size: 0.8rem;
  border: 1px solid var(--glass-border);
  color: var(--text-primary);
  border-radius: 5px;
  background: var(--glass-bg);
  cursor: pointer;
  white-space: nowrap;
}
.back-btn:hover {
  background: var(--glass-bg-hover);
}
</style>
