package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureSaveDto
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "entrances")
data class EntranceEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long = 0,        // 0 = not yet saved to server
    val label: String,
    val lat: Double,
    val lng: Double,
    val roadDbId: Long?,
    val entranceNumber: Int?,
    val entranceTypeKey: String = "main_entrance",

    // Phase 05 field checks
    val isNumbered: Boolean?      = null,   // does it have a plate?
    val isNumberCorrect: Boolean? = null,   // if numbered — is the number correct?
    val orderPlateNeeded: Boolean = false,
    val relocateNeeded: Boolean   = false,

    val syncStatus: SyncStatus = SyncStatus.PENDING,
) {
    fun toSaveDto(): FeatureSaveDto = FeatureSaveDto(
        type  = "house_entrance",
        layer = entranceTypeKey,
        label = label,
        data  = mapOf(
            "lat"              to lat,
            "lng"              to lng,
            "roadDbId"         to roadDbId,
            "entranceNumber"   to entranceNumber,
            "entranceTypeKey"  to entranceTypeKey,
            "isNumbered"       to isNumbered,
            "isNumberCorrect"  to isNumberCorrect,
            "orderPlateNeeded" to orderPlateNeeded,
            "relocateNeeded"   to relocateNeeded,
        ),
    )

    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = mapOf(
            "lat"              to lat,
            "lng"              to lng,
            "isNumbered"       to isNumbered,
            "isNumberCorrect"  to isNumberCorrect,
            "orderPlateNeeded" to orderPlateNeeded,
            "relocateNeeded"   to relocateNeeded,
        ),
    )
}
