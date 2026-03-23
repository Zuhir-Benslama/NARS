package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.CityCenterEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface CityCenterDao {
    @Query("SELECT * FROM city_centers LIMIT 1")
    fun get(): Flow<CityCenterEntity?>

    @Query("SELECT * FROM city_centers WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<CityCenterEntity>

    @Query("SELECT * FROM city_centers WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): CityCenterEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(items: List<CityCenterEntity>)

    @Update
    suspend fun update(item: CityCenterEntity)

    @Query("UPDATE city_centers SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE city_centers SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("SELECT COUNT(*) FROM city_centers WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
