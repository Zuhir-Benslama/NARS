<template>
    <div v-if="visible" class="modal">
        <div class="modal-content settings-modal">
            <div class="modal-header">{{ t('settings_title') }}</div>

            <div class="settings-container">
                <!-- Sidebar Navigation -->
                <div class="settings-sidebar">
                    <button v-for="tab in tabs" :key="tab.id" 
                            :class="['sidebar-tab', { active: activeTab === tab.id }]"
                            @click="activeTab = tab.id">
                        {{ tab.icon }} {{ t(tab.tKey) }}
                    </button>
                </div>

                <!-- Content Panels -->
                <div class="settings-main">
                    <!-- 1 & 2: Language & Theme -->
                    <div v-if="activeTab === 'general'">
                        <div class="modal-field">
                            <label>{{ t('label_language') }}</label>
                            <select v-model="ui.language" class="modal-input" @change="changeLanguage" >
                                <option value="en">{{ t('lang_en') }}</option>
                                <option value="fr">{{ t('lang_fr') }}</option>
                                <option value="ar">{{ t('lang_ar') }}</option>
                            </select>
                        </div>
                        <div class="modal-field">
                            <label>{{ t('label_theme') }}</label>
                            <div class="theme-switcher">
                                <button :class="['theme-btn', { selected: ui.theme === 'light' }]" @click="setTheme('light')">{{ t('theme_white') }}</button>
                                <button :class="['theme-btn', { selected: ui.theme === 'dark' }]" @click="setTheme('dark')">{{ t('theme_dark') }}</button>
                            </div>
                        </div>
                    </div>

                    <!-- 3: User Credentials -->
                    <div v-if="activeTab === 'account'">
                        <div class="modal-field">
                            <label>{{ t('label_username') }}</label>
                            <input type="text" v-model="account.username" class="modal-input" autocomplete="off" />
                        </div>
                        <div class="modal-field">
                            <label>{{ t('label_email') }}</label>
                            <input type="email" v-model="account.email" class="modal-input" autocomplete="off" />
                        </div>
                        <div class="modal-field">
                            <label>{{ t('label_password') }}</label>
                            <input type="password" v-model="account.password" class="modal-input" :placeholder="t('placeholder_password')" />
                        </div>
                        <button class="modal-btn modal-btn-save" @click="saveAccount">{{ t('btn_update_creds') }}</button>
                    </div>

                    <!-- 4: Custom Feature Types -->
                    <div v-if="activeTab === 'features'">
                        <p class="settings-hint">{{ t('hint_features') }}</p>
                        <div class="modal-field">
                            <label>{{ t('label_category') }}</label>
                            <select v-model="newFeature.category" class="modal-input">
                                <option value="districts">{{ t('cat_districts') }}</option>
                                <option value="roads">{{ t('cat_roads') }}</option>
                                <option value="publicBuildings">{{ t('cat_publicBuildings') }}</option>
                                <option value="publicSpaces">{{ t('cat_publicSpaces') }}</option>
                            </select>
                        </div>
                        <div class="modal-field">
                            <label>{{ t('label_feature_label') }}</label>
                            <input type="text" v-model="newFeature.label" class="modal-input" :placeholder="t('placeholder_feature_label')" />
                        </div>
                        <button class="modal-btn modal-btn-save" @click="addFeatureType">{{ t('btn_add_feature') }}</button>
                    </div>

                    <!-- 5: About -->
                    <div v-if="activeTab === 'about'" class="about-panel">
                        <h3>{{ t('about_nars') }}</h3>
                        <p>{{ t('about_nars_desc') }}</p>
                        <p class="version">{{ t('about_version') }}</p>
                        <hr />
                        <p>{{ t('about_body') }}</p>
                        <p><small>{{ t('about_copyright') }}</small></p>
                    </div>
                </div>
            </div>

            <div class="modal-buttons">
                <button class="modal-btn modal-btn-cancel" @click="$emit('close')">{{ t('btn_close') }}</button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, watch } from 'vue'
import { store } from '../store'
import { apiFetch } from '../api'
import { t, setLang, currentLang } from '../i18n'

const props = defineProps<{ visible: boolean }>()
const emit = defineEmits(['close'])

const activeTab = ref('general')
const tabs = [
    { id: 'general',  tKey: 'tab_general',  icon: '⚙️' },
    { id: 'account',  tKey: 'tab_account',  icon: '👤' },
    { id: 'features', tKey: 'tab_features', icon: '⬟' },
    { id: 'about',    tKey: 'tab_about',    icon: 'ℹ️' }
]

