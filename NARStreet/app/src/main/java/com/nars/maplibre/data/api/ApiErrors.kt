package com.nars.maplibre.data.api

import com.nars.maplibre.utils.Config
import kotlinx.coroutines.delay

/**
 * Error types matching the web version (errors.ts)
 * Sealed class hierarchy for proper error handling
 */
sealed class NarsError(
    override val message: String,
    val code: String,
    val context: ErrorContext,
    cause: Throwable? = null
) : Exception(message, cause) {

    data class ErrorContext(
        val url: String,
        val method: String,
        val status: Int? = null
    )
}

/** Network error - connection failed, offline, DNS failure */
class NetworkError(context: NarsError.ErrorContext, cause: Throwable? = null) : NarsError(
    message = "Network error. Please check your connection.",
    code = "NETWORK_ERROR",
    context = context,
    cause = cause
)

/** Timeout error - request took too long */
class TimeoutError(context: NarsError.ErrorContext) : NarsError(
    message = "Request timed out after ${Config.API_DEFAULT_TIMEOUT_MS}ms",
    code = "TIMEOUT_ERROR",
    context = context
)

/** Authentication error - 401/403 */
class AuthError(context: NarsError.ErrorContext) : NarsError(
    message = "Authentication failed. Please login again.",
    code = "AUTH_ERROR",
    context = context
)

/** Server error - 5xx responses */
class ServerError(
    context: NarsError.ErrorContext,
    message: String = "Server error occurred"
) : NarsError(
    message = message,
    code = "SERVER_ERROR",
    context = context
)

/** Not found error - 404 */
class NotFoundError(context: NarsError.ErrorContext) : NarsError(
    message = "Resource not found",
    code = "NOT_FOUND_ERROR",
    context = context
)

/** Conflict error - 409 */
class ConflictError(
    context: NarsError.ErrorContext,
    val details: String? = null
) : NarsError(
    message = "Conflict: ${details ?: "Resource conflict"}",
    code = "CONFLICT_ERROR",
    context = context
)

/** Validation error - 422 */
class ValidationError(
    context: NarsError.ErrorContext,
    val details: String? = null
) : NarsError(
    message = "Validation failed: ${details ?: "Invalid data"}",
    code = "VALIDATION_ERROR",
    context = context
)

/**
 * Retry utility matching web version (errors.ts: withRetry)
 * Implements exponential backoff with jitter
 */
suspend fun <T> withRetry(
    operation: suspend () -> T,
    config: RetryConfig = RetryConfig(),
    onRetry: (suspend (Throwable, Int) -> Unit)? = null
): T {
    var lastException: Throwable? = null
    
    repeat(config.maxRetries + 1) { attempt ->
        try {
            return operation()
        } catch (e: Exception) {
            lastException = e
            
            // Don't retry on the last attempt
            if (attempt >= config.maxRetries) {
                throw e
            }
            
            // Don't retry on certain errors
            if (e is AuthError || e is ValidationError || e is NotFoundError) {
                throw e
            }
            
            // Calculate delay with exponential backoff and jitter
            val delayMs = calculateBackoff(attempt + 1, config)
            
            // Log retry attempt
            onRetry?.invoke(e, attempt + 1)
            
            delay(delayMs)
        }
    }
    
    throw requireNotNull(lastException) { "withRetry: lastException should never be null after exhausting retries" }
}

/**
 * Retry configuration matching web version
 */
data class RetryConfig(
    val maxRetries: Int = Config.API_MAX_RETRIES,
    val baseDelayMs: Long = Config.API_RETRY_BASE_DELAY_MS.toLong(),
    val maxDelayMs: Long = Config.API_RETRY_MAX_DELAY_MS.toLong(),
    val jitterFactor: Double = 0.2
)

/**
 * Calculate backoff delay with exponential backoff and jitter
 */
private fun calculateBackoff(attempt: Int, config: RetryConfig): Long {
    // Exponential backoff: baseDelay * 2^(attempt-1)
    val exponentialDelay = config.baseDelayMs * (1L shl (attempt - 1))
    
    // Add jitter (±20% by default)
    val jitter = exponentialDelay * config.jitterFactor
    val jitteredDelay = exponentialDelay + (jitter * (Math.random() * 2 - 1)).toLong()
    
    // Cap at max delay
    return jitteredDelay.coerceIn(config.baseDelayMs, config.maxDelayMs)
}
