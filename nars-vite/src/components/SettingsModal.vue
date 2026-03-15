<template>
    <div v-if="visible" class="s-backdrop">
        <div class="s-card">

            <!-- Header -->
            <div class="s-header">
                <span class="s-header-icon">⚙️</span>
                <span>{{ t('settings_title') }}</span>
                <button class="s-close" @click="$emit('close')">✕</button>
            </div>

            <!-- Tab bar — same bottom-border style as login tabs -->
            <div class="s-tabs">
                <button
                    v-for="tab in tabs"
                    :key="tab.id"
                    :class="['s-tab', { active: activeTab === tab.id }]"
                    @click="activeTab = tab.id"
                >
                    {{ tab.icon }} {{ t(tab.tKey) }}
                </button>
            </div>

            <!-- Content -->
            <div class="s-body">

                <!-- General: language + theme -->
                <div v-if="activeTab === 'general'">
                    <div class="s-field">
                        <label>{{ t('label_language') }}</label>
                        <select v-model="ui.language" class="s-input" @change="changeLanguage">
                            <option value="en">{{ t('lang_en') }}</option>
                            <option value="fr">{{ t('lang_fr') }}</option>
                            <option value="ar">{{ t('lang_ar') }}</option>
                        </select>
                    </div>

                    <div class="s-field">
                        <label>{{ t('label_theme') }}</label>
                        <div class="s-theme-row">
                            <button
                                :class="['s-theme-btn', { active: theme === 'light' }]"
                                @click="setTheme('light')"
                            >☀️ {{ t('theme_white') }}</button>
                            <button
                                :class="['s-theme-btn', { active: theme === 'dark' }]"
                                @click="setTheme('dark')"
                            >🌙 {{ t('theme_dark') }}</button>
                            <button
                                :class="['s-theme-btn', { active: theme === 'auto' }]"
                                @click="setTheme('auto')"
                            >🖥️ {{ t('theme_auto') }}</button>
                        </div>
                        <p class="s-hint s-hint-sm">
                            {{ theme === 'auto' ? t('theme_auto_hint') : '' }}
                        </p>
                    </div>
                </div>

                <!-- Account -->
                <div v-if="activeTab === 'account'">
                    <div class="s-field">
                        <label>{{ t('label_username') }}</label>
                        <input type="text"     v-model="account.username" class="s-input" autocomplete="off" />
                    </div>
                    <div class="s-field">
                        <label>{{ t('label_email') }}</label>
                        <input type="email"    v-model="account.email"    class="s-input" autocomplete="off" />
                    </div>
                    <div class="s-field">
                        <label>{{ t('label_password') }}</label>
                        <input type="password" v-model="account.password" class="s-input" :placeholder="t('placeholder_password')" />
                    </div>
                    <button class="s-btn" @click="saveAccount">{{ t('btn_update_creds') }}</button>
                </div>

                <!-- Feature types -->
                <div v-if="activeTab === 'features'">
                    <p class="s-hint">{{ t('hint_features') }}</p>
                    <div class="s-field">
                        <label>{{ t('label_category') }}</label>
                        <select v-model="newFeature.category" class="s-input">
                            <option value="districts">{{ t('cat_districts') }}</option>
                            <option value="roads">{{ t('cat_roads') }}</option>
                            <option value="publicBuildings">{{ t('cat_publicBuildings') }}</option>
                            <option value="publicSpaces">{{ t('cat_publicSpaces') }}</option>
                        </select>
                    </div>
                    <div class="s-field">
                        <label>{{ t('label_feature_label') }}</label>
                        <input type="text" v-model="newFeature.label" class="s-input" :placeholder="t('placeholder_feature_label')" />
                    </div>
                    <button class="s-btn" @click="addFeatureType">{{ t('btn_add_feature') }}</button>
                </div>

                <!-- About -->
                <div v-if="activeTab === 'about'" class="s-about">
                    <div class="s-about-logo">🗺️</div>
                    <h3>{{ t('about_nars') }}</h3>
                    <p>{{ t('about_nars_desc') }}</p>
                    <hr class="s-divider" />
                    <p>{{ t('about_body') }}</p>
                    <p class="s-muted">{{ t('about_version') }} &nbsp;·&nbsp; {{ t('about_copyright') }}</p>
                </div>

            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, watch } from 'vue'
