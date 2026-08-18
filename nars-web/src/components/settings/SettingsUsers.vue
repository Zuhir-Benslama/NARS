<template>
  <div class="settings-users">
    <!-- Existing users list -->
    <div class="su-section">
      <h3 class="su-section-title">{{ t("su_user_list_title") }} ({{ users.length }})</h3>
      <div v-if="fetchUsersError" class="su-feedback su-error">{{ fetchUsersError }}</div>
      <div v-else-if="users.length === 0" class="su-empty">{{ t("admin.no_data") }}</div>
      <div v-for="u in users" :key="u.user_id" class="su-user-row">
        <div class="su-user-info">
          <span class="su-user-name">{{ u.name }}</span>
          <span class="su-user-meta">{{ u.username }} — {{ u.email }}</span>
          <span class="su-user-role">{{ roleLabel(u.role) }}</span>
        </div>
        <div class="su-user-actions">
          <button class="su-btn su-btn-edit" @click="startEdit(u)">{{ t("su_edit_btn") }}</button>
          <button class="su-btn su-btn-delete" @click="confirmDelete(u)">
            {{ t("su_delete_btn") }}
          </button>
        </div>
      </div>
    </div>

    <hr class="su-divider" />

    <!-- Create / Edit form -->
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

    <!-- Delete confirmation modal -->
    <Teleport to="body">
      <div v-if="deleteTarget" class="su-modal-overlay" @click.self="deleteTarget = null">
        <div class="su-modal">
          <p>{{ t("su_confirm_delete", { username: deleteTarget.username }) }}</p>
          <div class="su-modal-actions">
            <button class="modal-btn modal-btn-danger" :disabled="deleting" @click="doDelete">
              <span v-if="deleting">…</span>
              <span v-else>{{ t("su_delete_btn") }}</span>
            </button>
            <button class="modal-btn modal-btn-cancel" @click="deleteTarget = null">
              {{ t("su_cancel_btn") }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"
import { getUserMessageKey } from "../../lib/errors"
import { debugWarn } from "../../utils/debug"
import type { UserRole } from "../../types"
import LocationSearchSelect from "./LocationSearchSelect.vue"

interface ManageableUser {
  user_id: string
  username: string
  name: string
  email: string
  role: string
  phone: string
  commune_id: number | null
  daira_id: number | null
  wilaya_id: number | null
}

const { t } = useI18n()
const appStore = useAppStore()

const role = computed<UserRole>(() => appStore.user?.role ?? "commune_user")

// ── User list ────────────────────────────────────────────────────────────
const users = ref<ManageableUser[]>([])
const loadingUsers = ref(false)
const fetchUsersError = ref<string | null>(null)

async function fetchUsers() {
  loadingUsers.value = true
  fetchUsersError.value = null
  try {
    const res = await apiFetch("/api/admin/users")
    if (res.ok) {
      users.value = (await res.json()) as ManageableUser[]
    } else {
      fetchUsersError.value = t("admin.load_error")
    }
  } catch (e) {
    debugWarn("[SettingsUsers] fetchUsers failed:", e)
    fetchUsersError.value = t("admin.load_error")
  } finally {
    loadingUsers.value = false
  }
}

function roleLabel(r: string): string {
  return (
    {
      commune_user: t("su_role_commune"),
      field_worker: t("su_role_field_worker"),
      daira_admin: t("su_role_daira"),
      wilaya_admin: t("su_role_wilaya"),
    }[r] ?? r
  )
}

// ── Delete ───────────────────────────────────────────────────────────────
const deleteTarget = ref<ManageableUser | null>(null)
const deleting = ref(false)

function confirmDelete(u: ManageableUser) {
  deleteTarget.value = u
}

async function doDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    const res = await apiFetch(`/api/admin/users/${deleteTarget.value.user_id}`, {
      method: "DELETE",
    })
    if (res.ok) {
      showToast(t("su_delete_success"), "success")
      users.value = users.value.filter((u) => u.user_id !== deleteTarget.value!.user_id)
      deleteTarget.value = null
    } else {
      showToast(t("su_err_network"), "error")
    }
  } catch (err) {
    showToast(t(getUserMessageKey(err)), "error")
  } finally {
    deleting.value = false
  }
}

// ── Target role ──────────────────────────────────────────────────────────
interface RoleOption {
  value: string
  label: string
}

