// ─── DRAFT REVIEW ACTIONS ─────────────────────────────────────────────────────
// Accept / reject / delete actions for AI drafts. Successful reviews remove the
// draft from the review queue; accept additionally materializes the production
// feature server-side (roads row, later house entrances for buildings).

import { useDraftsStore } from "../../stores/draftsStore"
import { acceptDraft, rejectDraft, deleteDraft } from "../../api/drafts"
import { showToast, showConfirm } from "../../lib/toast"
import { t } from "../../i18n"
import { debugError } from "../../utils/debug"
import { getUserMessageKey } from "../../lib/errors"

export type DraftReviewAction = "accept" | "reject" | "delete"

const CONFIRM_KEYS: Record<Exclude<DraftReviewAction, "accept">, string> = {
  reject: "draft_confirm_reject",
  delete: "draft_confirm_delete",
}

export async function reviewDraft(draftId: string, action: DraftReviewAction): Promise<boolean> {
  const draftsStore = useDraftsStore()

  if (action !== "accept") {
    const confirmed = await showConfirm(t(CONFIRM_KEYS[action]))
    if (!confirmed) return false
  }

  try {
    const call =
      action === "accept"
        ? () => acceptDraft(draftId)
        : action === "reject"
          ? () => rejectDraft(draftId)
          : () => deleteDraft(draftId)
    await call()

    draftsStore.removeDraft(draftId)
    const toastKey =
      action === "accept"
        ? "draft_accepted"
        : action === "reject"
          ? "draft_rejected"
          : "draft_deleted"
    showToast(t(toastKey), "success")
    return true
  } catch (err) {
    debugError(`[DRAFTS] ${action} failed:`, err)
    showToast(t("draft_action_failed", { error: t(getUserMessageKey(err)) }), "error")
    return false
  }
}
