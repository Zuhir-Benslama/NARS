package com.nars.narstreet.core.sync

import androidx.work.*
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Single place that enqueues the SyncWorker.
 * Replaces the copy-pasted enqueue() / enqueueSyncWork() in every repository.
 */
@Singleton
class SyncScheduler @Inject constructor(
    private val workManager: WorkManager,
) {
    fun schedule() = workManager.enqueueUniqueWork(
        SyncWorker.TAG,
        ExistingWorkPolicy.KEEP,
        OneTimeWorkRequestBuilder<SyncWorker>()
            .setConstraints(Constraints(NetworkType.CONNECTED))
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.SECONDS)
            .build(),
    )
}