import { useI18n }              from 'vue-i18n'
import { store }                from '../store'
import { apiFetch }             from '../api'
import { setLang }              from '../i18n'
import { theme, setTheme }      from '../composables/useTheme'

const { t, locale } = useI18n()

const props = defineProps<{ visible: boolean }>()
const emit  = defineEmits(['close'])

// ── Tabs ──────────────────────────────────────────────────────────────────────
const activeTab = ref('general')
const tabs = [
    { id: 'general',  tKey: 'tab_general',  icon: '⚙️' },
    { id: 'account',  tKey: 'tab_account',  icon: '👤' },
    { id: 'features', tKey: 'tab_features', icon: '⬟'  },
    { id: 'about',    tKey: 'tab_about',    icon: 'ℹ️'  },
]

// ── Language ──────────────────────────────────────────────────────────────────
const ui = reactive({ language: locale.value })
watch(locale, (lang) => { ui.language = lang })

async function changeLanguage() {
    await setLang(ui.language)
}

// ── Account ───────────────────────────────────────────────────────────────────
const account = reactive({
    username: store.user?.username || '',
    email:    store.user?.email    || '',
    password: '',
})

watch(() => props.visible, (val) => {
    if (val && store.user) {
        account.username = store.user.username || ''
        account.email    = store.user.email    || ''
    }
})

async function saveAccount() {
    const res = await apiFetch('/api/user/update', {
        method:  'PUT',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(account),
    })
    if (res.ok) {
        alert(t('alert_account_updated'))
        account.password = ''
    }
}

// ── Feature types ─────────────────────────────────────────────────────────────
const newFeature = reactive({ category: 'districts', label: '' })

async function addFeatureType() {
    if (!newFeature.label.trim()) return
    const res = await apiFetch('/api/feature-types/custom', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(newFeature),
    })
    if (res.ok) {
        alert(t('alert_feature_added', { label: newFeature.label, category: newFeature.category }))
        newFeature.label = ''
    }
}
</script>

<style scoped>
.s-backdrop {
    position: fixed;
    inset: 0;
    z-index: 10000;
    display: flex;
    align-items: center;
    justify-content: center;
    /* Match login page: stronger blur + subtle dark wash to keep map legible */
    background: rgba(0, 0, 0, 0.35);
    backdrop-filter: blur(20px);
    -webkit-backdrop-filter: blur(20px);
}

