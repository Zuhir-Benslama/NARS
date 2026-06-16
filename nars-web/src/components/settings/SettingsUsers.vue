<template>
  <div class="settings-users">
    <!-- Existing users list -->
    <div class="su-section">
      <h3 class="su-section-title">{{ t("su_user_list_title") }} ({{ users.length }})</h3>
      <div v-if="users.length === 0" class="su-empty">{{ t("admin.no_data") }}</div>
      <div v-for="u in users" :key="u.user_id" class="su-user-row">
        <div class="su-user-info">
          <span class="su-user-name">{{ u.name }}</span>
          <span class="su-user-meta">{{ u.username }} — {{ u.email }}</span>
          <span class="su-user-role">{{ roleLabel(u.role) }}</span>
        </div>
        <div class="su-user-actions">
          <button class="su-btn su-btn-edit" @click="startEdit(u)">{{ t("su_edit_btn") }}</button>
          <button class="su-btn su-btn-delete" @click="confirmDelete(u)">{{ t("su_delete_btn") }}</button>
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
          <option v-for="r in availableTargetRoles" :key="r.value" :value="r.value">{{ r.label }}</option>
        </select>
      </div>

      <div class="modal-field">
        <label>{{ t("su_full_name") }}</label>
        <input v-model="form.name" type="text" class="modal-input" autocomplete="off" />
      </div>
      <div class="modal-field">
        <label>{{ t("su_email") }}</label>
        <input v-model="form.email" type="email" class="modal-input" autocomplete="off" />
      </div>
      <div class="modal-field">
        <label>{{ t("su_phone") }}</label>
        <input v-model="form.phone" type="tel" class="modal-input" autocomplete="off" />
      </div>
      <div class="modal-field">
        <label>{{ t("su_username") }}</label>
        <input v-model="form.username" type="text" class="modal-input" autocomplete="off" :disabled="!!editingUser" />
      </div>
      <div v-if="!editingUser" class="modal-field">
        <label>{{ t("su_password") }}</label>
        <input v-model="form.password" type="password" class="modal-input" autocomplete="new-password" />
      </div>

      <div v-if="showWilayaSelect" class="modal-field">
        <label>{{ t("su_wilaya") }}</label>
        <div class="su-search-wrap">
          <input ref="wilayaInputRef" v-model="wilayaQuery" type="text" class="modal-input" :placeholder="t('su_search_wilaya')" autocomplete="off" @focus="fetchWilayas('')" />
          <Teleport v-if="wilayaOptions.length" to="body">
            <div class="su-dropdown" :style="wilayaDropdownStyle" @mousedown.prevent>
              <div v-for="w in wilayaOptions" :key="w.id" class="su-dropdown-item" @click="selectWilaya(w)">{{ w.name_fr }}</div>
            </div>
          </Teleport>
        </div>
      </div>

      <div v-if="showDairaSelect" class="modal-field">
        <label>{{ t("su_daira") }}</label>
        <div class="su-search-wrap">
          <input ref="dairaInputRef" v-model="dairaQuery" type="text" class="modal-input" :placeholder="dairaPlaceholder" autocomplete="off" :disabled="needWilayaFirst && !selectedWilayaId" @focus="fetchDairas('')" />
          <Teleport v-if="dairaOptions.length" to="body">
            <div class="su-dropdown" :style="dairaDropdownStyle" @mousedown.prevent>
              <div v-for="d in dairaOptions" :key="d.id" class="su-dropdown-item" @click="selectDaira(d)">{{ d.name_fr }}</div>
            </div>
          </Teleport>
        </div>
      </div>

      <div v-if="showCommuneSelect" class="modal-field">
        <label>{{ t("su_commune") }}</label>
        <div class="su-search-wrap">
          <input ref="communeInputRef" v-model="communeQuery" type="text" class="modal-input" :placeholder="t('su_search_commune')" autocomplete="off" @focus="fetchCommunes('')" />
          <Teleport v-if="communeOptions.length" to="body">
            <div class="su-dropdown" :style="communeDropdownStyle" @mousedown.prevent>
              <div v-for="c in communeOptions" :key="c.id" class="su-dropdown-item" @click="selectCommune(c)">{{ c.name_fr }}</div>
            </div>
          </Teleport>
        </div>
      </div>

      <div class="su-form-actions">
        <button class="modal-btn modal-btn-save" :disabled="loading" @click="submit">
          <span v-if="loading">…</span>
          <span v-else>{{ editingUser ? t("su_update_btn") : t("su_create_btn") }}</span>
        </button>
        <button v-if="editingUser" class="modal-btn modal-btn-cancel" @click="cancelEdit">{{ t("su_cancel_btn") }}</button>
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
            <button class="modal-btn modal-btn-cancel" @click="deleteTarget = null">{{ t("su_cancel_btn") }}</button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"
