package com.nars.maplibre.ui.navigation

import androidx.compose.runtime.Composable
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.nars.maplibre.ui.screens.LoginScreen
import com.nars.maplibre.ui.screens.MapScreen
import com.nars.maplibre.ui.screens.SettingsScreen

object Routes {
    const val LOGIN = "login"
    const val MAP = "map"
    const val SETTINGS = "settings"
}

@Composable
fun NarsNavHost() {
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
