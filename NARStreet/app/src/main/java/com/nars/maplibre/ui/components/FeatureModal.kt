package com.nars.maplibre.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Save
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.PUBLIC_BUILDING_SECTORS
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Feature modal matching the web version (FeatureModal.vue).
 * Collects: Name, Decision No., Decision Date, and phase-specific fields.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FeatureModal(
    feature: NarsFeature?,
    phase: PhaseDefinition,
    onSave: (NarsFeature) -> Unit,
    onDismiss: () -> Unit
) {
    val isEdit = feature?.dbId != 0L

    var name by remember { mutableStateOf(feature?.properties?.name ?: "") }
    var decisionNumber by remember { mutableStateOf(feature?.properties?.decisionNumber ?: "") }
    var decisionDate by remember { mutableStateOf(feature?.properties?.decisionDate ?: "") }
    var areaTypeKey by remember { mutableStateOf(feature?.properties?.areaTypeKey ?: "central_urban") }
    var districtTypeKey by remember { mutableStateOf(feature?.properties?.districtTypeKey ?: "district") }
    var roadTypeKey by remember { mutableStateOf(feature?.properties?.roadTypeKey ?: "street") }
    var entranceTypeKey by remember { mutableStateOf(feature?.properties?.entranceTypeKey ?: "main_entrance") }
    var spaceTypeKey by remember { mutableStateOf(feature?.properties?.spaceTypeKey ?: "garden") }
    var sectorKey by remember { mutableStateOf(feature?.properties?.sectorKey ?: "banking_postal") }
    var buildingTypeKey by remember { mutableStateOf(feature?.properties?.buildingTypeKey ?: "bank") }
    var entranceNumberStr by remember { mutableStateOf(feature?.properties?.entranceNumber?.toString() ?: "") }
    var bisNumberStr by remember { mutableStateOf(feature?.properties?.bisNumber?.toString() ?: "") }

    var errorName by remember { mutableStateOf(false) }
    var errorDecisionNum by remember { mutableStateOf(false) }
    var errorDecisionDate by remember { mutableStateOf(false) }
    var showDatePicker by remember { mutableStateOf(false) }

    val dateFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")

    var expandedAreaType by remember { mutableStateOf(false) }
    var expandedDistrictType by remember { mutableStateOf(false) }
    var expandedRoadType by remember { mutableStateOf(false) }
    var expandedEntranceType by remember { mutableStateOf(false) }
    var expandedSpaceType by remember { mutableStateOf(false) }
    var expandedSector by remember { mutableStateOf(false) }
    var expandedBuildingType by remember { mutableStateOf(false) }

    val isMainUrban = phase.key == "areas" && areaTypeKey == "central_urban"
    val isZoneWithTypeName = phase.key == "districts" &&
        (districtTypeKey == "trad_activities_zone" || districtTypeKey == "industry_zone")
    val isCityCenter = phase.key == "cityCenter"
    val isHouseEntranceEdit = phase.key == "houseEntrances" && isEdit

    if (isMainUrban && name.isEmpty()) name = "Main Urban Area"
    if (isCityCenter && name.isEmpty()) name = "City Center"
    if (isZoneWithTypeName && name.isEmpty()) {
        name = if (districtTypeKey == "trad_activities_zone") "Traditional Activities Zone" else "Industry Zone"
    }

    Dialog(onDismissRequest = onDismiss) {
        Card(
            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)
        ) {
            Column(
                modifier = Modifier.fillMaxWidth().padding(20.dp).verticalScroll(rememberScrollState())
            ) {
                Text(
                    text = if (isEdit) "Edit ${phase.label.replace("s", "")} Info" else "Add ${phase.label.replace("s", "")} Details",
                    fontSize = 18.sp,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(modifier = Modifier.height(16.dp))

                if (!isHouseEntranceEdit) {
                    OutlinedTextField(
                        value = name, onValueChange = { name = it; errorName = false },
                        label = { Text("Name *") }, modifier = Modifier.fillMaxWidth(),
                        readOnly = isMainUrban || isZoneWithTypeName || isCityCenter,
                        enabled = !isMainUrban, isError = errorName
                    )
                    if (isMainUrban) Text("The main urban area takes the municipality name.", fontSize = 11.sp, color = Color.Gray)
                    if (isCityCenter) Text("The city center is always named \"City Center\".", fontSize = 11.sp, color = Color.Gray)
                    if (isZoneWithTypeName) Text("This zone uses its type name.", fontSize = 11.sp, color = Color.Gray)
                    Spacer(modifier = Modifier.height(8.dp))

                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedTextField(
                            value = decisionNumber, onValueChange = { decisionNumber = it; errorDecisionNum = false },
                            label = { Text("Decision No. *") }, modifier = Modifier.weight(1f),
                            isError = errorDecisionNum, placeholder = { Text("e.g. 2024/001") }
                        )
                        OutlinedTextField(
                            value = decisionDate,
                            onValueChange = {},
                            label = { Text("Decision Date *") },
                            modifier = Modifier.weight(1f).clickable { showDatePicker = true },
                            readOnly = true,
                            isError = errorDecisionDate,
                            placeholder = { Text("YYYY-MM-DD") },
                            trailingIcon = {
                                TextButton(onClick = { showDatePicker = true }) {
                                    Text("📅")
                                }
                            }
                        )
                    }
                    Spacer(modifier = Modifier.height(8.dp))
                }

                // Phase-specific fields
                if (phase.key == "areas") {
                    SimpleDropdown(
                        label = "Area Type *", value = areaTypeKey,
                        options = listOf("central_urban" to "Main Urban Area", "secondary_urban" to "Secondary Urban Area"),
                        expanded = expandedAreaType, onExpandedChange = { expandedAreaType = it },
                        onSelected = { areaTypeKey = it; expandedAreaType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }
                if (phase.key == "districts") {
                    SimpleDropdown(
                        label = "District Type *", value = districtTypeKey,
                        options = listOf("housing_estate" to "Housing Estate", "urban_pole" to "Urban Pole",
                            "district" to "District", "trad_activities_zone" to "Traditional Activities Zone",
                            "industry_zone" to "Industry Zone"),
                        expanded = expandedDistrictType, onExpandedChange = { expandedDistrictType = it },
                        onSelected = { districtTypeKey = it; expandedDistrictType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }
                if (phase.key == "roads") {
                    SimpleDropdown(
                        label = "Road Type *", value = roadTypeKey,
                        options = listOf("boulevard" to "Boulevard", "avenue" to "Avenue", "street" to "Street",
                            "drive" to "Drive", "lane" to "Lane", "cul_de_sac" to "Cul-de-sac", "way" to "Way"),
                        expanded = expandedRoadType, onExpandedChange = { expandedRoadType = it },
                        onSelected = { roadTypeKey = it; expandedRoadType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }
                if (phase.key == "houseEntrances") {
                    SimpleDropdown(
                        label = "Entrance Type *", value = entranceTypeKey,
                        options = listOf("main_entrance" to "Main Entrance", "secondary_entrance" to "Secondary Entrance"),
                        expanded = expandedEntranceType, onExpandedChange = { expandedEntranceType = it },
                        onSelected = { entranceTypeKey = it; expandedEntranceType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    if (entranceTypeKey == "main_entrance" && !isEdit) {
                        OutlinedTextField(
                            value = entranceNumberStr, onValueChange = { entranceNumberStr = it },
                            label = { Text("Entrance Number") }, modifier = Modifier.fillMaxWidth(),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                    }
                    if (entranceTypeKey == "secondary_entrance" && bisNumberStr.isNotEmpty()) {
                        OutlinedTextField(
                            value = "BIS${bisNumberStr.padStart(2, '0')}", onValueChange = {},
                            label = { Text("BIS Number") }, modifier = Modifier.fillMaxWidth(), readOnly = true
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                    }
                }
                if (phase.key == "publicBuildings") {
                    val sectorBuildings = PUBLIC_BUILDING_SECTORS.find { it.key == sectorKey }?.buildings ?: emptyList()
                    SimpleDropdown(
                        label = "Sector *", value = sectorKey,
                        options = PUBLIC_BUILDING_SECTORS.map { it.key to it.label },
                        expanded = expandedSector, onExpandedChange = { expandedSector = it },
                        onSelected = {
                            sectorKey = it
                            buildingTypeKey = PUBLIC_BUILDING_SECTORS.find { s -> s.key == it }?.buildings?.firstOrNull()?.key ?: ""
                            expandedSector = false
                        }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    SimpleDropdown(
                        label = "Building Type *", value = buildingTypeKey,
                        options = sectorBuildings.map { it.key to it.label },
                        expanded = expandedBuildingType, onExpandedChange = { expandedBuildingType = it },
                        onSelected = { buildingTypeKey = it; expandedBuildingType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }
                if (phase.key == "publicSpaces") {
                    SimpleDropdown(
                        label = "Space Type *", value = spaceTypeKey,
                        options = listOf("garden" to "Garden", "square" to "Square"),
                        expanded = expandedSpaceType, onExpandedChange = { expandedSpaceType = it },
                        onSelected = { spaceTypeKey = it; expandedSpaceType = false }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }

                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(
                        onClick = {
                            var valid = true
                            if (!isHouseEntranceEdit) {
                                val labelRequired = !(phase.key == "districts" &&
                                    (districtTypeKey == "trad_activities_zone" || districtTypeKey == "industry_zone")) &&
                                    phase.key != "cityCenter" &&
                                    !(phase.key == "areas" && areaTypeKey == "central_urban")
                                if (labelRequired && name.trim().isEmpty()) { errorName = true; valid = false }
                                if (decisionNumber.trim().isEmpty()) { errorDecisionNum = true; valid = false }
                                if (decisionDate.trim().isEmpty()) { errorDecisionDate = true; valid = false }
                            }
                            if (!valid) return@Button

                            val props = feature?.properties?.copy(
                                name = name.trim(), decisionNumber = decisionNumber.trim(), decisionDate = decisionDate.trim(),
                                areaTypeKey = areaTypeKey.takeIf { phase.key == "areas" },
                                districtTypeKey = districtTypeKey.takeIf { phase.key == "districts" },
                                roadTypeKey = roadTypeKey.takeIf { phase.key == "roads" },
                                entranceTypeKey = entranceTypeKey.takeIf { phase.key == "houseEntrances" },
                                spaceTypeKey = spaceTypeKey.takeIf { phase.key == "publicSpaces" },
                                sectorKey = sectorKey.takeIf { phase.key == "publicBuildings" },
                                buildingTypeKey = buildingTypeKey.takeIf { phase.key == "publicBuildings" },
                                entranceNumber = entranceNumberStr.toIntOrNull().takeIf { phase.key == "houseEntrances" },
                                bisNumber = bisNumberStr.toIntOrNull().takeIf { phase.key == "houseEntrances" }
                            ) ?: com.nars.maplibre.data.model.FeatureProperties(
                                phase = phase.key, color = phase.color,
                                name = name.trim(), decisionNumber = decisionNumber.trim(), decisionDate = decisionDate.trim(),
                                areaTypeKey = areaTypeKey.takeIf { phase.key == "areas" },
                                districtTypeKey = districtTypeKey.takeIf { phase.key == "districts" },
                                roadTypeKey = roadTypeKey.takeIf { phase.key == "roads" },
                                entranceTypeKey = entranceTypeKey.takeIf { phase.key == "houseEntrances" },
                                spaceTypeKey = spaceTypeKey.takeIf { phase.key == "publicSpaces" },
                                sectorKey = sectorKey.takeIf { phase.key == "publicBuildings" },
                                buildingTypeKey = buildingTypeKey.takeIf { phase.key == "publicBuildings" },
                                entranceNumber = entranceNumberStr.toIntOrNull().takeIf { phase.key == "houseEntrances" },
                                bisNumber = bisNumberStr.toIntOrNull().takeIf { phase.key == "houseEntrances" }
                            )

                            val updatedFeature = feature?.copy(properties = props)
                                ?: NarsFeature(id = "feat_${System.currentTimeMillis()}", dbId = 0L,
                                    type = NarsFeatureType.URBAN_AREA,
                                    geometry = com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(0.0, 0.0)),
                                    properties = props)
                            onSave(updatedFeature)
                        },
                        modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary)
                    ) {
                        Icon(Icons.Default.Save, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(modifier = Modifier.width(4.dp))
                        Text(if (isEdit) "Update" else "Save")
                    }
                    Button(
                        onClick = onDismiss, modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                    ) { Text("Cancel") }
                }
            }
        }
    }

    // Date picker dialog
    if (showDatePicker) {
        val datePickerState = rememberDatePickerState()
        DatePickerDialog(
            onDismissRequest = { showDatePicker = false },
            confirmButton = {
                TextButton(onClick = {
                    datePickerState.selectedDateMillis?.let { millis ->
                        val date = Instant.ofEpochMilli(millis).atZone(ZoneId.systemDefault()).toLocalDate()
                        decisionDate = date.format(dateFormatter)
                        errorDecisionDate = false
                    }
                    showDatePicker = false
                }) { Text("OK") }
            },
            dismissButton = {
                TextButton(onClick = { showDatePicker = false }) { Text("Cancel") }
            }
        ) {
            DatePicker(state = datePickerState)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SimpleDropdown(
    label: String,
    value: String,
    options: List<Pair<String, String>>,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    onSelected: (String) -> Unit
) {
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = onExpandedChange) {
        OutlinedTextField(
            value = options.find { it.first == value }?.second ?: value,
            onValueChange = {}, label = { Text(label) },
            modifier = Modifier.fillMaxWidth().menuAnchor(),
            readOnly = true, trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) }
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { onExpandedChange(false) }) {
            options.forEach { (key, lbl) ->
                DropdownMenuItem(text = { Text(lbl) }, onClick = { onSelected(key) })
            }
        }
    }
}
