package com.nars.maplibre.ui.components

import android.content.Context
import android.view.Gravity
import android.widget.PopupMenu
import com.nars.maplibre.R
import com.nars.maplibre.data.model.NarsFeature

class ContextMenuManager(
    private val context: Context,
    private val onEditGeometry: (NarsFeature) -> Unit,
    private val onEditInfo: (NarsFeature) -> Unit,
    private val onDelete: (NarsFeature) -> Unit
) {

    fun showContextMenu(feature: NarsFeature, anchor: android.view.View) {
        val popup = PopupMenu(context, anchor)
        popup.gravity = Gravity.CENTER

        popup.menu.add(0, MENU_EDIT_GEOMETRY, 0, R.string.menu_edit_geometry)
        popup.menu.add(0, MENU_EDIT_INFO, 1, R.string.menu_edit_info)
        popup.menu.add(0, MENU_DELETE, 2, R.string.menu_delete)

        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                MENU_EDIT_GEOMETRY -> onEditGeometry(feature)
                MENU_EDIT_INFO -> onEditInfo(feature)
                MENU_DELETE -> onDelete(feature)
            }
            true
        }

        popup.show()
    }

    companion object {
        const val MENU_EDIT_GEOMETRY = 1
        const val MENU_EDIT_INFO = 2
        const val MENU_DELETE = 3
    }
}

data class ContextMenuState(
    val isVisible: Boolean = false,
    val feature: NarsFeature? = null,
    val positionX: Float = 0f,
    val positionY: Float = 0f
)

fun dismissContextMenu(state: ContextMenuState): ContextMenuState {
    return state.copy(isVisible = false, feature = null)
}