import type { UserRole } from "../../types"

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
type SearchOption = { id: number; name_fr: string }

function extractSearchOptions(payload: unknown): SearchOption[] {
  if (!payload || typeof payload !== "object") return []
  const items = (payload as { items?: unknown }).items
  if (!Array.isArray(items)) return []
  return items
    .map((item): SearchOption | null => {
      if (!item || typeof item !== "object") return null
      const id = Number((item as { id?: unknown }).id)
      const raw = item as { name_fr?: unknown; nameFr?: unknown; name_ar?: unknown; nameAr?: unknown; full_name?: unknown; fullName?: unknown }
      const label =
        (typeof raw.name_fr === "string" && raw.name_fr.trim()) ||
        (typeof raw.nameFr === "string" && raw.nameFr.trim()) ||
        (typeof raw.name_ar === "string" && raw.name_ar.trim()) ||
        (typeof raw.nameAr === "string" && raw.nameAr.trim()) ||
        (typeof raw.full_name === "string" && raw.full_name.trim()) ||
        (typeof raw.fullName === "string" && raw.fullName.trim()) ||
        null
      if (!Number.isInteger(id) || !label) return null
      return { id, name_fr: label }
    })
    .filter((item): item is SearchOption => item !== null)
}

// ── User list ────────────────────────────────────────────────────────────
const users = ref<ManageableUser[]>([])
const loadingUsers = ref(false)

async function fetchUsers() {
  loadingUsers.value = true
  try {
    const res = await apiFetch("/api/admin/users")
    users.value = (await res.json()) as ManageableUser[]
  } catch {
    // silent
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
    const res = await apiFetch(`/api/admin/users/${deleteTarget.value.user_id}`, { method: "DELETE" })
    if (res.ok) {
      showToast(t("su_delete_success"), "success")
      users.value = users.value.filter((u) => u.user_id !== deleteTarget.value!.user_id)
      deleteTarget.value = null
    }
  } catch {
    showToast(t("su_err_network"), "error")
  } finally {
    deleting.value = false
  }
}

// ── Target role ──────────────────────────────────────────────────────────
interface RoleOption { value: string; label: string }

const availableTargetRoles = computed<RoleOption[]>(() => {
  switch (role.value) {
    case "commune_user":
      return [{ value: "field_worker", label: t("su_role_field_worker") }]
    case "daira_admin":
      return [{ value: "commune_user", label: t("su_role_commune") }]
    case "wilaya_admin":
      return [{ value: "daira_admin", label: t("su_role_daira") }]
    case "national_admin":
      return [
        { value: "wilaya_admin", label: t("su_role_wilaya") },
        { value: "daira_admin", label: t("su_role_daira") },
      ]
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
    case "commune_user": return t("su_hint_commune")
    case "daira_admin": return t("su_hint_daira")
    case "wilaya_admin": return t("su_hint_wilaya")
    case "national_admin": return t("su_hint_national")
    default: return ""
  }
})

// ── Field visibility ─────────────────────────────────────────────────────
const showWilayaSelect = computed(() => role.value === "national_admin")
const showDairaSelect = computed(() => (role.value === "national_admin" && targetRole.value === "daira_admin") || role.value === "wilaya_admin")
const showCommuneSelect = computed(() => role.value === "daira_admin" || (role.value === "commune_user" && targetRole.value !== "field_worker"))
const needWilayaFirst = computed(() => role.value === "national_admin")

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

// ── Location state ───────────────────────────────────────────────────────
const wilayaQuery = ref("")
const wilayaOptions = ref<SearchOption[]>([])
const selectedWilayaId = ref<number | null>(null)
const dairaQuery = ref("")
const dairaOptions = ref<SearchOption[]>([])
const selectedDairaId = ref<number | null>(null)
const communeQuery = ref("")
const communeOptions = ref<SearchOption[]>([])
const selectedCommuneId = ref<number | null>(null)

const wilayaInputRef = ref<HTMLInputElement | null>(null)
const dairaInputRef = ref<HTMLInputElement | null>(null)
const communeInputRef = ref<HTMLInputElement | null>(null)

const positionTick = ref(0)
function updatePositions() { positionTick.value++ }
function getDropdownStyle(el: HTMLInputElement | null): Record<string, string> | null {
  void positionTick.value
  if (!el) return null
  const rect = el.getBoundingClientRect()
  return { position: "fixed", top: `${rect.bottom + 2}px`, left: `${rect.left}px`, width: `${rect.width}px` }
}
const wilayaDropdownStyle = computed(() => getDropdownStyle(wilayaInputRef.value))
const dairaDropdownStyle = computed(() => getDropdownStyle(dairaInputRef.value))
const communeDropdownStyle = computed(() => getDropdownStyle(communeInputRef.value))

