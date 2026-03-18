package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "roads")
data class RoadEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val dataJson: String,          // full feature data blob from NARS API

    // Phase 04 characteristics — null = not yet filled by field worker
    val lanes: Int?           = null,
    val trafficCapacity: String?  = null,   // HIGH / MEDIUM / LOW
    val tradActivity: String?     = null,   // HIGH / MEDIUM / LOW
    val hasMedianStrip: Boolean?  = null,
    val hasGreenery: Boolean?     = null,
    val isDeadEnd: Boolean?       = null,

    val syncStatus: SyncStatus = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = buildDataWithCharacteristics(),
    )

    private fun buildDataWithCharacteristics(): Map<String, Any?> = mapOf(
        "lanes"           to lanes,
        "trafficCapacity" to trafficCapacity,
        "tradActivity"    to tradActivity,
        "hasMedianStrip"  to hasMedianStrip,
        "hasGreenery"     to hasGreenery,
        "isDeadEnd"       to isDeadEnd,
    )
}
