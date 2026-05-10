package com.nars.maplibre.utils

import android.util.Log

/**
 * Centralized logging utility for NARS application
 * 
 * Provides safe logging that:
 * - Prevents logging sensitive information (tokens, passwords, cookies)
 * - Can be disabled in production builds
 * - Provides consistent log formatting
 */
object NarsLogger {

    private const val DEFAULT_TAG = "NARS"

    /**
     * Enable/disable logging
     * In production, automatically disabled
     */
    var isEnabled: Boolean = try { com.nars.maplibre.BuildConfig.DEBUG } catch (_: Exception) { true }

    /**
     * Verbose logging - most detailed, disabled in production
     */
    fun v(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        if (isEnabled) {
            Log.v(tag, sanitizeMessage(message), throwable)
        }
    }

    /**
     * Debug logging - for development debugging
     */
    fun d(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        if (isEnabled) {
            Log.d(tag, sanitizeMessage(message), throwable)
        }
    }

    /**
     * Info logging - for general informational messages
     */
    fun i(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        if (isEnabled) {
            Log.i(tag, sanitizeMessage(message), throwable)
        }
    }

    /**
     * Warning logging - for potential issues
     */
    fun w(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        Log.w(tag, sanitizeMessage(message), throwable)
    }

    /**
     * Error logging - for errors and exceptions
     */
    fun e(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        Log.e(tag, sanitizeMessage(message), throwable)
    }

    /**
     * What a Terrible Failure - for critical errors
     */
    fun wtf(tag: String = DEFAULT_TAG, message: String, throwable: Throwable? = null) {
        Log.wtf(tag, sanitizeMessage(message), throwable)
    }

    /**
     * Sanitize log message to prevent sensitive data leakage
     * Removes or masks:
     * - Authorization tokens
     * - Cookies
     * - Passwords
     * - API keys
     */
    private fun sanitizeMessage(message: String): String {
        return message
            // Mask authorization tokens (Bearer tokens)
            .replace(Regex("Bearer\\s+[A-Za-z0-9\\-_]+"), "Bearer [REDACTED]")
            // Mask session cookies
            .replace(Regex("session[_-]?id[=:]\\s*[A-Za-z0-9\\-_]+"), "session_id=[REDACTED]")
            // Mask password fields
            .replace(Regex("password[=:]\\s*[^\\s,}]+"), "password=[REDACTED]")
            // Mask API keys
            .replace(Regex("api[_-]?key[=:]\\s*[A-Za-z0-9\\-_]+"), "api_key=[REDACTED]")
            // Mask access tokens
            .replace(Regex("access[_-]?token[=:]\\s*[A-Za-z0-9\\-_]+"), "access_token=[REDACTED]")
            // Mask refresh tokens
            .replace(Regex("refresh[_-]?token[=:]\\s*[A-Za-z0-9\\-_]+"), "refresh_token=[REDACTED]")
    }

    /**
     * Log network request without sensitive headers
     */
    fun logNetworkRequest(tag: String, method: String, url: String, body: String? = null) {
        if (isEnabled) {
            d(tag, "$method $url")
            body?.let {
                v(tag, "Request body: $it")
            }
        }
    }

    /**
     * Log network response without sensitive data
     */
    fun logNetworkResponse(tag: String, url: String, responseCode: Int, responseBody: String? = null) {
        if (isEnabled) {
            d(tag, "Response from $url: $responseCode")
            responseBody?.let {
                v(tag, "Response body: ${sanitizeMessage(it)}")
            }
        }
    }

    /**
     * Log authentication event (safe)
     */
    fun logAuthEvent(tag: String, event: String, username: String? = null) {
        val safeMessage = if (username != null) {
            "$event for user: ${maskUsername(username)}"
        } else {
            event
        }
        i(tag, safeMessage)
    }

    /**
     * Mask username for privacy
     */
    private fun maskUsername(username: String): String {
        return when {
            username.length <= 2 -> "**"
            username.length <= 4 -> "${username[0]}**${username.last()}"
            else -> "${username.take(2)}**${username.takeLast(2)}"
        }
    }
}
