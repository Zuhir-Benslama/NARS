package com.nars.maplibre.ui.screens

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.test.platform.app.InstrumentationRegistry
import com.nars.maplibre.data.api.SessionManager
import io.mockk.coEvery
import io.mockk.coVerify
import io.mockk.mockk
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.koin.core.context.startKoin
import org.koin.core.context.stopKoin
import org.koin.dsl.module

class LoginScreenTest {

    @get:Rule
    val composeTestRule = createComposeRule()

    private val mockSessionManager = mockk<SessionManager>(relaxed = true)
    private lateinit var targetContext: Context

    @Before
    fun setup() {
        targetContext = InstrumentationRegistry.getInstrumentation().targetContext
        startKoin {
            modules(module {
                single<SessionManager> { mockSessionManager }
            })
        }
    }

    @After
    fun teardown() {
        stopKoin()
    }

    @Test
    fun displaysLoginForm() {
        composeTestRule.setContent {
            LoginScreen(onLoginSuccess = {})
        }

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_title)
        ).assertIsDisplayed()
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_subtitle)
        ).assertIsDisplayed()
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_username)
        ).assertIsDisplayed()
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_password)
        ).assertIsDisplayed()
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_sign_in)
        ).assertIsDisplayed()
    }

    @Test
    fun signInButtonDisabledWhenFieldsEmpty() {
        composeTestRule.setContent {
            LoginScreen(onLoginSuccess = {})
        }

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_sign_in)
        ).assertIsNotEnabled()
    }

    @Test
    fun signInButtonEnabledWhenFieldsFilled() {
        composeTestRule.setContent {
            LoginScreen(onLoginSuccess = {})
        }

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_username)
        ).performTextInput("testuser")
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_password)
        ).performTextInput("testpass")

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_sign_in)
        ).assertIsEnabled()
    }

    @Test
    fun successfulLoginCallsCallback() {
        coEvery { mockSessionManager.login(any(), any()) } returns Result.success(mockk(relaxed = true))

        var loginSucceeded = false
        composeTestRule.setContent {
            LoginScreen(onLoginSuccess = { loginSucceeded = true })
        }

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_username)
        ).performTextInput("testuser")
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_password)
        ).performTextInput("testpass")
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_sign_in)
        ).performClick()

        composeTestRule.waitForIdle()
        assert(loginSucceeded) { "Login should have succeeded" }
        coVerify { mockSessionManager.login("testuser", "testpass") }
    }

    @Test
    fun failedLoginShowsError() {
        coEvery { mockSessionManager.login(any(), any()) } returns Result.failure(Exception("Invalid credentials"))

        composeTestRule.setContent {
            LoginScreen(onLoginSuccess = {})
        }

        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_username)
        ).performTextInput("baduser")
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_password)
        ).performTextInput("badpass")
        composeTestRule.onNodeWithText(
            targetContext.getString(com.nars.maplibre.R.string.login_sign_in)
        ).performClick()

        composeTestRule.waitForIdle()
        composeTestRule.onNodeWithText("Invalid credentials", substring = true).assertIsDisplayed()
    }
}
