<template>
  <div
    v-if="visible"
    ref="modalRef"
    class="modal"
    role="dialog"
    aria-modal="true"
    aria-labelledby="settingsHeader"
  >
    <div class="modal-content settings-modal">
      <div id="settingsHeader" class="modal-header">
        {{ t("settings_title") }}
      </div>

      <div class="settings-container">
        <!-- Sidebar -->
        <div class="settings-sidebar" role="tablist" @keydown="onTabKeydown">
          <button
            v-for="tab in tabs"
            :id="'settings-tab-' + tab.id"
            :key="tab.id"
            role="tab"
            :aria-selected="activeTab === tab.id"
            :aria-controls="'settings-panel-' + tab.id"
            :tabindex="activeTab === tab.id ? 0 : -1"
            :class="['sidebar-tab', { active: activeTab === tab.id }]"
            @click="activeTab = tab.id"
          >
            {{ tab.icon }} {{ t(tab.tKey) }}
          </button>
        </div>

        <!-- Active tab panel -->
        <div
          :id="'settings-panel-' + activeTab"
          class="settings-main"
          role="tabpanel"
          :aria-labelledby="'settings-tab-' + activeTab"
        >
          <SettingsGeneral v-if="activeTab === 'general'" />
          <SettingsAccount v-if="activeTab === 'account'" :visible="visible" />
          <SettingsUsers v-if="activeTab === 'users'" />
          <SettingsFeatures v-if="activeTab === 'features'" />
          <SettingsAbout v-if="activeTab === 'about'" />
        </div>
      </div>

      <div class="modal-buttons">
        <button class="modal-btn modal-btn-cancel" @click="$emit('close')">
          {{ t("btn_close") }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue"
import { useFocusTrap } from "../composables/useFocusTrap"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../stores/appStore"
import SettingsGeneral from "./settings/SettingsGeneral.vue"
import SettingsAccount from "./settings/SettingsAccount.vue"
import SettingsUsers from "./settings/SettingsUsers.vue"
import SettingsFeatures from "./settings/SettingsFeatures.vue"
import SettingsAbout from "./settings/SettingsAbout.vue"

const props = defineProps<{ visible: boolean }>()
defineEmits(["close"])

const modalRef = ref<HTMLElement | null>(null)
useFocusTrap(modalRef, () => props.visible)

const { t } = useI18n()
const appStore = useAppStore()

const activeTab = ref("general")

// Users tab follows the server's UserManagementRoles (includes commune_user,
// which may create field_worker accounts for its own commune).
const canManageUsers = computed(() => appStore.canManageUsers)

const baseTabs = [
  { id: "general", tKey: "tab_general", icon: "⚙️" },
  { id: "account", tKey: "tab_account", icon: "👤" },
  { id: "features", tKey: "tab_features", icon: "⬟" },
  { id: "about", tKey: "tab_about", icon: "ℹ️" },
]
const adminTab = { id: "users", tKey: "tab_users", icon: "👥" }

const tabs = computed(() =>
  canManageUsers.value ? [baseTabs[0], baseTabs[1], adminTab, baseTabs[2], baseTabs[3]] : baseTabs,
)

function onTabKeydown(e: KeyboardEvent) {
  const idx = tabs.value.findIndex((t) => t.id === activeTab.value)
  if (e.key === "ArrowRight" || e.key === "ArrowDown") {
    e.preventDefault()
    const next = (idx + 1) % tabs.value.length
    activeTab.value = tabs.value[next].id
    document.getElementById("settings-tab-" + activeTab.value)?.focus()
  } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
    e.preventDefault()
    const prev = (idx - 1 + tabs.value.length) % tabs.value.length
    activeTab.value = tabs.value[prev].id
    document.getElementById("settings-tab-" + activeTab.value)?.focus()
  }
  // Tab is intentionally left untouched so the browser's default order can
  // move focus out of the tablist into the panel content / close button.
}
</script>

<style scoped>
.modal {
  position: fixed;
  z-index: 10000;
  left: 0;
  top: 0;
  width: 100%;
  height: 100%;
  background: var(--overlay-bg);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-content {
  width: 620px;
  max-width: 95vw;
  max-height: 88vh;
  display: flex;
  flex-direction: column;
  background: var(--modal-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid var(--modal-border);
  box-shadow: var(--modal-shadow);
  border-radius: 15px;
  overflow: hidden;
  color: var(--modal-text);
  font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
}

.modal-header {
  padding: 20px 24px 0;
  font-size: 18px;
  font-weight: 700;
  color: var(--text-primary);
  flex-shrink: 0;
}

.settings-container {
  display: flex;
  flex: 1;
  overflow: hidden;
  margin-top: 16px;
}

.settings-sidebar {
  width: 140px;
  flex-shrink: 0;
  border-right: 1px solid var(--glass-border);
  display: flex;
  flex-direction: column;
  padding: 8px 0;
}

.sidebar-tab {
  padding: 11px 16px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  color: var(--text-secondary);
  border: none;
  background: none;
  text-align: left;
  transition: all 0.2s;
  font-family: inherit;
  border-left: 3px solid transparent;
}
.sidebar-tab:hover {
  background: var(--glass-bg-hover);
  color: var(--text-primary);
}
.sidebar-tab.active {
  color: var(--text-primary);
  background: rgba(255, 255, 255, 0.08);
  border-left-color: rgba(255, 255, 255, 0.7);
}

.settings-main {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
}

.modal-buttons {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 24px;
  border-top: 1px solid var(--glass-border);
  flex-shrink: 0;
}

/* Shared styles used by tab sub-components via :deep() */
:deep(.settings-hint) {
  font-size: 13px;
  color: var(--text-secondary);
  margin-bottom: 16px;
  line-height: 1.5;
}
:deep(.theme-switcher) {
  display: flex;
  gap: 8px;
  margin-top: 4px;
}
:deep(.theme-btn) {
  flex: 1;
  padding: 9px 14px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  font-family: inherit;
  background: var(--input-bg);
  color: var(--text-secondary);
  border: 1px solid var(--input-border);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
}
:deep(.theme-btn:hover) {
  background: var(--glass-bg-hover);
  color: var(--text-primary);
}
:deep(.theme-btn.selected) {
  background: rgba(255, 255, 255, 0.22);
  color: var(--text-primary);
  border-color: rgba(255, 255, 255, 0.55);
}

:deep(.about-panel h3) {
  font-size: 18px;
  font-weight: 700;
  color: var(--text-primary);
  margin-bottom: 8px;
}
:deep(.about-panel p) {
  font-size: 13px;
  color: var(--text-secondary);
  margin-bottom: 8px;
  line-height: 1.6;
}
:deep(.about-panel .version) {
  font-weight: 600;
  color: var(--text-primary);
  font-size: 14px;
}
:deep(.about-panel hr) {
  border: none;
  border-top: 1px solid var(--glass-border);
  margin: 12px 0;
}
</style>
