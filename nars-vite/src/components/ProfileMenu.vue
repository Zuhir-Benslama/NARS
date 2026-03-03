<template>
    <div class="profile-menu" v-click-outside="closeDropdown">
        <div class="profile-button" @click="toggleDropdown">
            <div class="profile-icon">{{ initials }}</div>
            <div class="profile-info">
                <div class="profile-username">{{ username }}</div>
                <div class="profile-name">{{ name }}</div>
            </div>
            <span :class="['dropdown-arrow', { open: dropdownOpen }]">▼</span>
        </div>
        <div :class="['profile-dropdown', { show: dropdownOpen }]">
            <div class="dropdown-item" @click="onSettings">
                <span>⚙️</span><span>Settings</span>
            </div>
            <div class="dropdown-item logout" @click="onLogout">
                <span>🚪</span><span>Log Out</span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { store }         from '../store'
import { apiFetch }      from '../api'

const dropdownOpen = ref(false)

const username = computed(() => store.user?.username || 'Loading...')
const name     = computed(() => store.user?.name     || '')
const initials = computed(() => (store.user?.username || 'U').charAt(0).toUpperCase())

const toggleDropdown = () => { dropdownOpen.value = !dropdownOpen.value }
const closeDropdown  = () => { dropdownOpen.value = false }

function onSettings() {
    alert('Settings coming soon.')
    closeDropdown()
}

async function onLogout() {
    try {
        const res = await apiFetch('/api/logout', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
        })
        if (res.ok) window.location.href = '/login'
        else alert('Logout failed. Please try again.')
    } catch { alert('Logout failed. Please try again.') }
}
</script>