.s-card {
    width: 660px;
    max-width: 95vw;
    max-height: 90vh;
    display: flex;
    flex-direction: column;
    background: rgba(255, 255, 255, 0.2);
    backdrop-filter: blur(20px);
    -webkit-backdrop-filter: blur(20px);
    border-radius: 15px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2), inset 0 1px 0 rgba(255, 255, 255, 0.15);
    border: 1px solid rgba(255, 255, 255, 0.25);
    overflow: hidden;
    color: #fff;
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.s-header {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 22px 28px 18px;
    font-size: 20px;
    font-weight: 600;
    text-shadow: 0 2px 10px rgba(0, 0, 0, 0.3);
    border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.s-header-icon { font-size: 22px; }

.s-close {
    margin-left: auto;
    background: none;
    border: none;
    color: rgba(255, 255, 255, 0.55);
    font-size: 16px;
    cursor: pointer;
    padding: 4px 8px;
    border-radius: 6px;
    transition: all 0.2s;
    line-height: 1;
}
.s-close:hover {
    background: rgba(255, 255, 255, 0.12);
    color: #fff;
}

.s-tabs {
    display: flex;
    background: rgba(0, 0, 0, 0.01);
    border-bottom: 1px solid rgba(255, 255, 255, 0.07);
    flex-shrink: 0;
}

.s-tab {
    flex: 1;
    padding: 16px 8px;
    background: none;
    border: none;
    border-bottom: 3px solid transparent;
    color: rgba(255, 255, 255, 0.6);
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.3s;
    white-space: nowrap;
}
.s-tab:hover {
    background: rgba(255, 255, 255, 0.08);
    color: rgba(255, 255, 255, 0.9);
}
.s-tab.active {
    color: #fff;
    background: rgba(255, 255, 255, 0.1);
    border-bottom: 3px solid rgba(255, 255, 255, 0.8);
}

.s-body {
    padding: 28px;
    overflow-y: auto;
    flex: 1;
}

.s-field { margin-bottom: 20px; }

.s-field label {
    display: block;
    margin-bottom: 8px;
    font-weight: 600;
    color: rgba(255, 255, 255, 0.85);
    font-size: 14px;
}

.s-input {
    width: 100%;
    padding: 12px;
    border: 1px solid rgba(255, 255, 255, 0.3);
    border-radius: 8px;
    font-size: 14px;
    background: rgba(255, 255, 255, 0.1);
    backdrop-filter: blur(8px);
    -webkit-backdrop-filter: blur(8px);
    -webkit-appearance: none;
    appearance: none;
    color-scheme: dark;
    color: #fff;
    -webkit-text-fill-color: #fff;
    transition: all 0.2s;
    letter-spacing: 0.2px;
}
.s-input::placeholder { color: rgba(255, 255, 255, 0.3); }
.s-input:focus {
    outline: none;
    border-color: rgba(255, 255, 255, 0.45);
    box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.04);
}
.s-input option { background: #0d2a45; color: #fff; }
.s-input:-webkit-autofill,
.s-input:-webkit-autofill:hover,
.s-input:-webkit-autofill:focus {
    -webkit-box-shadow: 0 0 0px 1000px transparent inset !important;
    -webkit-text-fill-color: #fff !important;
    caret-color: #fff;
    transition: background-color 9999s ease-in-out 0s;
}

.s-btn {
    width: 100%;
    padding: 13px;
    background: rgba(255, 255, 255, 0.15);
    backdrop-filter: blur(8px);
    -webkit-backdrop-filter: blur(8px);
    color: #fff;
    border: 1px solid rgba(255, 255, 255, 0.25);
    border-radius: 8px;
    font-size: 15px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.2s;
    letter-spacing: 0.3px;
    margin-top: 6px;
}
.s-btn:hover {
    background: rgba(255, 255, 255, 0.1);
    transform: translateY(-2px);
    box-shadow: 0 6px 20px rgba(0, 0, 0, 0.1);
}
.s-btn:active { transform: translateY(0); }

.s-theme-row { display: flex; gap: 10px; }

.s-theme-btn {
    flex: 1;
    padding: 11px;
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 8px;
    color: rgba(255, 255, 255, 0.65);
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: all 0.2s;
}
.s-theme-btn:hover {
    background: rgba(255, 255, 255, 0.13);
    color: #fff;
}
.s-theme-btn.active {
    background: rgba(255, 255, 255, 0.2);
    border-color: rgba(255, 255, 255, 0.5);
    color: #fff;
}

.s-hint {
    font-size: 13px;
    color: rgba(255, 255, 255, 0.6);
    margin-bottom: 18px;
}
.s-hint-sm {
    font-size: 12px;
    margin-top: 8px;
    margin-bottom: 0;
    min-height: 18px;
}

.s-about { text-align: center; padding: 10px 0; }
.s-about-logo { font-size: 42px; margin-bottom: 12px; }
.s-about h3 {
    font-size: 20px;
    font-weight: 600;
    margin-bottom: 6px;
    text-shadow: 0 2px 10px rgba(0, 0, 0, 0.3);
}
.s-about p { color: rgba(255, 255, 255, 0.75); font-size: 14px; margin-bottom: 6px; }
.s-divider {
    border: none;
    border-top: 1px solid rgba(255, 255, 255, 0.15);
    margin: 18px auto;
    width: 60%;
}
.s-muted {
    color: rgba(255, 255, 255, 0.4) !important;
    font-size: 12px !important;
    margin-top: 14px;
}
/* ── Light mode overrides ────────────────────────────────────────────────────── */
/* The modal uses glassmorphism (rgba whites) designed for dark.                 */
/* In light mode we flip to a semi-opaque light card with dark text.             */

:global([data-theme="light"]) .s-card {
    background: rgba(255, 255, 255, 0.88);
    border: 1px solid rgba(0, 0, 0, 0.12);
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.12), inset 0 1px 0 rgba(255, 255, 255, 0.9);
    color: #1a1a2e;
}

