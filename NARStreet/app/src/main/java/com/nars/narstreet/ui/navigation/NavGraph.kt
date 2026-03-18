package com.nars.narstreet.ui.navigation

import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.*
import androidx.navigation.navArgument
import com.nars.narstreet.ui.login.LoginScreen
import com.nars.narstreet.ui.login.LoginViewModel
import com.nars.narstreet.ui.phase04.Phase04Screen
import com.nars.narstreet.ui.phase05.Phase05Screen
import com.nars.narstreet.ui.phase06.Phase06Screen
import com.nars.narstreet.ui.phase07.Phase07Screen
import com.nars.narstreet.ui.phase08.Phase08Screen

object Route {
    const val LOGIN   = "login"
    const val PHASE04 = "phase04"
    const val PHASE05 = "phase05"
    const val PHASE06 = "phase06"
    const val PHASE07 = "phase07"
    const val PHASE08 = "phase08"

    // Detail routes with IDs
    const val PHASE06_DETAIL = "phase06/{buildingId}"
    const val PHASE07_DETAIL = "phase07/{spaceId}"
    const val PHASE08_DETAIL = "phase08/{panelId}"

    fun phase06Detail(id: Long) = "phase06/$id"
    fun phase07Detail(id: Long) = "phase07/$id"
    fun phase08Detail(id: Long) = "phase08/$id"
}

@Composable
fun NARStreetNavGraph(
    navController: NavHostController,
    isLoggedIn: Boolean,
) {
    val startDestination = if (isLoggedIn) Route.PHASE04 else Route.LOGIN

    NavHost(navController = navController, startDestination = startDestination) {

        composable(Route.LOGIN) {
            val vm: LoginViewModel = hiltViewModel()
            LoginScreen(
                viewModel  = vm,
                onLoggedIn = {
                    navController.navigate(Route.PHASE04) {
                        popUpTo(Route.LOGIN) { inclusive = true }
                    }
                },
            )
        }

        composable(Route.PHASE04) {
            Phase04Screen(
                onLogout = {
                    navController.navigate(Route.LOGIN) {
                        popUpTo(0) { inclusive = true }
                    }
                },
                onNavigateTo = { navController.navigate(it) },
            )
        }

        composable(Route.PHASE05) {
            Phase05Screen(
                onBack   = { navController.popBackStack() },
                onLogout = {
                    navController.navigate(Route.LOGIN) { popUpTo(0) { inclusive = true } }
                },
            )
        }

        composable(
            route     = Route.PHASE06_DETAIL,
            arguments = listOf(navArgument("buildingId") { type = NavType.LongType }),
        ) { backStack ->
            val buildingId = backStack.arguments?.getLong("buildingId") ?: return@composable
            Phase06Screen(
                buildingId = buildingId,
                onBack     = { navController.popBackStack() },
            )
        }

        composable(
            route     = Route.PHASE07_DETAIL,
            arguments = listOf(navArgument("spaceId") { type = NavType.LongType }),
        ) { backStack ->
            val spaceId = backStack.arguments?.getLong("spaceId") ?: return@composable
            Phase07Screen(
                spaceId = spaceId,
                onBack  = { navController.popBackStack() },
            )
        }

        composable(
            route     = Route.PHASE08_DETAIL,
            arguments = listOf(navArgument("panelId") { type = NavType.LongType }),
        ) { backStack ->
            val panelId = backStack.arguments?.getLong("panelId") ?: return@composable
            Phase08Screen(
                panelId = panelId,
                onBack  = { navController.popBackStack() },
            )
        }
    }
}
