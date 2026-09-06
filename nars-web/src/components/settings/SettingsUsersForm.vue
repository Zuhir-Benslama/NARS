<template>
  <div class="su-section">
    <h3 class="su-section-title">
      {{ editingUser ? t("su_edit_btn") : t("su_create_btn") }}
    </h3>
    <p class="settings-hint">{{ hintText }}</p>

    <div v-if="successMsg" class="su-feedback su-success">{{ successMsg }}</div>
    <div v-if="errorMsg" class="su-feedback su-error">{{ errorMsg }}</div>

    <div v-if="availableTargetRoles.length > 1" class="modal-field">
      <label>{{ t("su_account_type") }}</label>
      <select v-model="targetRole" class="modal-input su-select" :disabled="!!editingUser">
        <option v-for="r in availableTargetRoles" :key="r.value" :value="r.value">
          {{ r.label }}
        </option>
      </select>
    </div>

    <div class="modal-field">
      <label>{{ t("su_full_name") }}</label>
      <input
        v-model="form.name"
        type="text"
        class="modal-input"
        autocomplete="off"
        maxlength="150"
      />
    </div>
    <div class="modal-field">
      <label>{{ t("su_email") }}</label>
      <input
        v-model="form.email"
        type="email"
        class="modal-input"
        autocomplete="off"
        maxlength="254"
      />
    </div>
    <div class="modal-field">
      <label>{{ t("su_phone") }}</label>
      <input
        v-model="form.phone"
        type="tel"
        class="modal-input"
        autocomplete="off"
        maxlength="20"
      />
    </div>
    <div class="modal-field">
      <label>{{ t("su_username") }}</label>
      <input
        v-model="form.username"
        type="text"
        class="modal-input"
        autocomplete="off"
        maxlength="50"
        :disabled="!!editingUser"
      />
    </div>
    <div v-if="!editingUser" class="modal-field">
      <label>{{ t("su_password") }}</label>
      <input
        v-model="form.password"
        type="password"
        class="modal-input"
        autocomplete="new-password"
        maxlength="128"
      />
    </div>

    <LocationSearchSelect
      v-if="showWilayaSelect"
      ref="wilayaRef"
      v-model="selectedWilayaId"
      :label="t('su_wilaya')"
      :placeholder="t('su_search_wilaya')"
      endpoint="/api/wilayas"
    />

    <LocationSearchSelect
      v-if="showDairaSelect"
      ref="dairaRef"
      v-model="selectedDairaId"
      :label="t('su_daira')"
      :placeholder="dairaPlaceholder"
      :disabled="needWilayaFirst && !selectedWilayaId"
      :endpoint="dairaEndpoint"
    />

    <LocationSearchSelect
      v-if="showCommuneSelect"
      ref="communeRef"
      v-model="selectedCommuneId"
      :label="t('su_commune')"
      :placeholder="t('su_search_commune')"
      :endpoint="communeEndpoint"
    />

    <div class="su-form-actions">
      <button class="modal-btn modal-btn-save" :disabled="loading" @click="submit">
        <span v-if="loading">…</span>
        <span v-else>{{ editingUser ? t("su_update_btn") : t("su_create_btn") }}</span>
      </button>
      <button v-if="editingUser" class="modal-btn modal-btn-cancel" @click="cancelEdit">
        {{ t("su_cancel_btn") }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"
import { getUserMessageKey } from "../../lib/errors"
import type { ManageableUser, UserRole } from "../../types"
import LocationSearchSelect from "./LocationSearchSelect.vue"

const props = defineProps<{
  role: UserRole
  editingUser: ManageableUser | null
}>()

const emit = defineEmits<{
  saved: []
  cancel: []
}>()

const { t } = useI18n()
const appStore = useAppStore()

// ── Target role ──────────────────────────────────────────────────────────
interface RoleOption {
  value: string
  label: string
}

const availableTargetRoles = computed<RoleOption[]>(() => {
  switch (props.role) {
    case "commune_user":
      return [{ value: "field_worker", label: t("su_role_field_worker") }]
    case "daira_admin":
      return [{ value: "commune_user", label: t("su_role_commune") }]
    case "wilaya_admin":
      return [{ value: "daira_admin", label: t("su_role_daira") }]
    case "national_admin":
      return [{ value: "wilaya_admin", label: t("su_role_wilaya") }]
    default:
      return []
  }
})
const targetRole = ref(availableTargetRoles.value[0]?.value ?? "")
watch(availableTargetRoles, (opts) => {
  if (!props.editingUser) targetRole.value = opts[0]?.value ?? ""
})

// ── Hint text ────────────────────────────────────────────────────────────
const hintText = computed(() => {
  switch (props.role) {
    case "commune_user":
      return t("su_hint_commune")
    case "daira_admin":
      return t("su_hint_daira")
    case "wilaya_admin":
      return t("su_hint_wilaya")
    case "national_admin":
      return t("su_hint_national")
    default:
      return ""
  }
})

// ── Field visibility ─────────────────────────────────────────────────────
const showWilayaSelect = computed(() => props.role === "national_admin")
const showDairaSelect = computed(() => props.role === "wilaya_admin")
const showCommuneSelect = computed(
  () =>
    props.role === "daira_admin" ||
    (props.role === "commune_user" && targetRole.value !== "field_worker"),
)
const needWilayaFirst = computed(() => props.role === "national_admin")

// ── Location state ───────────────────────────────────────────────────────
const selectedWilayaId = ref<number | null>(null)
const selectedDairaId = ref<number | null>(null)
const selectedCommuneId = ref<number | null>(null)

const wilayaRef = ref<InstanceType<typeof LocationSearchSelect> | null>(null)
const dairaRef = ref<InstanceType<typeof LocationSearchSelect> | null>(null)
const communeRef = ref<InstanceType<typeof LocationSearchSelect> | null>(null)

const dairaPlaceholder = computed(() =>
  needWilayaFirst.value && !selectedWilayaId.value
    ? t("su_select_wilaya_first")
    : t("su_search_daira"),
)

const dairaEndpoint = computed(() => (q: string) => {
  // Scope the daira list to the chosen wilaya — or, for a wilaya admin who has
  // no selector, to the caller's own wilaya.
  const wilayaId = selectedWilayaId.value ?? appStore.user?.wilaya?.id ?? null
  const wilayaParam = wilayaId ? `&wilaya_id=${wilayaId}` : ""
  return `/api/dairas?search=${encodeURIComponent(q)}${wilayaParam}`
})

const communeEndpoint = computed(() => (q: string) => {
  // Scope the commune list to the chosen daira — or, for a daira admin who has
  // no selector, to the caller's own daira.
  const dairaId = selectedDairaId.value ?? appStore.user?.daira?.id ?? null
  const dairaParam = dairaId ? `&daira_id=${dairaId}` : ""
  return `/api/communes?search=${encodeURIComponent(q)}${dairaParam}`
})

watch(selectedWilayaId, () => {
  selectedDairaId.value = null
  selectedCommuneId.value = null
  dairaRef.value?.reset()
  communeRef.value?.reset()
})

watch(selectedDairaId, () => {
  selectedCommuneId.value = null
  communeRef.value?.reset()
})

watch(targetRole, () => {
  if (!props.editingUser) {
    selectedWilayaId.value = null
    selectedDairaId.value = null
    selectedCommuneId.value = null
    wilayaRef.value?.reset()
    dairaRef.value?.reset()
    communeRef.value?.reset()
  }
})

// ── Form state ───────────────────────────────────────────────────────────
const form = ref({ name: "", email: "", phone: "", username: "", password: "" })
const loading = ref(false)
const successMsg = ref("")
const errorMsg = ref("")

function resetCreateForm() {
  form.value = { name: "", email: "", phone: "", username: "", password: "" }
  selectedWilayaId.value = null
  selectedDairaId.value = null
  selectedCommuneId.value = null
}

function resetForm() {
  resetCreateForm()
  targetRole.value = availableTargetRoles.value[0]?.value ?? ""
  successMsg.value = ""
  errorMsg.value = ""
}

watch(
  () => props.editingUser,
  (u) => {
    if (u) {
      form.value = {
        name: u.name,
        email: u.email,
        phone: u.phone,
        username: u.username,
        password: "",
      }
      targetRole.value = u.role
      selectedWilayaId.value = u.wilaya_id
      selectedDairaId.value = u.daira_id
      selectedCommuneId.value = u.commune_id
      successMsg.value = ""
      errorMsg.value = ""
    } else {
      resetForm()
    }
  },
)

function cancelEdit() {
  emit("cancel")
  resetForm()
}

// ── Validation ───────────────────────────────────────────────────────────
function validate(): string | null {
  if (!form.value.name.trim()) return t("su_err_name")
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.value.email)) return t("su_err_email")
  if (!form.value.phone.trim()) return t("su_err_phone")
  if (form.value.username.length < 3) return t("su_err_username")
  if (!props.editingUser && form.value.password.length < 8) return t("su_err_password")
  if (showWilayaSelect.value && !selectedWilayaId.value) return t("su_err_wilaya")
  if (showDairaSelect.value && !selectedDairaId.value) return t("su_err_daira")
  if (showCommuneSelect.value && !selectedCommuneId.value) return t("su_err_commune")
  return null
}

