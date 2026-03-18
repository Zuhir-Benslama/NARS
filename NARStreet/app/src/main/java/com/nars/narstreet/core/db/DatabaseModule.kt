package com.nars.narstreet.core.db

import android.content.Context
import androidx.room.Room
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object DatabaseModule {

    @Provides @Singleton
    fun provideDatabase(@ApplicationContext context: Context): AppDatabase =
        Room.databaseBuilder(context, AppDatabase::class.java, "narstreet.db")
            .fallbackToDestructiveMigration()
            .build()

    @Provides fun provideRoadDao(db: AppDatabase)     = db.roadDao()
    @Provides fun provideEntranceDao(db: AppDatabase) = db.entranceDao()
    @Provides fun provideBuildingDao(db: AppDatabase) = db.buildingDao()
    @Provides fun provideSpaceDao(db: AppDatabase)    = db.spaceDao()
    @Provides fun providePanelDao(db: AppDatabase)    = db.panelDao()
}