const ui = reactive({
    language: currentLang.value,
    theme: localStorage.getItem('nars_theme') || 'light'
})

const account = reactive({
    username: store.user?.username || '',
    email: store.user?.email || '',
    password: ''
})

// Re-sync account data from store when the modal becomes visible
watch(() => props.visible, (val) => {
    if (val && store.user) {
        account.username = store.user.username || ''
        account.email = store.user.email || ''
    }
})

watch(currentLang, (lang) => {
    ui.language = lang
})

const newFeature = reactive({ category: 'districts', label: '' })

async function changeLanguage() {
    await setLang(ui.language)
}

function setTheme(mode: 'light' | 'dark') {
    ui.theme = mode
    document.documentElement.setAttribute('data-theme', mode)
    localStorage.setItem('nars_theme', mode)
}

async function saveAccount() {
    const res = await apiFetch('/api/user/update', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(account)
    })
    if (res.ok) {
        alert(t('alert_account_updated'))
        account.password = ''
    }
}

async function addFeatureType() {
    if (!newFeature.label.trim()) return
    const res = await apiFetch('/api/feature-types/custom', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newFeature)
    })
    if (res.ok) {
        alert(t('alert_feature_added', { label: newFeature.label, category: newFeature.category }))
        newFeature.label = ''
    }
}
</script>

<style scoped>
/* ── Backdrop ────────────────────────────────────────────────────────────────── */
.modal {
    position: fixed;
    z-index: 10000;
    left: 0; top: 0;
    width: 100%; height: 100%;
    background: var(--overlay-bg);
    display: flex;
    align-items: center;
    justify-content: center;
}

/* ── Card ─────────────────────────────────────────────────────────────────────── */
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
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

/* ── Header ──────────────────────────────────────────────────────────────────── */
.modal-header {
    padding: 20px 24px 0;
    font-size: 18px;
    font-weight: 700;
    color: var(--text-primary);
    flex-shrink: 0;
}

/* ── Layout: sidebar + main ──────────────────────────────────────────────────── */
.settings-container {
    display: flex;
    flex: 1;
    overflow: hidden;
    margin-top: 16px;
}

/* ── Sidebar ─────────────────────────────────────────────────────────────────── */
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
    background: rgba(255,255,255,0.08);
    border-left-color: rgba(255,255,255,0.70);
}

/* ── Main content ────────────────────────────────────────────────────────────── */
.settings-main {
    flex: 1;
    overflow-y: auto;
    padding: 20px 24px;
}

/* ── Fields (reuse app.css modal-field + modal-input) ───────────────────────── */
.settings-hint {
    font-size: 13px;
    color: var(--text-secondary);
    margin-bottom: 16px;
    line-height: 1.5;
}

/* ── Theme switcher buttons ──────────────────────────────────────────────────── */
.theme-switcher {
    display: flex;
    gap: 8px;
    margin-top: 4px;
}

.theme-btn {
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

.theme-btn:hover {
    background: var(--glass-bg-hover);
    color: var(--text-primary);
}

.theme-btn.selected {
    background: rgba(255,255,255,0.22);
    color: var(--text-primary);
    border-color: rgba(255,255,255,0.55);
}

/* ── About panel ─────────────────────────────────────────────────────────────── */
.about-panel h3 {
    font-size: 18px;
    font-weight: 700;
    color: var(--text-primary);
    margin-bottom: 8px;
}

.about-panel p {
    font-size: 13px;
    color: var(--text-secondary);
    margin-bottom: 8px;
    line-height: 1.6;
}

.about-panel .version {
    font-weight: 600;
    color: var(--text-primary);
    font-size: 14px;
}

.about-panel hr {
    border: none;
    border-top: 1px solid var(--glass-border);
    margin: 12px 0;
}

/* ── Footer buttons ──────────────────────────────────────────────────────────── */
.modal-buttons {
    display: flex;
    justify-content: flex-end;
    gap: 10px;
    padding: 14px 24px;
    border-top: 1px solid var(--glass-border);
    flex-shrink: 0;
}

/* ── Export progress bar ─────────────────────────────────────────────────────── */
.export-progress-wrap {
    margin-top: 12px;
    border-radius: 8px;
    overflow: hidden;
    background: var(--input-bg);
    border: 1px solid var(--glass-border);
    height: 28px;
    position: relative;
}

.export-progress-bar {
    height: 100%;
    background: rgba(39, 174, 96, 0.75);
    transition: width 0.3s ease;
}

.export-progress-label {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 11px;
    font-weight: 600;
    color: var(--text-primary);
}
</style>