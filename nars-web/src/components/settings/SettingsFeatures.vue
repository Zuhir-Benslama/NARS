<template>
  <div>
    <p class="settings-hint">
      {{ t("hint_features") }}
    </p>
    <div class="modal-field">
      <label>{{ t("label_category") }}</label>
      <select v-model="form.category" class="modal-input">
        <option value="districts">
          {{ t("cat_districts") }}
        </option>
        <option value="roads">
          {{ t("cat_roads") }}
        </option>
        <option value="publicBuildings">
          {{ t("cat_publicBuildings") }}
        </option>
        <option value="publicSpaces">
          {{ t("cat_publicSpaces") }}
        </option>
      </select>
    </div>
    <div class="modal-field">
      <label>{{ t("label_feature_label") }}</label>
      <input
        v-model="form.label"
        type="text"
        class="modal-input"
        :placeholder="t('placeholder_feature_label')"
      />
    </div>
    <button class="modal-btn modal-btn-save" :disabled="saving" @click="add">
      {{ t("btn_add_feature") }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"

const { t } = useI18n()

const form = reactive({ category: "districts", label: "" })
const saving = ref(false)

async function add() {
  if (saving.value || !form.label.trim()) return
  saving.value = true
  try {
    const res = await apiFetch("/api/feature-types/custom", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(form),
    })
    if (res.ok) {
      showToast(t("alert_feature_added", { label: form.label, category: form.category }), "success")
      form.label = ""
    } else {
      showToast(t("error_add_feature_failed"), "error")
    }
  } catch {
    showToast(t("error_add_feature_failed"), "error")
  } finally {
    saving.value = false
  }
}
</script>
