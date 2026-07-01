# NARS Web — Code Quality Issues

## ✅ Fixed (2026-07-01)

All 14 items from the initial audit have been resolved.

- [x] **🔴 XSS in map popups** — `styles.ts`. `escapeHtml` now called on all user-supplied values in `buildPopupContent`.
- [x] **🔴 Keyboard handler on non-focusable `<div>`** — `FeatureModal.vue`. Moved to `window` listener via `onMounted`/`onUnmounted`.
- [x] **🟠 Silent error swallowing** — `useApiFetch.ts`. Added `logError(createNetworkError(...))` in catch.
- [x] **🟠 Spurious `disableSnapping`/`enableSnapping`** — `draw-events.ts`. Replaced with single `resetSnapping()` helper.
- [x] **🟠 Promise queue race condition** — `modalStore.ts`. Switched to single-pending-promise model.
- [x] **🟠 Dev-only global coupling** — `feature-modal.ts`. `fetchRoadSide` now accepts explicit `geometry` parameter.
- [x] **🟠 Side effect in `<script setup>`** — `FieldPanel.vue`. `fetchFeatures()` moved to `onMounted`.
- [x] **🟠 Unhandled hanging promise** — `map-init.ts`. Added 10s timeout to `style.load`.
- [x] **🔵 Dead router guard** — `router/index.ts`. Removed.
- [x] **🔵 Fragile role check** — `appStore.ts`. Changed to explicit admin allowlist.
- [x] **🔵 Mixed async patterns** — `feature-persistence.ts`. Switched to all-`await`.
- [x] **🔵 Dev globals on `window`** — `__narsCurrentGeometry` removed from `feature-modal.ts`; `ctx.map` global already absent.
- [x] **🔵 Empty state raw key** — `FieldPanel.vue`. Now looks up `tabs.find().label`.

## 🔵 Remaining (lower priority)

- [ ] **Hardcoded tile URLs** — `src/config/index.ts:53-72`. Should be environment variables.

## ✅ Fixed (2026-07-01)

- [x] **💡 Extract `FeatureModal.vue` validation** — moved to `useFeatureValidation` composable.
- [x] **💡 Discriminated unions for `FeatureData`** — per-type interfaces no longer extend bag-of-optional-fields; `FeatureDataByType` provides proper narrowing.
- [x] **💡 Add AbortController to `useApiFetch`** — `apiFetch` merges external signal with timeout controller via `AbortSignal.any()`.
- [x] **💡 Remove barrel file** — `src/map/features/features.ts` removed; consumers import directly from submodules.
