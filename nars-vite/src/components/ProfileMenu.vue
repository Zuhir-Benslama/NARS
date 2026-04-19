<template>
    <div v-click-outside="closeDropdown" class="profile-menu">
        <div class="profile-button" @click="toggleDropdown">
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
        <div :class="['profile-dropdown', { show: dropdownOpen }]">
            <div class="dropdown-item" @click="onSettings">
                <span>⚙️</span>
                <span>{{ t('menu_settings') }}</span>
            </div>
            <div class="dropdown-item logout" @click="onLogout">
                <span>🚪</span>
                <span>{{ t('menu_logout') }}</span>
            </div>
        </div>
    </div>

    <!-- Settings Modal -->
    <SettingsModal :visible="settingsVisible" @close="settingsVisible = false" />
</template>

<script setup lang="ts">
    import { ref, computed } from 'vue'
    import { useI18n } from 'vue-i18n'
    import { store } from '../store'
    import { apiFetch } from '../api'
    import { showToast } from '../toast'
    import SettingsModal from './SettingsModal.vue'

    const { t } = useI18n()

    const dropdownOpen = ref(false)
    const settingsVisible = ref(false)

    const username = computed(() => store.user?.username || t('loading'))
    const name = computed(() => store.user?.name || '')
    const initials = computed(() => (store.user?.username || 'U').charAt(0).toUpperCase())

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
            localStorage.setItem('nars_resume_phase', String(store.currentPhase))
            const res = await apiFetch('/api/logout', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
            })
            if (res.ok) window.location.href = '/login'
            else showToast(t('alert_logout_failed'), 'error')
        } catch {
            showToast(t('alert_logout_failed'), 'error')
        }
    }
</script>