onMounted(() => {
  window.addEventListener("resize", updatePositions)
  const main = document.querySelector(".settings-main")
  if (main) main.addEventListener("scroll", updatePositions)
  fetchUsers()
})
onUnmounted(() => {
  window.removeEventListener("resize", updatePositions)
  const main = document.querySelector(".settings-main")
  if (main) main.removeEventListener("scroll", updatePositions)
})

const dairaPlaceholder = computed(() => needWilayaFirst.value && !selectedWilayaId.value ? t("su_select_wilaya_first") : t("su_search_daira"))

watch(targetRole, () => {
  if (!editingUser.value) {
    wilayaQuery.value = ""
    selectedWilayaId.value = null
    wilayaOptions.value = []
    dairaQuery.value = ""
    selectedDairaId.value = null
    dairaOptions.value = []
    communeQuery.value = ""
    selectedCommuneId.value = null
    communeOptions.value = []
  }
})

// ── Location loaders ─────────────────────────────────────────────────────
let wilayaTimer: ReturnType<typeof setTimeout> | null = null
async function fetchWilayas(q: string) {
  if (wilayaTimer) clearTimeout(wilayaTimer)
  wilayaTimer = setTimeout(async () => {
    try {
      const res = await apiFetch(`/api/wilayas?search=${encodeURIComponent(q)}`)
      wilayaOptions.value = extractSearchOptions(await res.json())
    } catch { /* silent */ }
  }, 200)
}
watch(wilayaQuery, (q) => { fetchWilayas(q ?? "") })

let dairaTimer: ReturnType<typeof setTimeout> | null = null
async function fetchDairas(q: string) {
  if (needWilayaFirst.value && !selectedWilayaId.value) return
  if (dairaTimer) clearTimeout(dairaTimer)
  dairaTimer = setTimeout(async () => {
    try {
      const wilayaParam = selectedWilayaId.value ? `&wilaya_id=${selectedWilayaId.value}` : ""
      const res = await apiFetch(`/api/dairas?search=${encodeURIComponent(q)}${wilayaParam}`)
      dairaOptions.value = extractSearchOptions(await res.json())
    } catch { /* silent */ }
  }, 200)
}
watch(dairaQuery, (q) => { fetchDairas(q ?? "") })

let communeTimer: ReturnType<typeof setTimeout> | null = null
async function fetchCommunes(q: string) {
  if (communeTimer) clearTimeout(communeTimer)
  communeTimer = setTimeout(async () => {
    try {
      const res = await apiFetch(`/api/communes?search=${encodeURIComponent(q)}`)
      communeOptions.value = extractSearchOptions(await res.json())
    } catch { /* silent */ }
  }, 200)
}
watch(communeQuery, (q) => { fetchCommunes(q ?? "") })

onUnmounted(() => {
  if (wilayaTimer) clearTimeout(wilayaTimer)
  if (dairaTimer) clearTimeout(dairaTimer)
  if (communeTimer) clearTimeout(communeTimer)
})

function selectWilaya(w: { id: number; name_fr: string }) {
  selectedWilayaId.value = w.id
  wilayaQuery.value = w.name_fr
  wilayaOptions.value = []
  dairaQuery.value = ""
  selectedDairaId.value = null
  dairaOptions.value = []
}
function selectDaira(d: { id: number; name_fr: string }) {
  selectedDairaId.value = d.id
  dairaQuery.value = d.name_fr
  dairaOptions.value = []
}
function selectCommune(c: { id: number; name_fr: string }) {
  selectedCommuneId.value = c.id
  communeQuery.value = c.name_fr
  communeOptions.value = []
}

