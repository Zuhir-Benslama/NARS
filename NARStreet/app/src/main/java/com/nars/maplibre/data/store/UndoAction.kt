package com.nars.maplibre.data.store

import com.nars.maplibre.data.model.NarsFeature

/**
 * Undo action matching web version (undo.ts)
 */
sealed class UndoAction {
    data class Delete(
        val feature: NarsFeature,
        val phaseKey: String
    ) : UndoAction()

    data class Update(
        val oldFeature: NarsFeature,
        val newFeature: NarsFeature,
        val phaseKey: String
    ) : UndoAction()
}