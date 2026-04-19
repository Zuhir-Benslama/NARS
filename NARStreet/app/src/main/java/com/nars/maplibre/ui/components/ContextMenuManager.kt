package com.nars.maplibre.ui.components

import android.content.Context
import android.view.Gravity
import android.widget.PopupMenu
import com.nars.maplibre.R
import com.nars.maplibre.data.model.NarsFeature

/**
 * Context menu matching web version (context-menu.ts)
 * Provides right-click/long-press menu for feature operations
 */
class ContextMenuManager(
    private val context: Context,
    private val onEditGeometry: (NarsFeature) -> Unit,
    private val onEditInfo: (NarsFeature) -> Unit,
    private val onDelete: (NarsFeature) -> Unit,
    private val onComputeDirections: ((NarsFeature) -> Unit)? = null
) {

    /**
     * Show context menu for a feature
     * @param feature The feature to show menu for
     * @param anchor The view to anchor the menu to
     */
    fun showContextMenu(feature: NarsFeature, anchor: android.view.View) {
        val popup = PopupMenu(context, anchor)
        popup.gravity = Gravity.CENTER

        // Always show edit options
        popup.menu.add(0, MENU_EDIT_GEOMETRY, 0, R.string.menu_edit_geometry)
        popup.menu.add(0, MENU_EDIT_INFO, 1, R.string.menu_edit_info)
        popup.menu.add(0, MENU_DELETE, 2, R.string.menu_delete)

        // Road-specific: compute directions
        if (feature.properties.phase == "roads" && onComputeDirections != null) {
            popup.menu.add(0, MENU_COMPUTE_DIRECTIONS, 3, R.string.menu_compute_directions)
        }

        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                MENU_EDIT_GEOMETRY -> onEditGeometry(feature)
                MENU_EDIT_INFO -> onEditInfo(feature)
                MENU_DELETE -> onDelete(feature)
                MENU_COMPUTE_DIRECTIONS -> onComputeDirections?.invoke(feature)
            }
            true
        }

        popup.show()
    }

    /**
     * Show context menu at specific coordinates (for map-based menus)
     */
    fun showContextMenuAtLocation(
        feature: NarsFeature,
        x: Float,
        y: Float
    ) {
        // For compose-based implementation, return a state that can be used
        // to show a compose dialog/dropdown instead
        // This is a placeholder for compose integration
    }

    companion object {
        const val MENU_EDIT_GEOMETRY = 1
        const val MENU_EDIT_INFO = 2
        const val MENU_DELETE = 3
        const val MENU_COMPUTE_DIRECTIONS = 4
    }
}

/**
 * Context menu state for Compose-based UI
 */
data class ContextMenuState(
    val isVisible: Boolean = false,
    val feature: NarsFeature? = null,
    val positionX: Float = 0f,
    val positionY: Float = 0f
)

/**
 * Dismiss context menu
 */
fun dismissContextMenu(state: ContextMenuState): ContextMenuState {
    return state.copy(isVisible = false, feature = null)
}