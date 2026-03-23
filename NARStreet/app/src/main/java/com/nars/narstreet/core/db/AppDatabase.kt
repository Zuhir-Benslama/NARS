package com.nars.narstreet.core.db

import androidx.room.Database
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import com.nars.narstreet.data.dao.*
import com.nars.narstreet.data.model.*

// Bump version on every schema change and add a Migration in DatabaseModule.
// During development, fallbackToDestructiveMigration() handles upgrades automatically
// (all data re-syncs from the server). Before shipping to real users, replace it
// with explicit Migration objects so field data is not wiped on app update.
@Database(
    entities = [
        AreaEntity::class,
        DistrictEntity::class,
        CityCenterEntity::class,
        RoadEntity::class,
        EntranceEntity::class,
        BuildingEntity::class,
        SpaceEntity::class,
        PanelEntity::class,
    ],
    version      = 102,  // v102: force clean rebuild — wipe stale coordinatesJson data
    exportSchema = true,  // generates schemas/ JSON files — commit these to VCS
)
@TypeConverters(Converters::class)
abstract class AppDatabase : RoomDatabase() {
    abstract fun areaDao():       AreaDao
    abstract fun districtDao():   DistrictDao
    abstract fun cityCenterDao(): CityCenterDao
    abstract fun roadDao():       RoadDao
    abstract fun entranceDao():   EntranceDao
    abstract fun buildingDao():   BuildingDao
    abstract fun spaceDao():      SpaceDao
    abstract fun panelDao():      PanelDao
}
