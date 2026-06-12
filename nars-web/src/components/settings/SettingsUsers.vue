<template>
  <div class="settings-users">
    <p class="settings-hint">{{ hintText }}</p>

    <!-- Success / error feedback -->
    <div v-if="successMsg" class="su-feedback su-success">{{ successMsg }}</div>
    <div v-if="errorMsg" class="su-feedback su-error">{{ errorMsg }}</div>

    <!-- Role selector (only shown to national_admin who can create either wilaya or daira admins) -->
    <div v-if="availableTargetRoles.length > 1" class="modal-field">
      <label>{{ t("su_account_type") }}</label>
      <select v-model="targetRole" class="modal-input su-select">
        <option v-for="r in availableTargetRoles" :key="r.value" :value="r.value">
          {{ r.label }}
        </option>
      </select>
    </div>

    <!-- Personal details -->
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
      <input v-model="form.username" type="text" class="modal-input" autocomplete="off" />
    </div>
    <div class="modal-field">
      <label>{{ t("su_password") }}</label>
      <input
        v-model="form.password"
        type="password"
        class="modal-input"
        autocomplete="new-password"
      />
    </div>

    <!-- Wilaya selector (national_admin creating a wilaya_admin) -->
    <div v-if="showWilayaSelect" class="modal-field">
      <label>{{ t("su_wilaya") }}</label>
      <div class="su-search-wrap">
        <input
          ref="wilayaInputRef"
          v-model="wilayaQuery"
          type="text"
          class="modal-input"
          :placeholder="t('su_search_wilaya')"
          autocomplete="off"
          @focus="fetchWilayas('')"
        />
        <Teleport v-if="wilayaOptions.length" to="body">
          <div class="su-dropdown" :style="wilayaDropdownStyle" @mousedown.prevent>
            <div
              v-for="w in wilayaOptions"
              :key="w.id"
              class="su-dropdown-item"
              @click="selectWilaya(w)"
            >
              {{ w.name_fr }}
            </div>
          </div>
        </Teleport>
      </div>
    </div>

    <!-- Daira selector (wilaya_admin or national_admin creating a daira_admin) -->
    <div v-if="showDairaSelect" class="modal-field">
      <label>{{ t("su_daira") }}</label>
      <div class="su-search-wrap">
        <input
          ref="dairaInputRef"
          v-model="dairaQuery"
          type="text"
          class="modal-input"
          :placeholder="dairaPlaceholder"
          autocomplete="off"
          :disabled="needWilayaFirst && !selectedWilayaId"
          @focus="fetchDairas('')"
        />
        <Teleport v-if="dairaOptions.length" to="body">
          <div class="su-dropdown" :style="dairaDropdownStyle" @mousedown.prevent>
            <div
              v-for="d in dairaOptions"
              :key="d.id"
              class="su-dropdown-item"
              @click="selectDaira(d)"
            >
              {{ d.name_fr }}
            </div>
          </div>
        </Teleport>
      </div>
    </div>

    <!-- Commune selector (daira_admin creating a commune_user) -->
    <div v-if="showCommuneSelect" class="modal-field">
      <label>{{ t("su_commune") }}</label>
      <div class="su-search-wrap">
        <input
          ref="communeInputRef"
          v-model="communeQuery"
          type="text"
          class="modal-input"
          :placeholder="t('su_search_commune')"
          autocomplete="off"
          @focus="fetchCommunes('')"
        />
        <Teleport v-if="communeOptions.length" to="body">
          <div class="su-dropdown" :style="communeDropdownStyle" @mousedown.prevent>
            <div
              v-for="c in communeOptions"
              :key="c.id"
              class="su-dropdown-item"
              @click="selectCommune(c)"
            >
              {{ c.name_fr }}
            </div>
          </div>
        </Teleport>
      </div>
    </div>

    <button class="modal-btn modal-btn-save" :disabled="loading" @click="createUser">
      <span v-if="loading">…</span>
      <span v-else>{{ t("su_create_btn") }}</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"
import type { UserRole } from "../../types"

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
      const raw = item as {
        name_fr?: unknown
        nameFr?: unknown
        name_ar?: unknown
        nameAr?: unknown
        full_name?: unknown
        fullName?: unknown
      }
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

