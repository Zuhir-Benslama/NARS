<template>
  <div class="fp-panel">
    <div class="fp-header">
      <h2 class="fp-title">{{ t("fp_title") }}</h2>
      <button v-if="selectedFeature" class="fp-back" @click="clearSelection">
        {{ t("fp_back") }}
      </button>
    </div>

    <!-- Feature type selector -->
    <div v-if="!selectedFeature" class="fp-tabs">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        :class="['fp-tab', { active: activeTab === tab.key }]"
        role="tab"
        :aria-selected="activeTab === tab.key"
        @click="activeTab = tab.key"
      >
        {{ tab.label }}
      </button>
    </div>

    <!-- Feature list (when no feature selected) -->
    <div v-if="!selectedFeature" class="fp-list-wrap">
      <div v-if="loading" class="fp-loading">{{ t("fp_loading") }}</div>
      <div v-else-if="features.length === 0" class="fp-empty">
        {{
          t("fp_empty_no_type", {
            type: tabs.find((tab) => tab.key === activeTab)?.label ?? activeTab,
          })
        }}
      </div>
      <div v-else class="fp-list">
        <button v-for="f in features" :key="f.id" class="fp-item" @click="selectFeature(f)">
          {{ f.label || t("fp_unnamed", { type: activeTab }) }}
        </button>
      </div>
    </div>

    <!-- Inspection form (when a feature is selected) -->
    <div v-else class="fp-form-wrap">
      <RoadInspectionForm
        v-if="selectedFeature.type === 'road'"
        :feature="selectedFeature"
        @done="clearSelection"
      />
      <EntranceInspectionForm
        v-else-if="selectedFeature.type === 'house_entrance'"
        :feature="selectedFeature"
        @done="clearSelection"
      />
      <NamingPanelInspectionForm
        v-else-if="selectedFeature.type === 'naming_panel'"
        :feature="selectedFeature"
        @done="clearSelection"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { currentLang, i18n } from "../i18n"
import { useFieldStore } from "../stores/fieldStore"
import { apiFetch } from "../api"
import { logError, createNetworkError } from "../lib/errors"
import RoadInspectionForm from "./inspection/RoadInspectionForm.vue"
import EntranceInspectionForm from "./inspection/EntranceInspectionForm.vue"
import NamingPanelInspectionForm from "./inspection/NamingPanelInspectionForm.vue"
import type { InspectionType } from "../types/inspection"

function localizedFeatureLabel(tab: string, f: unknown): string {
  const obj = (f ?? {}) as Record<string, unknown>
  const d = (obj.data ?? {}) as Record<string, unknown>
  const preferred =
    currentLang.value === "ar"
      ? (d.labelAr as string) || (d.label as string) || ""
      : currentLang.value === "fr"
        ? (d.labelFr as string) || (d.labelLatin as string) || (d.label as string) || ""
        : (d.labelEn as string) || (d.labelLatin as string) || (d.label as string) || ""
  const raw = (preferred || (obj.label as string) || "").trim()
  if (!raw) return "Unnamed"
  let typeLabel = ""
  if (tab === "road") {
    const key = d.roadTypeKey as string | undefined
    if (key) {
      const r = i18n.global.t(`featureTypes.${key}`)
      typeLabel = r && r !== `featureTypes.${key}` ? r : key
    } else {
      typeLabel = i18n.global.t("fp_tab_roads")
    }
  } else if (tab === "house_entrance") {
    typeLabel = i18n.global.t("fp_tab_entrances")
  } else if (tab === "naming_panel") {
    typeLabel = i18n.global.t("fp_tab_naming_panels")
  }
  return typeLabel ? `${typeLabel} ${raw}`.trim() : raw
}

interface TabDef {
  key: InspectionType
  label: string
  apiType: string
}

const { t } = useI18n()

const tabs = computed<TabDef[]>(() => [
  { key: "road", label: t("fp_tab_roads"), apiType: "road" },
  { key: "house_entrance", label: t("fp_tab_entrances"), apiType: "house_entrance" },
  { key: "naming_panel", label: t("fp_tab_naming_panels"), apiType: "naming_panel" },
])

interface ApiFeature {
  id: string
  label: string
  data?: Record<string, unknown>
}

export interface FieldPanelProps {
  fetchFeaturesFn?: (apiType: string) => Promise<ApiFeature[]>
}

const props = withDefaults(defineProps<FieldPanelProps>(), {
  fetchFeaturesFn: async (apiType: string) => {
    const res = await apiFetch(`/api/field/features?type=${apiType}`)
    const data = await res.json()
    return (data.features ?? []).map((f: Record<string, unknown>) => ({
      id: f.id,
      label: f.label || "Unnamed",
      data: f.data,
    }))
  },
})

const activeTab = ref<InspectionType>("road")
const features = ref<ApiFeature[]>([])
const loading = ref(false)
let _fetchController: AbortController | null = null

const fieldStore = useFieldStore()
const selectedFeature = computed(() => fieldStore.selectedFeature)

watch(currentLang, () => {
  fetchFeatures()
})

watch(activeTab, () => {
  fieldStore.clearSelection()
  fetchFeatures()
})

async function fetchFeatures() {
  _fetchController?.abort()
  const tab = tabs.value.find((t) => t.key === activeTab.value)
  if (!tab) return
  loading.value = true
  features.value = []
  const controller = new AbortController()
  _fetchController = controller
  try {
    const result = await props.fetchFeaturesFn(tab.apiType)
    if (!controller.signal.aborted) {
      features.value = result.map((f) => ({
        ...f,
        label: localizedFeatureLabel(tab.apiType, f as unknown),
      }))
    }
  } catch (err) {
    if (!controller.signal.aborted)
      logError(
        createNetworkError("Failed to load field features", { action: "fetchFeatures" }, err),
      )
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}

function selectFeature(f: ApiFeature) {
  fieldStore.selectFeature({ id: f.id, label: f.label, type: activeTab.value })
}

function clearSelection() {
  fieldStore.clearSelection()
  fetchFeatures()
}

onMounted(() => {
  fetchFeatures()
})
onUnmounted(() => {
  _fetchController?.abort()
})
</script>

<style scoped>
.fp-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 340px;
  height: 100vh;
  background: var(--modal-bg, #0f172a);
  border-left: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  z-index: 1000;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.fp-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px;
  border-bottom: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
}
.fp-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}
.fp-back {
  background: none;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  color: var(--text-secondary, #94a3b8);
  padding: 4px 10px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 12px;
}
.fp-tabs {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
}
.fp-tab {
  flex: 1;
  padding: 10px;
  border: none;
  background: transparent;
  color: var(--text-secondary, #94a3b8);
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
  border-bottom: 2px solid transparent;
}
.fp-tab.active {
  color: var(--accent, #3b82f6);
  border-bottom-color: var(--accent, #3b82f6);
}
.fp-list-wrap {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}
.fp-loading,
.fp-empty {
  padding: 20px;
  text-align: center;
  font-size: 13px;
  color: var(--text-secondary, #94a3b8);
}
.fp-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.fp-item {
  display: block;
  width: 100%;
  text-align: left;
  padding: 10px 12px;
  border: none;
  border-radius: 6px;
  background: var(--glass-bg, rgba(255, 255, 255, 0.03));
  color: var(--text-primary, #fff);
  font-size: 13px;
  cursor: pointer;
  transition: background 0.15s;
}
.fp-item:hover {
  background: var(--glass-bg-hover, rgba(255, 255, 255, 0.07));
}
.fp-form-wrap {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
}
</style>
