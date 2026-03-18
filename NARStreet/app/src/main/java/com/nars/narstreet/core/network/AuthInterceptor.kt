package com.nars.narstreet.core.network

import com.nars.narstreet.core.session.SessionManager
import kotlinx.coroutines.runBlocking
import okhttp3.Interceptor
import okhttp3.Response
import javax.inject.Inject

class AuthInterceptor @Inject constructor(
    private val session: SessionManager,
) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        val token = runBlocking { session.currentToken() }
        val request = if (token != null) {
            chain.request().newBuilder()
                .header("Cookie", "access_token=$token")
                .build()
        } else {
            chain.request()
        }
        return chain.proceed(request)
    }
}