// ── Submit ───────────────────────────────────────────────────────────────
async function submit() {
  successMsg.value = ""
  errorMsg.value = ""
  const err = validate()
  if (err) {
    errorMsg.value = err
    return
  }

  loading.value = true
  try {
    if (props.editingUser) {
      const body: Record<string, unknown> = {}
      if (form.value.name.trim() !== props.editingUser.name) body.name = form.value.name.trim()
      if (form.value.email.trim() !== props.editingUser.email) body.email = form.value.email.trim()
      if (form.value.phone.trim() !== props.editingUser.phone) body.phone = form.value.phone.trim()
      if (targetRole.value !== props.editingUser.role) body.role = targetRole.value
      if (selectedWilayaId.value !== props.editingUser.wilaya_id)
        body.wilaya_id = selectedWilayaId.value
      if (selectedDairaId.value !== props.editingUser.daira_id)
        body.daira_id = selectedDairaId.value
      if (selectedCommuneId.value !== props.editingUser.commune_id)
        body.commune_id = selectedCommuneId.value

      if (Object.keys(body).length === 0) {
        successMsg.value = t("su_update_success")
        showToast(successMsg.value, "success")
        return
      }

      const res = await apiFetch(`/api/admin/users/${props.editingUser.user_id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      })
      if (res.ok) {
        successMsg.value = t("su_update_success")
        showToast(successMsg.value, "success")
        emit("saved")
      } else {
        const data = (await res.json().catch(() => null)) as { detail?: string } | null
        errorMsg.value = data?.detail ?? t("su_err_network")
        showToast(errorMsg.value, "error")
      }
    } else {
      const body: Record<string, unknown> = {
        name: form.value.name.trim(),
        email: form.value.email.trim(),
        phone: form.value.phone.trim(),
        username: form.value.username.trim(),
        password: form.value.password,
        role: targetRole.value,
        commune_id: selectedCommuneId.value ?? null,
        daira_id: selectedDairaId.value ?? null,
        wilaya_id: selectedWilayaId.value ?? null,
      }
      const res = await apiFetch("/api/admin/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      })
      if (res.ok) {
        successMsg.value = t("su_success")
        showToast(successMsg.value, "success")
        resetCreateForm()
        emit("saved")
      } else {
        const data = (await res.json().catch(() => null)) as { detail?: string } | null
        errorMsg.value = data?.detail ?? t("su_err_network")
        showToast(errorMsg.value, "error")
      }
    }
  } catch (err) {
    errorMsg.value = t(getUserMessageKey(err))
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.su-section {
  margin-bottom: 1rem;
}
.su-section-title {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 0.75rem;
}
.su-select {
  appearance: none;
  cursor: pointer;
}
.su-form-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}
.su-feedback {
  font-size: 13px;
  padding: 8px 12px;
  border-radius: 6px;
  margin-bottom: 12px;
  line-height: 1.5;
}
.su-success {
  background: var(--success-bg);
  color: var(--success-color);
  border: 1px solid var(--success-border);
}
.su-error {
  background: var(--danger-bg);
  color: var(--danger-color);
  border: 1px solid var(--danger-border);
}
</style>
