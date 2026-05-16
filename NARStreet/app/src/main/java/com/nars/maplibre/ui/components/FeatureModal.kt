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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.window.Dialog
import com.nars.maplibre.R
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.ui.theme.GlassBackground
import com.nars.maplibre.utils.ValidationResult
import com.nars.maplibre.utils.validateFeatureProperties

@Composable
fun FeatureValidationModal(
    feature: NarsFeature,
    phase: PhaseDefinition,
    onSave: (NarsFeature) -> Unit,
    onDismiss: () -> Unit
) {
    var props by remember { mutableStateOf(feature.properties) }
    var validationErrors by remember { mutableStateOf<Map<String, String>>(emptyMap()) }

    Dialog(onDismissRequest = onDismiss) {
        Card(
            modifier = Modifier.fillMaxWidth().padding(8.dp),
            shape = RoundedCornerShape(16.dp),
            colors = CardDefaults.cardColors(containerColor = GlassBackground)
        ) {
            Column(
                modifier = Modifier.fillMaxWidth().padding(16.dp).verticalScroll(rememberScrollState())
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = when (phase.key) {
                            "roads" -> stringResource(R.string.feature_road_attributes)
                            "houseEntrances" -> stringResource(R.string.feature_entrance_check)
                            "namingPanels" -> stringResource(R.string.feature_panel_check)
                            else -> stringResource(R.string.feature_details)
                        },
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )
                    Box(
                        modifier = Modifier.size(32.dp).clip(RoundedCornerShape(8.dp))
                            .background(MaterialTheme.colorScheme.surfaceVariant)
                            .clickable { onDismiss() },
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            imageVector = Icons.Default.Close,
                            contentDescription = stringResource(R.string.map_close),
                            tint = MaterialTheme.colorScheme.onSurface
                        )
                    }
                }

                Spacer(modifier = Modifier.height(16.dp))

                // Road name - bold and prominent
                val roadName = feature.properties.name ?: stringResource(R.string.feature_unnamed_road)
                Text(
                    text = roadName,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )

                // Show coordinates for reference
                val coords = when (val geom = feature.geometry) {
                    is com.nars.maplibre.data.model.LineStringGeometry -> {
                        val c = geom.coordinates.chunked(2)
                        if (c.isNotEmpty()) "Lat: ${c[0][1].formatDecimal(6)}, Lng: ${c[0][0].formatDecimal(6)}" else null
                    }
                    is com.nars.maplibre.data.model.PointGeometry -> {
                        if (geom.coordinates.size >= 2) "Lat: ${geom.coordinates[1].formatDecimal(6)}, Lng: ${geom.coordinates[0].formatDecimal(6)}" else null
                    }
                    else -> null
                }
                coords?.let {
                    Text(
                        text = it,
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }

                Spacer(modifier = Modifier.height(16.dp))

                when (phase.key) {
                    "roads" -> RoadsValidationFields(props = props, onPropsChanged = { props = it })
                    "houseEntrances" -> HouseEntranceValidationFields(props = props, onPropsChanged = { props = it })
                    "namingPanels" -> NamingPanelValidationFields(props = props, onPropsChanged = { props = it })
                }

                Spacer(modifier = Modifier.height(20.dp))

                validationErrors.entries.forEach { (field, msg) ->
                    Text(
                        text = "$field: $msg",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.error,
                        modifier = Modifier.fillMaxWidth()
                    )
                }

                Spacer(modifier = Modifier.height(8.dp))

                Button(
                    onClick = {
                        val result = validateFeatureProperties(props, phase)
                        if (result.valid) {
                            onSave(feature.copy(properties = props))
                            onDismiss()
                        } else {
                            validationErrors = result.errors
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary)
                ) {
                    Text(stringResource(R.string.feature_save))
                }
            }
        }
    }
}

@Composable
private fun RoadsValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationRadioGroup(
            label = "Road Traffic",
            options = listOf("high", "medium", "low"),
            selectedValue = props.roadTraffic,
            onValueChanged = { onPropsChanged(props.copy(roadTraffic = it)) }
        )

        ValidationRadioGroup(
            label = "Traditional Activity",
            options = listOf("high", "medium", "low"),
            selectedValue = props.tradActivity,
            onValueChanged = { onPropsChanged(props.copy(tradActivity = it)) }
        )

        ValidationNumberField(
            label = "Number of Lanes",
            value = props.numLanes?.toString() ?: "",
            onValueChanged = { onPropsChanged(props.copy(numLanes = it.toIntOrNull())) }
        )

        ValidationSwitch(
            label = "Has Median",
            checked = props.hasMedian ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasMedian = it)) }
        )

        ValidationSwitch(
            label = "Has Vegetation",
            checked = props.hasVegetation ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasVegetation = it)) }
        )

        ValidationSwitch(
            label = "Dead End",
            checked = props.isDeadEnd ?: false,
            onCheckedChange = { onPropsChanged(props.copy(isDeadEnd = it)) }
        )

        ValidationSwitch(
            label = "Has Sidewalk",
            checked = props.hasSidewalk ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasSidewalk = it)) }
        )
    }
}

@Composable
private fun HouseEntranceValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationSwitch(
            label = "Has Entrance",
            checked = props.hasEntrance ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasEntrance = it)) }
        )

        if (props.hasEntrance == true) {
            ValidationSwitch(
                label = "Has Numbering Panel",
                checked = props.hasNumberingPanel ?: false,
                onCheckedChange = { onPropsChanged(props.copy(hasNumberingPanel = it)) }
            )

            if (props.hasNumberingPanel == true) {
                ValidationSwitch(
                    label = "Number Correct",
                    checked = props.numberingPanelCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(numberingPanelCorrect = it)) }
                )

                ValidationSwitch(
                    label = "Position Correct",
                    checked = props.numberingPanelPositionCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(numberingPanelPositionCorrect = it)) }
                )
            }
        }
    }
}

@Composable
private fun NamingPanelValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationSwitch(
            label = "Has Location",
            checked = props.hasNamingPanelLocation ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasNamingPanelLocation = it)) }
        )

        if (props.hasNamingPanelLocation == true) {
            ValidationSwitch(
                label = "Has Naming Panel",
                checked = props.hasNamingPanel ?: false,
                onCheckedChange = { onPropsChanged(props.copy(hasNamingPanel = it)) }
            )

            if (props.hasNamingPanel == true) {
                ValidationSwitch(
                    label = "Name Correct",
                    checked = props.namingCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(namingCorrect = it)) }
                )

                ValidationSwitch(
                    label = "Position Correct",
                    checked = props.namingPanelPositionCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(namingPanelPositionCorrect = it)) }
                )
            }
        }
    }
}

@Composable
private fun ValidationRadioGroup(
    label: String,
    options: List<String>,
    selectedValue: String?,
    onValueChanged: (String) -> Unit
) {
    Column {
        Text(text = label, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface)
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            options.forEach { option ->
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.clickable { onValueChanged(option) }
                ) {
                    RadioButton(
                        selected = selectedValue == option,
                        onClick = { onValueChanged(option) }
                    )
                    Text(text = option.replaceFirstChar { it.uppercase() }, fontSize = 12.sp)
                }
            }
        }
    }
}

@Composable
private fun ValidationNumberField(
    label: String,
    value: String,
    onValueChanged: (String) -> Unit
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChanged,
        label = { Text(label) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        modifier = Modifier.fillMaxWidth(),
        singleLine = true
    )
}

@Composable
private fun ValidationSwitch(
    label: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = label, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface)
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}

private fun Double.formatDecimal(digits: Int) = "%.${digits}f".format(this)