// ── Validation ───────────────────────────────────────────────────────────
function validate(): string | null {
  if (!form.value.name.trim()) return t("su_err_name")
  if (!form.value.email.includes("@")) return t("su_err_email")
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
  if (err) { errorMsg.value = err; return }

  loading.value = true
  try {
    if (editingUser.value) {
      const body: Record<string, unknown> = {}
      if (form.value.name.trim() !== editingUser.value.name) body.name = form.value.name.trim()
      if (form.value.email.trim() !== editingUser.value.email) body.email = form.value.email.trim()
      if (form.value.phone.trim() !== editingUser.value.phone) body.phone = form.value.phone.trim()
      if (targetRole.value !== editingUser.value.role) body.role = targetRole.value
      if (selectedWilayaId.value !== editingUser.value.wilaya_id) body.wilaya_id = selectedWilayaId.value
      if (selectedDairaId.value !== editingUser.value.daira_id) body.daira_id = selectedDairaId.value
      if (selectedCommuneId.value !== editingUser.value.commune_id) body.commune_id = selectedCommuneId.value

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
      const data = await res.json()
      if (res.ok) {
        successMsg.value = t("su_update_success")
        showToast(successMsg.value, "success")
        cancelEdit()
        await fetchUsers()
      } else {
        errorMsg.value = data.detail || data.error || t("su_err_update_generic")
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
      const data = await res.json()
      if (res.ok) {
        successMsg.value = t("su_success")
        showToast(successMsg.value, "success")
        form.value = { name: "", email: "", phone: "", username: "", password: "" }
        wilayaQuery.value = ""
        selectedWilayaId.value = null
        dairaQuery.value = ""
        selectedDairaId.value = null
        communeQuery.value = ""
        selectedCommuneId.value = null
        await fetchUsers()
      } else {
        errorMsg.value = data.detail || data.error || t("su_err_generic")
      }
    }
  } catch {
    errorMsg.value = t("su_err_network")
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.settings-users { display: flex; flex-direction: column; gap: 0; }
.su-section { margin-bottom: 1rem; }
.su-section-title { font-size: 0.95rem; font-weight: 600; color: var(--text-primary); margin: 0 0 0.75rem; }
.su-empty { font-size: 0.85rem; color: var(--text-muted); padding: 0.5rem 0; }
.su-user-row {
  display: flex; justify-content: space-between; align-items: center;
  padding: 0.6rem 0; border-bottom: 1px solid var(--glass-border, rgba(255,255,255,0.08));
}
.su-user-info { display: flex; flex-direction: column; gap: 0.15rem; min-width: 0; }
.su-user-name { font-size: 0.85rem; font-weight: 600; color: var(--text-primary); }
.su-user-meta { font-size: 0.75rem; color: var(--text-muted); }
.su-user-role { font-size: 0.72rem; color: var(--text-secondary); }
.su-user-actions { display: flex; gap: 0.4rem; flex-shrink: 0; }
.su-btn {
  padding: 0.3rem 0.7rem; font-size: 0.75rem; border-radius: 5px;
  cursor: pointer; border: 1px solid var(--glass-border);
  background: var(--glass-bg); color: var(--text-primary);
}
.su-btn:hover { background: var(--glass-bg-hover); }
.su-btn-delete { color: #ef4444; border-color: rgba(239,68,68,0.3); }
.su-btn-delete:hover { background: rgba(239,68,68,0.12); }
.su-divider { border: none; border-top: 1px solid var(--glass-border, rgba(255,255,255,0.1)); margin: 1rem 0; }
.su-feedback { font-size: 13px; padding: 8px 12px; border-radius: 6px; margin-bottom: 12px; line-height: 1.5; }
.su-success { background: rgba(16,185,129,0.15); color: #10b981; border: 1px solid rgba(16,185,129,0.3); }
.su-error { background: rgba(239,68,68,0.12); color: #ef4444; border: 1px solid rgba(239,68,68,0.25); }
.su-select { appearance: none; cursor: pointer; }
.su-search-wrap { position: relative; }
.su-dropdown { background: var(--modal-bg, #1a2035); border: 1px solid var(--glass-border, rgba(255,255,255,0.15)); border-radius: 8px; max-height: 180px; overflow-y: auto; z-index: 10001; box-shadow: 0 4px 20px rgba(0,0,0,0.35); }
.su-dropdown-item { padding: 9px 14px; font-size: 13px; color: var(--text-secondary); cursor: pointer; transition: background 0.15s; }
.su-dropdown-item:hover { background: var(--glass-bg-hover, rgba(255,255,255,0.07)); color: var(--text-primary); }
.su-form-actions { display: flex; gap: 0.5rem; margin-top: 0.75rem; }
.su-modal-overlay {
  position: fixed; inset: 0; z-index: 10002;
  display: flex; align-items: center; justify-content: center;
  background: rgba(0,0,0,0.5); backdrop-filter: blur(2px);
}
.su-modal {
  background: var(--modal-bg, #1a2035); border: 1px solid var(--glass-border);
  border-radius: 10px; padding: 1.5rem; max-width: 360px; width: 90%;
  box-shadow: 0 8px 32px rgba(0,0,0,0.3);
}
.su-modal p { margin: 0 0 1rem; font-size: 0.9rem; color: var(--text-primary); }
.su-modal-actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
</style>

<style>
.su-dropdown { position: fixed; background: var(--modal-bg, #1a2035); border: 1px solid var(--glass-border, rgba(255,255,255,0.15)); border-radius: 8px; max-height: 180px; overflow-y: auto; z-index: 10001; box-shadow: 0 4px 20px rgba(0,0,0,0.35); }
.su-dropdown-item { padding: 9px 14px; font-size: 13px; color: var(--text-secondary); cursor: pointer; transition: background 0.15s; }
.su-dropdown-item:hover { background: var(--glass-bg-hover, rgba(255,255,255,0.07)); color: var(--text-primary); }
</style>
