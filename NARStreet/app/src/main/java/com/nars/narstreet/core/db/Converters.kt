package com.nars.narstreet.core.db

import androidx.room.TypeConverter
import com.nars.narstreet.data.model.SyncStatus

class Converters {
    @TypeConverter fun fromSyncStatus(value: SyncStatus): String = value.name
    @TypeConverter fun toSyncStatus(value: String): SyncStatus   = SyncStatus.valueOf(value)
}
