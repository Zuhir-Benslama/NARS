package com.nars.maplibre.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.nars.maplibre.NarsViewModel
import com.nars.maplibre.ui.screens.LoginScreen
import com.nars.maplibre.ui.screens.MapScreen
import com.nars.maplibre.ui.screens.SettingsScreen

/**
 * Navigation routes
 */
object Routes {
    const val LOGIN = "login"
    const val MAP = "map"
    const val SETTINGS = "settings"
}

/**
 * Navigation host for NARS
 * Handles authentication flow
 */
@Composable
fun NarsNavHost(viewModel: NarsViewModel) {
    val navController = rememberNavController()

    NavHost(
        navController = navController,
        startDestination = Routes.LOGIN
    ) {
        composable(Routes.LOGIN) {
            LoginScreen(
                onLoginSuccess = {
                    navController.navigate(Routes.MAP) {
                        popUpTo(Routes.LOGIN) { inclusive = true }
                    }
                }
            )
        }

        composable(Routes.MAP) {
            MapScreen(
                viewModel = viewModel,
                onNavigateToSettings = {
                    navController.navigate(Routes.SETTINGS)
                },
                onLogout = {
                    navController.navigate(Routes.LOGIN) {
                        popUpTo(0) { inclusive = true }
                    }
                }
            )
        }

        composable(Routes.SETTINGS) {
            SettingsScreen(
                viewModel = viewModel,
                onNavigateBack = {
                    navController.popBackStack()
                },
                onLogout = {
                    navController.popBackStack()
                    navController.navigate(Routes.LOGIN) {
                        popUpTo(0) { inclusive = true }
                    }
                }
            )
        }
    }
}
