<template>
  <div class="settings-users">
    <!-- Existing users list -->
    <div class="su-section">
      <h3 class="su-section-title">{{ t("su_user_list_title") }} ({{ users.length }})</h3>
      <div v-if="fetchUsersError" class="su-feedback su-error">{{ fetchUsersError }}</div>
      <div v-else-if="loadingUsers" class="su-empty">{{ t("admin.loading") }}</div>
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
    <SettingsUsersForm
      :role="role"
      :editing-user="editingUser"
      @saved="handleSaved"
      @cancel="editingUser = null"
    />

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
import { ref, computed, onMounted } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../../stores/appStore"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"
import { getUserMessageKey } from "../../lib/errors"
import { debugWarn } from "../../utils/debug"
import type { ManageableUser, UserRole } from "../../types"
import SettingsUsersForm from "./SettingsUsersForm.vue"

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

// ── Edit ─────────────────────────────────────────────────────────────────
const editingUser = ref<ManageableUser | null>(null)

function startEdit(u: ManageableUser) {
  editingUser.value = u
}

function handleSaved() {
  editingUser.value = null
  void fetchUsers()
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

onMounted(fetchUsers)
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
.su-error {
  background: var(--danger-bg);
  color: var(--danger-color);
  border: 1px solid var(--danger-border);
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
