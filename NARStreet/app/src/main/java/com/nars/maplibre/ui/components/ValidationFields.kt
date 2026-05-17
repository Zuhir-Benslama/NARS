package com.nars.maplibre.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.R
import com.nars.maplibre.data.model.FeatureProperties

@Composable
fun RoadsValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationRadioGroup(
            label = stringResource(R.string.road_traffic),
            options = listOf("high", "medium", "low"),
            selectedValue = props.roadTraffic,
            onValueChanged = { onPropsChanged(props.copy(roadTraffic = it)) }
        )

        ValidationRadioGroup(
            label = stringResource(R.string.trad_activity),
            options = listOf("high", "medium", "low"),
            selectedValue = props.tradActivity,
            onValueChanged = { onPropsChanged(props.copy(tradActivity = it)) }
        )

        ValidationNumberField(
            label = stringResource(R.string.num_lanes),
            value = props.numLanes?.toString() ?: "",
            onValueChanged = { onPropsChanged(props.copy(numLanes = it.toIntOrNull())) }
        )

        ValidationSwitch(
            label = stringResource(R.string.has_median),
            checked = props.hasMedian ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasMedian = it)) }
        )

        ValidationSwitch(
            label = stringResource(R.string.has_vegetation),
            checked = props.hasVegetation ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasVegetation = it)) }
        )

        ValidationSwitch(
            label = stringResource(R.string.is_dead_end),
            checked = props.isDeadEnd ?: false,
            onCheckedChange = { onPropsChanged(props.copy(isDeadEnd = it)) }
        )

        ValidationSwitch(
            label = stringResource(R.string.has_sidewalk),
            checked = props.hasSidewalk ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasSidewalk = it)) }
        )
    }
}

@Composable
fun HouseEntranceValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationSwitch(
            label = stringResource(R.string.has_entrance),
            checked = props.hasEntrance ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasEntrance = it)) }
        )

        if (props.hasEntrance == true) {
            ValidationSwitch(
                label = stringResource(R.string.has_numbering_panel),
                checked = props.hasNumberingPanel ?: false,
                onCheckedChange = { onPropsChanged(props.copy(hasNumberingPanel = it)) }
            )

            if (props.hasNumberingPanel == true) {
                ValidationSwitch(
                    label = stringResource(R.string.numbering_panel_correct),
                    checked = props.numberingPanelCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(numberingPanelCorrect = it)) }
                )

                ValidationSwitch(
                    label = stringResource(R.string.numbering_panel_position_correct),
                    checked = props.numberingPanelPositionCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(numberingPanelPositionCorrect = it)) }
                )
            }
        }
    }
}

@Composable
fun NamingPanelValidationFields(
    props: FeatureProperties,
    onPropsChanged: (FeatureProperties) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        ValidationSwitch(
            label = stringResource(R.string.has_naming_panel_location),
            checked = props.hasNamingPanelLocation ?: false,
            onCheckedChange = { onPropsChanged(props.copy(hasNamingPanelLocation = it)) }
        )

        if (props.hasNamingPanelLocation == true) {
            ValidationSwitch(
                label = stringResource(R.string.has_naming_panel),
                checked = props.hasNamingPanel ?: false,
                onCheckedChange = { onPropsChanged(props.copy(hasNamingPanel = it)) }
            )

            if (props.hasNamingPanel == true) {
                ValidationSwitch(
                    label = stringResource(R.string.naming_correct),
                    checked = props.namingCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(namingCorrect = it)) }
                )

                ValidationSwitch(
                    label = stringResource(R.string.naming_panel_position_correct),
                    checked = props.namingPanelPositionCorrect ?: false,
                    onCheckedChange = { onPropsChanged(props.copy(namingPanelPositionCorrect = it)) }
                )
            }
        }
    }
}

@Composable
fun ValidationRadioGroup(
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
fun ValidationNumberField(
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
fun ValidationSwitch(
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

fun Double.formatDecimal(digits: Int) = "%.${digits}f".format(this)
