<template>
  <div class="modal-field">
    <label>{{ label }}</label>
    <div class="lss-search-wrap">
      <input
        ref="inputRef"
        v-model="query"
        type="text"
        class="modal-input"
        :placeholder="placeholder"
        autocomplete="off"
        :disabled="disabled"
        @focus="runSearch('')"
      />
      <Teleport v-if="options.length" to="body">
        <div class="lss-dropdown" :style="dropdownStyle" @mousedown.prevent>
          <div
            v-for="opt in options"
            :key="opt.id"
            class="lss-dropdown-item"
            @click="selectOption(opt)"
          >
            {{ opt.name_fr }}
          </div>
        </div>
      </Teleport>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from "vue"
import { apiFetch } from "../../api"
import { debugWarn } from "../../utils/debug"

const DEBOUNCE_MS = 200

interface SearchOption {
  id: number
  name_fr: string
}

const props = defineProps<{
  modelValue: number | null
  label: string
  placeholder: string
  endpoint: string | ((query: string) => string)
  disabled?: boolean
}>()

const emit = defineEmits<{
  "update:modelValue": [value: number | null]
}>()

const query = ref("")
const options = ref<SearchOption[]>([])
const inputRef = ref<HTMLInputElement | null>(null)
const positionTick = ref(0)

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

// ── Debounced search ───────────────────────────────────────────────────────
let timer: ReturnType<typeof setTimeout> | null = null
// Monotonic generation counter — supersedes out-of-order responses so a slow
// result for an older query can never overwrite a newer one.
let searchGen = 0
// Suppresses the debounced re-search triggered by the programmatic query
// write in selectOption(), which would otherwise repopulate the dropdown over
// the just-selected value ~200 ms later.
let suppressNextSearch = false

function runSearch(q: string) {
  if (timer) clearTimeout(timer)
  const gen = ++searchGen
  timer = setTimeout(async () => {
    try {
      const url =
        typeof props.endpoint === "function"
          ? props.endpoint(q)
          : `${props.endpoint}?search=${encodeURIComponent(q)}`
      const res = await apiFetch(url)
      const items = extractSearchOptions(await res.json())
      if (gen !== searchGen) return
      options.value = items
    } catch (e) {
      if (gen === searchGen) debugWarn("[LocationSearchSelect] search failed:", e)
    }
  }, DEBOUNCE_MS)
}

function cleanup() {
  if (timer) clearTimeout(timer)
  searchGen++
}

// ── Dropdown positioning ────────────────────────────────────────────────────
function updatePosition() {
  positionTick.value++
}

function getDropdownStyle(): Record<string, string> | null {
  void positionTick.value
  if (!inputRef.value) return null
  const rect = inputRef.value.getBoundingClientRect()
  return {
    position: "fixed",
    top: `${rect.bottom + 2}px`,
    left: `${rect.left}px`,
    width: `${rect.width}px`,
  }
}

const dropdownStyle = computed(() => getDropdownStyle())

onMounted(() => {
  window.addEventListener("resize", updatePosition)
})
onUnmounted(() => {
  window.removeEventListener("resize", updatePosition)
  cleanup()
})

// ── Watchers ────────────────────────────────────────────────────────────────
watch(query, (q) => {
  if (suppressNextSearch) {
    suppressNextSearch = false
    return
  }
  runSearch(q ?? "")
})

function selectOption(opt: SearchOption) {
  emit("update:modelValue", opt.id)
  suppressNextSearch = true
  query.value = opt.name_fr
  options.value = []
  // If the query didn't actually change the watcher never fires; clear the
  // flag so it can't swallow the next genuine search.
  setTimeout(() => {
    suppressNextSearch = false
  }, 0)
}

function reset() {
  cleanup()
  query.value = ""
  options.value = []
}

defineExpose({ reset })
</script>

<style scoped>
.lss-search-wrap {
  position: relative;
}
.lss-dropdown {
  background: var(--modal-bg, #1a2035);
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 8px;
  max-height: 180px;
  overflow-y: auto;
  z-index: 10001;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.35);
}
.lss-dropdown-item {
  padding: 9px 14px;
  font-size: 13px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 0.15s;
}
.lss-dropdown-item:hover {
  background: var(--glass-bg-hover, rgba(255, 255, 255, 0.07));
  color: var(--text-primary);
}
</style>
