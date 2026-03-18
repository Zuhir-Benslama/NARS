package com.nars.narstreet.core.db

import androidx.room.Database
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import com.nars.narstreet.data.dao.*
import com.nars.narstreet.data.model.*

@Database(
    entities = [
        RoadEntity::class,
        EntranceEntity::class,
        BuildingEntity::class,
        SpaceEntity::class,
        PanelEntity::class,
    ],
    version  = 1,
    exportSchema = false,
)
@TypeConverters(Converters::class)
abstract class AppDatabase : RoomDatabase() {
    abstract fun roadDao(): RoadDao
    abstract fun entranceDao(): EntranceDao
    abstract fun buildingDao(): BuildingDao
    abstract fun spaceDao(): SpaceDao
    abstract fun panelDao(): PanelDao
}