// ── Target role ───────────────────────────────────────────────────────────────

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
  targetRole.value = opts[0]?.value ?? ""
})

// ── Hint text ─────────────────────────────────────────────────────────────────
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

// ── Field visibility ──────────────────────────────────────────────────────────

const showWilayaSelect = computed(() => role.value === "national_admin")
const showDairaSelect = computed(
  () =>
    (role.value === "national_admin" && targetRole.value === "daira_admin") ||
    role.value === "wilaya_admin",
)
const showCommuneSelect = computed(
  () =>
    role.value === "daira_admin" ||
    (role.value === "commune_user" && targetRole.value !== "field_worker"),
)
const needWilayaFirst = computed(() => role.value === "national_admin")

// ── Form state ────────────────────────────────────────────────────────────────
const form = ref({
  name: "",
  email: "",
  phone: "",
  username: "",
  password: "",
})
const loading = ref(false)
const successMsg = ref("")
const errorMsg = ref("")

// Location state
const wilayaQuery = ref("")
const wilayaOptions = ref<SearchOption[]>([])
const selectedWilayaId = ref<number | null>(null)
const dairaQuery = ref("")
const dairaOptions = ref<SearchOption[]>([])
const selectedDairaId = ref<number | null>(null)
const communeQuery = ref("")
const communeOptions = ref<SearchOption[]>([])
const selectedCommuneId = ref<number | null>(null)

// Input refs for dropdown positioning
const wilayaInputRef = ref<HTMLInputElement | null>(null)
const dairaInputRef = ref<HTMLInputElement | null>(null)
const communeInputRef = ref<HTMLInputElement | null>(null)

const positionTick = ref(0)
function updatePositions() {
  positionTick.value++
}

function getDropdownStyle(el: HTMLInputElement | null): Record<string, string> | null {
  void positionTick.value
  if (!el) return null
  const rect = el.getBoundingClientRect()
  return {
    position: "fixed",
    top: `${rect.bottom + 2}px`,
    left: `${rect.left}px`,
    width: `${rect.width}px`,
  }
}
const wilayaDropdownStyle = computed(() => getDropdownStyle(wilayaInputRef.value))
const dairaDropdownStyle = computed(() => getDropdownStyle(dairaInputRef.value))
const communeDropdownStyle = computed(() => getDropdownStyle(communeInputRef.value))

onMounted(() => {
  window.addEventListener("resize", updatePositions)
  const main = document.querySelector(".settings-main")
  if (main) main.addEventListener("scroll", updatePositions)
})
onUnmounted(() => {
  window.removeEventListener("resize", updatePositions)
  const main = document.querySelector(".settings-main")
  if (main) main.removeEventListener("scroll", updatePositions)
})

const dairaPlaceholder = computed(() =>
  needWilayaFirst.value && !selectedWilayaId.value
    ? t("su_select_wilaya_first")
    : t("su_search_daira"),
)

// Reset location selections when targetRole changes
watch(targetRole, () => {
  wilayaQuery.value = ""
  selectedWilayaId.value = null
  wilayaOptions.value = []
  dairaQuery.value = ""
  selectedDairaId.value = null
  dairaOptions.value = []
  communeQuery.value = ""
  selectedCommuneId.value = null
  communeOptions.value = []
})

// ── Location loaders ──────────────────────────────────────────────────────────
let wilayaTimer: ReturnType<typeof setTimeout> | null = null
async function fetchWilayas(q: string) {
  if (wilayaTimer) clearTimeout(wilayaTimer)
  wilayaTimer = setTimeout(async () => {
    try {
      const res = await apiFetch(`/api/wilayas?search=${encodeURIComponent(q)}`)
      const data: unknown = await res.json()
      wilayaOptions.value = extractSearchOptions(data)
    } catch {
      // silent
    }
  }, 200)
}
watch(wilayaQuery, (q) => {
  fetchWilayas(q ?? "")
})

