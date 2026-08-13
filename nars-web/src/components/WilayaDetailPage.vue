<template>
  <div class="wilaya-detail-page">
    <div v-if="loading" class="loading-state">
      <span class="spinner" />
      {{ t("admin.loading") }}
    </div>

    <div v-else-if="error" class="admin-error">{{ error }}</div>

    <template v-else-if="wilaya">
      <div class="detail-header">
        <button class="back-btn" @click="goBack">← {{ t("admin.back") }}</button>
        <h1>{{ wilaya.wilaya_name_fr }} — {{ wilaya.wilaya_name_ar }}</h1>
      </div>
      <div class="content-scroll">
        <DairaList :dairas="wilaya.dairas" />
      </div>
    </template>

    <div v-else class="admin-empty">{{ t("admin.no_data") }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from "vue"
import { useRoute, useRouter } from "vue-router"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../api"
import { useAppStore } from "../stores/appStore"
import { getUserMessageKey } from "../lib/errors"
import { slugify } from "../utils/string"
import type { NationalOverview, WilayaReport, UserRole } from "../types"
import DairaList from "./admin/DairaList.vue"

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const appStore = useAppStore()

const wilaya = ref<WilayaReport | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
let abortController: AbortController | null = null

const userRole = computed<UserRole>(() => appStore.user?.role ?? "commune_user")

function goBack() {
  router.push("/admin")
}

async function load() {
  const wilayaNameSlug = route.params.wilayaName as string
  loading.value = true
  error.value = null
  wilaya.value = null
  abortController?.abort()
  abortController = new AbortController()
  const { signal } = abortController

  try {
    const res = await apiFetch("/api/admin/overview", { signal })
    if (signal.aborted) return
    const data = await res.json()

    let wilayaId: number | null = null

    if (userRole.value === "national_admin") {
      const overview = data as NationalOverview
      const match = overview.wilayas.find((w) => slugify(w.wilaya_name_fr) === wilayaNameSlug)
      wilayaId = match?.wilaya_id ?? null
    } else if (userRole.value === "wilaya_admin") {
      const report = data as WilayaReport
      if (slugify(report.wilaya_name_fr) === wilayaNameSlug) {
        wilaya.value = report
      } else {
        error.value = t("admin.wilaya_not_found")
      }
      return
    } else {
      error.value = t("admin.access_denied")
      return
    }

    if (wilayaId === null) {
      error.value = t("admin.wilaya_not_found")
      return
    }

    const detailRes = await apiFetch(`/api/admin/wilaya/${wilayaId}`, { signal })
    if (signal.aborted) return
    wilaya.value = (await detailRes.json()) as WilayaReport
  } catch (err) {
    if (signal.aborted) return
    error.value = t(getUserMessageKey(err))
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

// Re-run when navigating between /nars/<wilaya1> and /nars/<wilaya2>: the
// component instance is reused, so a mount-only load would show stale data.
watch(() => route.params.wilayaName, load, { immediate: true })

onUnmounted(() => abortController?.abort())
</script>

<style scoped>
.wilaya-detail-page {
  padding: 1.5rem;
  max-width: 1100px;
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
.detail-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
}
.detail-header h1 {
  font-size: 1.3rem;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}
.back-btn {
  font-size: 0.8rem;
  padding: 0.35rem 0.75rem;
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
.loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.6rem;
  padding: 3rem;
  color: var(--text-secondary);
  font-size: 0.95rem;
}
.spinner {
  width: 18px;
  height: 18px;
  border: 2px solid var(--spinner-track);
  border-top-color: var(--spinner-fill);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
  display: inline-block;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
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
</style>
