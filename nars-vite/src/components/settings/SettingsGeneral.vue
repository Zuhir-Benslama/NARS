<template>
  <div>
    <div class="modal-field">
      <label>{{ t("label_language") }}</label>
      <select v-model="lang" class="modal-input" @change="changeLanguage">
        <option value="en">
          {{ t("lang_en") }}
        </option>
        <option value="fr">
          {{ t("lang_fr") }}
        </option>
        <option value="ar">
          {{ t("lang_ar") }}
        </option>
      </select>
    </div>
    <div class="modal-field">
      <label>{{ t("label_theme") }}</label>
      <div class="theme-switcher">
        <button :class="['theme-btn', { selected: theme === 'light' }]" @click="setTheme('light')">
          {{ t("theme_white") }}
        </button>
        <button :class="['theme-btn', { selected: theme === 'dark' }]" @click="setTheme('dark')">
          {{ t("theme_dark") }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from "vue"
import { useI18n } from "vue-i18n"
import { setLang, currentLang } from "../../i18n"
import { theme, setTheme } from "../../composables/useTheme"

const { t } = useI18n()

const lang = ref(currentLang.value)

watch(currentLang, (v) => {
  lang.value = v
})

async function changeLanguage() {
  await setLang(lang.value)
}
</script>
