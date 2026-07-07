<template>
  <div v-click-outside="closeDropdown" class="profile-menu">
    <div
      :class="['profile-button', { open: dropdownOpen }]"
      role="button"
      tabindex="0"
      :aria-expanded="dropdownOpen"
      aria-haspopup="true"
      @click="toggleDropdown"
      @keydown.enter="toggleDropdown"
      @keydown.space.prevent="toggleDropdown"
    >
      <div class="profile-icon">
        {{ initials }}
      </div>
      <div class="profile-info">
        <div class="profile-username">
          {{ username }}
        </div>
        <div class="profile-name">
          {{ name }}
        </div>
      </div>
      <span :class="['dropdown-arrow', { open: dropdownOpen }]">▼</span>
    </div>
    <div :class="['profile-dropdown', { show: dropdownOpen }]" role="menu">
      <div class="dropdown-item" role="menuitem" @click="onSettings">
        <span>⚙️</span>
        <span>{{ t("menu_settings") }}</span>
      </div>
      <div class="dropdown-item logout" role="menuitem" @click="onLogout">
        <span>🚪</span>
        <span>{{ t("menu_logout") }}</span>
      </div>
    </div>
  </div>

  <!-- Settings Modal -->
  <SettingsModal :visible="settingsVisible" @close="settingsVisible = false" />
</template>

<script setup lang="ts">
import { ref, computed } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../stores/appStore"
import { apiFetch } from "../api"
import { getLoginPath } from "../config"
import { showToast } from "../lib/toast"
import SettingsModal from "./SettingsModal.vue"

const { t } = useI18n()
const appStore = useAppStore()

const dropdownOpen = ref(false)
const settingsVisible = ref(false)

const username = computed(() => appStore.user?.username || t("loading"))
const name = computed(() => appStore.user?.name || "")
const initials = computed(() => (appStore.user?.username || "U").charAt(0).toUpperCase())

const toggleDropdown = () => {
  dropdownOpen.value = !dropdownOpen.value
}
const closeDropdown = () => {
  dropdownOpen.value = false
}

function onSettings() {
  settingsVisible.value = true
  closeDropdown()
}

async function onLogout() {
  try {
    localStorage.setItem("nars_resume_phase", String(appStore.currentPhase))
    const res = await apiFetch("/api/logout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
    })
    if (res.ok) window.location.href = getLoginPath()
    else showToast(t("alert_logout_failed"), "error")
  } catch {
    showToast(t("alert_logout_failed"), "error")
  }
}
</script>