let dairaTimer: ReturnType<typeof setTimeout> | null = null
async function fetchDairas(q: string) {
  if (needWilayaFirst.value && !selectedWilayaId.value) return
  if (dairaTimer) clearTimeout(dairaTimer)
  dairaTimer = setTimeout(async () => {
    try {
      const wilayaParam = selectedWilayaId.value ? `&wilaya_id=${selectedWilayaId.value}` : ""
      const res = await apiFetch(`/api/dairas?search=${encodeURIComponent(q)}${wilayaParam}`)
      const data: unknown = await res.json()
      dairaOptions.value = extractSearchOptions(data)
    } catch {
      /* silent */
    }
  }, 200)
}
watch(dairaQuery, (q) => {
  fetchDairas(q ?? "")
})

let communeTimer: ReturnType<typeof setTimeout> | null = null
async function fetchCommunes(q: string) {
  if (communeTimer) clearTimeout(communeTimer)
  communeTimer = setTimeout(async () => {
    try {
      const res = await apiFetch(`/api/communes?search=${encodeURIComponent(q)}`)
      const data: unknown = await res.json()
      communeOptions.value = extractSearchOptions(data)
    } catch {
      /* silent */
    }
  }, 200)
}
watch(communeQuery, (q) => {
  fetchCommunes(q ?? "")
})

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

// ── Validation ────────────────────────────────────────────────────────────────
function validate(): string | null {
  if (!form.value.name.trim()) return t("su_err_name")
  if (!form.value.email.includes("@")) return t("su_err_email")
  if (!form.value.phone.trim()) return t("su_err_phone")
  if (form.value.username.length < 3) return t("su_err_username")
  if (form.value.password.length < 8) return t("su_err_password")
  if (showWilayaSelect.value && !selectedWilayaId.value) return t("su_err_wilaya")
  if (showDairaSelect.value && !selectedDairaId.value) return t("su_err_daira")
  if (showCommuneSelect.value && !selectedCommuneId.value) return t("su_err_commune")
  return null
}

// ── Submit ────────────────────────────────────────────────────────────────────
async function createUser() {
  successMsg.value = ""
  errorMsg.value = ""

  const err = validate()
  if (err) {
    errorMsg.value = err
    return
  }

  loading.value = true
  try {
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
      successMsg.value = t("su_success", { role: targetRole.value })
      showToast(successMsg.value, "success")
      form.value = { name: "", email: "", phone: "", username: "", password: "" }
      wilayaQuery.value = ""
      selectedWilayaId.value = null
      dairaQuery.value = ""
      selectedDairaId.value = null
      communeQuery.value = ""
      selectedCommuneId.value = null
    } else {
      errorMsg.value = data.detail || data.error || t("su_err_generic")
    }
  } catch {
    errorMsg.value = t("su_err_network")
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
.su-feedback {
  font-size: 13px;
  padding: 8px 12px;
  border-radius: 6px;
  margin-bottom: 12px;
  line-height: 1.5;
}
.su-success {
  background: rgba(16, 185, 129, 0.15);
  color: #10b981;
  border: 1px solid rgba(16, 185, 129, 0.3);
}
.su-error {
  background: rgba(239, 68, 68, 0.12);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.25);
}
.su-select {
  appearance: none;
  cursor: pointer;
}
.su-search-wrap {
  position: relative;
}
.su-dropdown {
  background: var(--modal-bg, #1a2035);
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 8px;
  max-height: 180px;
  overflow-y: auto;
  z-index: 10001;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.35);
}
.su-dropdown-item {
  padding: 9px 14px;
  font-size: 13px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 0.15s;
}
.su-dropdown-item:hover {
  background: var(--glass-bg-hover, rgba(255, 255, 255, 0.07));
  color: var(--text-primary);
}
</style>

<style>
.su-dropdown {
  position: fixed;
  background: var(--modal-bg, #1a2035);
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 8px;
  max-height: 180px;
  overflow-y: auto;
  z-index: 10001;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.35);
}
.su-dropdown-item {
  padding: 9px 14px;
  font-size: 13px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 0.15s;
}
.su-dropdown-item:hover {
  background: var(--glass-bg-hover, rgba(255, 255, 255, 0.07));
  color: var(--text-primary);
}
</style>
