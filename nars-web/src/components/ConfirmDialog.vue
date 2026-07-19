<template>
  <Teleport to="body">
    <Transition name="confirm">
      <div
        v-if="store.visible"
        ref="dialogRef"
        class="confirm-backdrop"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirmMsg"
        @click.self="store.cancel()"
      >
        <div class="confirm-dialog">
          <p id="confirmMsg" class="confirm-message">{{ store.message }}</p>
          <div class="confirm-actions">
            <button class="confirm-btn cancel" @click="store.cancel()">{{ t("cancel") }}</button>
            <button ref="okRef" class="confirm-btn ok" @click="store.confirm()">
              {{ store.okText }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { watch, ref, nextTick, onMounted, onUnmounted } from "vue"
import { useI18n } from "vue-i18n"
import { useConfirmStore } from "../stores/confirmStore"
import { useFocusTrap } from "../composables/useFocusTrap"

const { t } = useI18n()
const store = useConfirmStore()
const dialogRef = ref<HTMLElement | null>(null)
const okRef = ref<HTMLElement | null>(null)

useFocusTrap(dialogRef, () => store.visible)

watch(
  () => store.visible,
  async (v) => {
    if (v) {
      await nextTick()
      okRef.value?.focus()
    }
  },
)

function onKey(e: KeyboardEvent) {
  if (e.key === "Escape" && store.visible) {
    store.cancel()
  }
}

onMounted(() => {
  window.addEventListener("keydown", onKey)
})

onUnmounted(() => {
  window.removeEventListener("keydown", onKey)
})
</script>

<style scoped>
.confirm-backdrop {
  position: fixed;
  inset: 0;
  background: var(--overlay-bg);
  z-index: 10000;
  display: flex;
  align-items: center;
  justify-content: center;
}

.confirm-dialog {
  background: var(--modal-bg);
  color: var(--modal-text);
  padding: 24px;
  border-radius: 12px;
  max-width: 380px;
  width: 90%;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
  font-size: 15px;
  line-height: 1.5;
}

.confirm-message {
  margin: 0 0 20px;
}

.confirm-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.confirm-btn {
  padding: 8px 20px;
  border-radius: 8px;
  font-size: 14px;
  cursor: pointer;
  border: none;
}

.confirm-btn.cancel {
  border: 1px solid var(--btn-cancel-border);
  background: var(--btn-cancel-bg);
  color: var(--btn-cancel-text);
}

.confirm-btn.cancel:hover {
  background: var(--btn-cancel-hover);
}

.confirm-btn.ok {
  background: var(--danger-color);
  color: var(--text-primary);
  font-weight: 600;
}

.confirm-btn.ok:hover {
  background: var(--danger-hover);
}

.confirm-enter-active,
.confirm-leave-active {
  transition: opacity 0.15s;
}

.confirm-enter-from,
.confirm-leave-to {
  opacity: 0;
}
</style>