const availableTargetRoles = computed<RoleOption[]>(() => {
  switch (role.value) {
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
  if (!editingUser.value) targetRole.value = opts[0]?.value ?? ""
})

// ── Hint text ────────────────────────────────────────────────────────────
const hintText = computed(() => {
  switch (role.value) {
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
const showWilayaSelect = computed(() => role.value === "national_admin")
const showDairaSelect = computed(() => role.value === "wilaya_admin")
const showCommuneSelect = computed(
  () =>
    role.value === "daira_admin" ||
    (role.value === "commune_user" && targetRole.value !== "field_worker"),
)
const needWilayaFirst = computed(() => role.value === "national_admin")

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
  if (!editingUser.value) {
    selectedWilayaId.value = null
    selectedDairaId.value = null
    selectedCommuneId.value = null
    wilayaRef.value?.reset()
    dairaRef.value?.reset()
    communeRef.value?.reset()
  }
})

onMounted(fetchUsers)

// ── Form state ───────────────────────────────────────────────────────────
const form = ref({ name: "", email: "", phone: "", username: "", password: "" })
const loading = ref(false)
const successMsg = ref("")
const errorMsg = ref("")

const editingUser = ref<ManageableUser | null>(null)

function startEdit(u: ManageableUser) {
  editingUser.value = u
  form.value = { name: u.name, email: u.email, phone: u.phone, username: u.username, password: "" }
  targetRole.value = u.role
  selectedWilayaId.value = u.wilaya_id
  selectedDairaId.value = u.daira_id
  selectedCommuneId.value = u.commune_id
  successMsg.value = ""
  errorMsg.value = ""
}

function cancelEdit() {
  editingUser.value = null
  form.value = { name: "", email: "", phone: "", username: "", password: "" }
  targetRole.value = availableTargetRoles.value[0]?.value ?? ""
  selectedWilayaId.value = null
  selectedDairaId.value = null
  selectedCommuneId.value = null
  successMsg.value = ""
  errorMsg.value = ""
}

// ── Validation ───────────────────────────────────────────────────────────
function validate(): string | null {
  if (!form.value.name.trim()) return t("su_err_name")
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.value.email)) return t("su_err_email")
  if (!form.value.phone.trim()) return t("su_err_phone")
  if (form.value.username.length < 3) return t("su_err_username")
  if (!editingUser.value && form.value.password.length < 8) return t("su_err_password")
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
    if (editingUser.value) {
      const body: Record<string, unknown> = {}
      if (form.value.name.trim() !== editingUser.value.name) body.name = form.value.name.trim()
      if (form.value.email.trim() !== editingUser.value.email) body.email = form.value.email.trim()
      if (form.value.phone.trim() !== editingUser.value.phone) body.phone = form.value.phone.trim()
      if (targetRole.value !== editingUser.value.role) body.role = targetRole.value
      if (selectedWilayaId.value !== editingUser.value.wilaya_id)
        body.wilaya_id = selectedWilayaId.value
      if (selectedDairaId.value !== editingUser.value.daira_id)
        body.daira_id = selectedDairaId.value
      if (selectedCommuneId.value !== editingUser.value.commune_id)
        body.commune_id = selectedCommuneId.value

      if (Object.keys(body).length === 0) {
        successMsg.value = t("su_update_success")
        showToast(successMsg.value, "success")
        return
      }

      const res = await apiFetch(`/api/admin/users/${editingUser.value.user_id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      })
      if (res.ok) {
        successMsg.value = t("su_update_success")
        showToast(successMsg.value, "success")
        cancelEdit()
        await fetchUsers()
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
        form.value = { name: "", email: "", phone: "", username: "", password: "" }
        selectedWilayaId.value = null
        selectedDairaId.value = null
        selectedCommuneId.value = null
        await fetchUsers()
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
.settings-users {
  display: flex;
  flex-direction: column;
  gap: 0;
}
.su-section {
  margin-bottom: 1rem;
}
.su-section-title {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 0.75rem;
}
.su-empty {
  font-size: 0.85rem;
  color: var(--text-muted);
  padding: 0.5rem 0;
}
.su-user-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.6rem 0;
  border-bottom: 1px solid var(--glass-border, rgba(255, 255, 255, 0.08));
}
.su-user-info {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}
.su-user-name {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
}
.su-user-meta {
  font-size: 0.75rem;
  color: var(--text-muted);
}
.su-user-role {
  font-size: 0.72rem;
  color: var(--text-secondary);
}
.su-user-actions {
  display: flex;
  gap: 0.4rem;
  flex-shrink: 0;
}
.su-btn {
  padding: 0.3rem 0.7rem;
  font-size: 0.75rem;
  border-radius: 5px;
  cursor: pointer;
  border: 1px solid var(--glass-border);
  background: var(--glass-bg);
  color: var(--text-primary);
}
.su-btn:hover {
  background: var(--glass-bg-hover);
}
.su-btn-delete {
  color: var(--danger-color);
  border-color: var(--danger-border);
}
.su-btn-delete:hover {
  background: var(--danger-bg);
}
.su-divider {
  border: none;
  border-top: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  margin: 1rem 0;
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
.su-select {
  appearance: none;
  cursor: pointer;
}
.su-form-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}
.su-modal-overlay {
  position: fixed;
  inset: 0;
  z-index: 10002;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--overlay-bg);
  backdrop-filter: blur(2px);
}
.su-modal {
  background: var(--modal-bg, #1a2035);
  border: 1px solid var(--glass-border);
  border-radius: 10px;
  padding: 1.5rem;
  max-width: 360px;
  width: 90%;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
}
.su-modal p {
  margin: 0 0 1rem;
  font-size: 0.9rem;
  color: var(--text-primary);
}
.su-modal-actions {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
}
</style>
