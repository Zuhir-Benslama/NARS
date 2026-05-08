// ─── EDIT UI ──────────────────────────────────────────────────────────────────
// Floating Save button shown at bottom-center when in edit mode.
// Styled to match the app's glassmorphism UI pattern.

import { commitEditMode } from "./edit-mode"

let _editSaveBtn: HTMLElement | null = null

export function showEditSaveButton(): void {
  hideEditSaveButton()

  const btn = document.createElement("button")
  btn.id = "nars-edit-save"
  btn.className = "nars-edit-save-btn"
  btn.setAttribute("aria-label", "Save edited geometry")
  btn.setAttribute("title", "Save edited geometry")
  btn.innerHTML = `
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
    Save Geometry
  `
  _editSaveBtn = btn
  document.body.appendChild(btn)

  btn.addEventListener("click", () => {
    void commitEditMode()
  })
}

export function hideEditSaveButton(): void {
  if (_editSaveBtn) {
    _editSaveBtn.remove()
    _editSaveBtn = null
  }
  document.getElementById("nars-edit-save")?.remove()
}
