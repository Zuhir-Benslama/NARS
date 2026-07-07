<template>
  <Teleport to="body">
    <div
      v-if="store.visible"
      ref="menuRef"
      class="nars-ctx-menu"
      :style="{ left: adjustedX + 'px', top: adjustedY + 'px' }"
      role="menu"
    >
      <template v-for="(item, i) in store.items" :key="i">
        <div v-if="item.separator" class="ctx-separator" />
        <div
          v-else
          :class="['ctx-item', { 'ctx-danger': item.danger }]"
          role="menuitem"
          @click="handleClick(item)"
        >
          {{ item.label }}
        </div>
      </template>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue"
import { useContextMenuStore } from "../stores/contextMenuStore"

const store = useContextMenuStore()
const menuRef = ref<HTMLElement | null>(null)

const adjustedX = computed(() => {
  if (!store.visible) return 0
  const w = menuRef.value?.offsetWidth || 180
  return store.x + w > window.innerWidth ? store.x - w : store.x
})

const adjustedY = computed(() => {
  if (!store.visible) return 0
  const h = menuRef.value?.offsetHeight || 100
  return store.y + h > window.innerHeight ? store.y - h : store.y
})

watch(
  () => store.visible,
  (v) => {
    if (v) {
      document.addEventListener("click", onDocClick, true)
      document.addEventListener("contextmenu", onDocClick, true)
      document.addEventListener("keydown", onKeyDown, true)
    } else {
      document.removeEventListener("click", onDocClick, true)
      document.removeEventListener("contextmenu", onDocClick, true)
      document.removeEventListener("keydown", onKeyDown, true)
    }
  },
)

function onDocClick() {
  store.hide()
}

function onKeyDown(e: KeyboardEvent) {
  if (e.key === "Escape") store.hide()
}

function handleClick(item: { onClick?: () => void }) {
  store.hide()
  item.onClick?.()
}
</script>

<style scoped>
.nars-ctx-menu {
  position: fixed;
  z-index: 100000;
  background: var(--dropdown-bg, #fff);
  border: 1px solid var(--dropdown-border, #e5e7eb);
  border-radius: 8px;
  padding: 4px 0;
  min-width: 160px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
  backdrop-filter: var(--dropdown-bg-blur, none);
}
.ctx-item {
  padding: 8px 14px;
  font-size: 13px;
  cursor: pointer;
  white-space: nowrap;
  transition: background 0.12s;
  color: var(--dropdown-item);
}
.ctx-item:hover {
  background: var(--dropdown-hover, #f3f4f6);
}
.ctx-danger {
  color: #ef4444;
}
.ctx-separator {
  border-top: 1px solid var(--dropdown-border, #eee);
  margin: 2px 0;
}
</style>