:global([data-theme="light"]) .s-header {
    border-bottom-color: rgba(0, 0, 0, 0.08);
    text-shadow: none;
}

:global([data-theme="light"]) .s-close {
    color: rgba(0, 0, 0, 0.45);
}
:global([data-theme="light"]) .s-close:hover {
    background: rgba(0, 0, 0, 0.06);
    color: #1a1a2e;
}

:global([data-theme="light"]) .s-tabs {
    border-bottom-color: rgba(0, 0, 0, 0.07);
    background: rgba(0, 0, 0, 0.02);
}

:global([data-theme="light"]) .s-tab {
    color: rgba(0, 0, 0, 0.5);
    border-bottom-color: transparent;
}
:global([data-theme="light"]) .s-tab:hover {
    background: rgba(0, 0, 0, 0.05);
    color: rgba(0, 0, 0, 0.85);
}
:global([data-theme="light"]) .s-tab.active {
    color: #1a1a2e;
    background: rgba(0, 0, 0, 0.06);
    border-bottom-color: #1a1a2e;
}

:global([data-theme="light"]) .s-field label {
    color: rgba(0, 0, 0, 0.75);
}

:global([data-theme="light"]) .s-input {
    background: rgba(0, 0, 0, 0.05);
    border-color: rgba(0, 0, 0, 0.2);
    color: #1a1a2e;
    -webkit-text-fill-color: #1a1a2e;
    color-scheme: light;
}
:global([data-theme="light"]) .s-input::placeholder {
    color: rgba(0, 0, 0, 0.3);
}
:global([data-theme="light"]) .s-input:focus {
    border-color: #2980b9;
    box-shadow: 0 0 0 3px rgba(41, 128, 185, 0.12);
}
:global([data-theme="light"]) .s-input option {
    background: #ffffff;
    color: #1a1a2e;
}

:global([data-theme="light"]) .s-btn {
    background: rgba(0, 0, 0, 0.08);
    border-color: rgba(0, 0, 0, 0.2);
    color: #1a1a2e;
}
:global([data-theme="light"]) .s-btn:hover {
    background: rgba(0, 0, 0, 0.13);
}

:global([data-theme="light"]) .s-theme-btn {
    background: rgba(0, 0, 0, 0.05);
    border-color: rgba(0, 0, 0, 0.18);
    color: rgba(0, 0, 0, 0.6);
}
:global([data-theme="light"]) .s-theme-btn:hover {
    background: rgba(0, 0, 0, 0.09);
    color: #1a1a2e;
}
:global([data-theme="light"]) .s-theme-btn.active {
    background: rgba(0, 0, 0, 0.12);
    border-color: rgba(0, 0, 0, 0.4);
    color: #1a1a2e;
}

:global([data-theme="light"]) .s-hint {
    color: rgba(0, 0, 0, 0.5);
}

:global([data-theme="light"]) .s-about h3 {
    text-shadow: none;
    color: #1a1a2e;
}
:global([data-theme="light"]) .s-about p {
    color: rgba(0, 0, 0, 0.65);
}
:global([data-theme="light"]) .s-muted {
    color: rgba(0, 0, 0, 0.35) !important;
}
:global([data-theme="light"]) .s-divider {
    border-top-color: rgba(0, 0, 0, 0.12);
}
</style>
