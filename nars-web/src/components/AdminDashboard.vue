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
            class="entity-link"
          >
            <EntityCard
              :name-fr="w.wilaya_name_fr"
              :name-ar="w.wilaya_name_ar"
              :stats="[
                { label: t('admin.dairas'), value: w.daira_count },
                { label: t('admin.communes'), value: w.commune_count },
                { label: t('admin.users'), value: w.commune_user_count, color: 'blue' },
              ]"
              :admin-label="t('admin.wilaya_admin')"
              :admin-name="w.wilaya_admin?.name"
            />
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
          <EntityCard
            v-for="d in wilayaData.dairas"
            :key="d.daira_id"
            :name-fr="d.daira_name_fr"
            :name-ar="d.daira_name_ar"
            :stats="[
              { label: t('admin.communes'), value: d.communes.length },
              {
                label: t('admin.users'),
                value: d.communes.reduce((sum, c) => sum + c.users.length, 0),
                color: 'blue',
              },
            ]"
            :admin-label="t('admin.daira_admin')"
            :admin-name="d.daira_admin?.name"
            @drill="selectedDaira = d"
          />
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
          <EntityCard
            v-for="c in dairaData.communes"
            :key="c.commune_id"
            :name-fr="c.commune_name_fr"
            :name-ar="c.commune_name_ar"
            :stats="[{ label: t('admin.users'), value: c.users.length, color: 'blue' }]"
            @drill="selectedCommune = c"
          />
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
import { ref, computed, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../api"
import { useAppStore } from "../stores/appStore"
import { slugify } from "../utils/string"
import type { NationalOverview, WilayaReport, DairaReport, CommuneReport, UserRole } from "../types"
import CommuneList from "./admin/CommuneList.vue"
import EntityCard from "./admin/EntityCard.vue"

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

const ROLE_LABELS: Record<string, string> = {
  national_admin: "admin.role.national",
  wilaya_admin: "admin.role.wilaya",
  daira_admin: "admin.role.daira",
  commune_user: "admin.role.commune",
  field_worker: "admin.role.field_worker",
}

const ROLE_BADGES: Record<string, string> = {
  national_admin: "badge-national",
  wilaya_admin: "badge-wilaya",
  daira_admin: "badge-daira",
  commune_user: "badge-commune",
  field_worker: "badge-commune",
}

const roleLabel = computed(() => {
  const key = ROLE_LABELS[userRole.value]
  return key ? t(key) : userRole.value
})

const roleBadgeClass = computed(() => ROLE_BADGES[userRole.value] ?? "")

let abortCtrl: AbortController | null = null

async function loadOverview() {
  loading.value = true
  error.value = null
  nationalData.value = null
  wilayaData.value = null
  dairaData.value = null

  abortCtrl?.abort()
  abortCtrl = new AbortController()
  const { signal } = abortCtrl

  try {
    const res = await apiFetch("/api/admin/overview", { signal })
    const data = await res.json()

    if (signal.aborted) return
    if (isNational.value) nationalData.value = data as NationalOverview
    else if (isWilaya.value) wilayaData.value = data as WilayaReport
    else if (isDaira.value) dairaData.value = data as DairaReport
  } catch (err) {
    if (signal.aborted) return
    error.value = err instanceof Error ? err.message : t("admin.load_error")
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

onMounted(loadOverview)
onUnmounted(() => abortCtrl?.abort())
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
  color: var(--text-primary);
}
.badge-wilaya {
  background: #1565c0;
  color: var(--text-primary);
}
.badge-daira {
  background: var(--accent-color);
  color: var(--text-primary);
}
.badge-commune {
  background: #64b5f6;
  color: #0d47a1;
}
.admin-error {
  background: var(--danger-bg);
  border: 1px solid var(--danger-border);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  color: var(--danger-color);
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
  border-bottom: 2px solid var(--accent-color);
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
.entity-link {
  text-decoration: none;
  color: inherit;
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
