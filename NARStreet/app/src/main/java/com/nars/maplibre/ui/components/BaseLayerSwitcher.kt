package com.nars.maplibre.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Layers
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.sp
import com.nars.maplibre.R
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.ui.theme.DropdownBackground
import com.nars.maplibre.ui.theme.DropdownHover
import com.nars.maplibre.ui.theme.DropdownItem
import com.nars.maplibre.ui.theme.GlassBackground
import com.nars.maplibre.ui.theme.GlassBorder
import com.nars.maplibre.ui.theme.TextPrimary
import com.nars.maplibre.ui.theme.TextSecondary

/**
 * Tile control / Base layer selector - Icon only
 * Click to cycle through layers
 */
@Composable
fun TileControl(
    currentLayer: BaseLayerType,
    onLayerSelected: (BaseLayerType) -> Unit,
    modifier: Modifier = Modifier
) {
    var dropdownExpanded by remember { mutableStateOf(false) }

    Box(modifier = modifier) {
        // Icon button
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(10.dp))
                .background(GlassBackground.copy(alpha = 0.7f))
                .clickable { dropdownExpanded = true },
            contentAlignment = Alignment.Center
        ) {
            Icon(
                imageVector = Icons.Default.Layers,
                contentDescription = stringResource(R.string.map_base_layers),
                tint = TextPrimary,
                modifier = Modifier.size(20.dp)
            )
        }

        // Dropdown menu
        DropdownMenu(
            expanded = dropdownExpanded,
            onDismissRequest = { dropdownExpanded = false },
            modifier = Modifier
                .background(
                    color = DropdownBackground,
                    shape = RoundedCornerShape(12.dp)
                )
        ) {
            BaseLayerType.entries.forEach { layer ->
                DropdownMenuItem(
                    text = {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            // Layer color indicator
                            Box(
                                modifier = Modifier
                                    .size(20.dp)
                                    .clip(RoundedCornerShape(4.dp))
                                    .background(
                                        when (layer) {
                                            BaseLayerType.SATELLITE -> Color(0xFF1a1a1a)
                                            BaseLayerType.STREET -> Color(0xFFf0f0f0)
                                            BaseLayerType.LIGHT -> Color(0xFFffffff)
                                            BaseLayerType.DARK -> Color(0xFF1f1f1f)
                                        }
                                    )
                            )

                            Text(
                                text = layer.displayName,
                                fontSize = 13.sp,
                                color = DropdownItem
                            )
                        }
                    },
                    trailingIcon = {
                        if (layer == currentLayer) {
                            Icon(
                                imageVector = Icons.Default.Check,
                                contentDescription = stringResource(R.string.map_selected),
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(18.dp)
                            )
                        }
                    },
                    onClick = {
                        onLayerSelected(layer)
                        dropdownExpanded = false
                    }
                )
            }
        }
    }
}

/**
 * Compact base layer selector (horizontal)
 * Matches nars-vite-maplibre compact design
 */
@Composable
fun CompactBaseLayerSelector(
    currentLayer: BaseLayerType,
    onLayerSelected: (BaseLayerType) -> Unit,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .clip(RoundedCornerShape(20.dp))
            .background(GlassBackground.copy(alpha = 0.7f))
            .padding(4.dp),
        horizontalArrangement = Arrangement.spacedBy(4.dp)
    ) {
        BaseLayerType.entries.forEach { layer ->
            CompactLayerButton(
                layer = layer,
                isSelected = currentLayer == layer,
                onClick = { onLayerSelected(layer) }
            )
        }
    }
}

/**
 * Compact layer button
 */
@Composable
private fun CompactLayerButton(
    layer: BaseLayerType,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    Box(
        modifier = Modifier
            .size(36.dp)
            .clip(RoundedCornerShape(18.dp))
            .background(
                if (isSelected) {
                    MaterialTheme.colorScheme.primary
                } else {
                    GlassBackground.copy(alpha = 0.5f)
                }
            )
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Box(
            modifier = Modifier
                .size(24.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(
                    when (layer) {
                        BaseLayerType.SATELLITE -> Color(0xFF1a1a1a)
                        BaseLayerType.STREET -> Color(0xFFf0f0f0)
                        BaseLayerType.LIGHT -> Color(0xFFffffff)
                        BaseLayerType.DARK -> Color(0xFF1f1f1f)
                    }
                )
        )
    }
}
