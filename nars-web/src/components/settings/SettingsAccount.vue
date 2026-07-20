<template>
  <div>
    <div class="modal-field">
      <label>{{ t("label_username") }}</label>
      <input v-model="form.username" type="text" class="modal-input" autocomplete="off" />
    </div>
    <div class="modal-field">
      <label>{{ t("label_email") }}</label>
      <input v-model="form.email" type="email" class="modal-input" autocomplete="off" />
    </div>
    <div class="modal-field">
      <label>{{ t("label_password") }}</label>
      <input
        v-model="form.password"
        type="password"
        class="modal-input"
        :placeholder="t('placeholder_password')"
      />
    </div>
    <button class="modal-btn modal-btn-save" @click="save">
      {{ t("btn_update_creds") }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { reactive, watch } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"

const { t } = useI18n()

const props = defineProps<{ visible: boolean }>()

const appStore = useAppStore()

const form = reactive({ username: "", email: "", password: "" })

function syncFromStore() {
  if (appStore.user) {
    form.username = appStore.user.username || ""
    form.email = appStore.user.email || ""
    form.password = ""
  }
}

syncFromStore()
watch(
  () => props.visible,
  (v) => {
    if (v) syncFromStore()
  },
)

function validateForm(): string | null {
  if (!form.username.trim()) return t("error_username_required")
  if (form.username.length < 3) return t("error_username_min_length")
  if (form.username.length > 50) return t("error_username_max_length")
  if (!/^[a-zA-Z0-9_.-]+$/.test(form.username)) return t("error_username_invalid_chars")

  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
  if (!form.email.trim()) return t("error_email_required")
  if (!emailRegex.test(form.email)) return t("error_email_invalid")

  if (form.password && form.password.length < 8) return t("error_password_min_length")

  return null
}

async function save() {
  const validationError = validateForm()
  if (validationError) {
    showToast(validationError, "error")
    return
  }

  const body: Record<string, string> = {
    username: form.username.trim(),
    email: form.email.trim(),
  }
  if (form.password) body.password = form.password

  const res = await apiFetch("/api/user/update", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })
  if (res.ok) {
    showToast(t("alert_account_updated"), "success")
    form.password = ""
  } else {
    const data = await res.json().catch(() => ({}))
    const msg = data.detail || data.error || t("alert_account_updated_failed") || "Update failed"
    showToast(msg, "error")
  }
}
</script>
