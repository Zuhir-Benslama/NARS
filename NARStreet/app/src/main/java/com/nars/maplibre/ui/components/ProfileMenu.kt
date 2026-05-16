package com.nars.maplibre.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Logout
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.sp
import com.nars.maplibre.R
import com.nars.maplibre.data.model.User
import com.nars.maplibre.ui.theme.DangerColor
import com.nars.maplibre.ui.theme.DropdownBackground
import com.nars.maplibre.ui.theme.DropdownItem
import com.nars.maplibre.ui.theme.GlassBorder
import com.nars.maplibre.ui.theme.PrimaryGradientEnd
import com.nars.maplibre.ui.theme.PrimaryGradientStart
import com.nars.maplibre.ui.theme.TextPrimary
import com.nars.maplibre.ui.theme.TextSecondary

@Composable
fun ProfileMenu(
    user: User?,
    onSettingsClick: () -> Unit,
    onLogoutClick: () -> Unit,
    modifier: Modifier = Modifier,
    compact: Boolean = false
) {
    var dropdownExpanded by remember { mutableStateOf(false) }
    val initials = user?.getInitials() ?: "U"

    Box(modifier = modifier) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(20.dp))
                .background(
                    Brush.linearGradient(
                        colors = listOf(PrimaryGradientStart, PrimaryGradientEnd)
                    )
                )
                .clickable { dropdownExpanded = true },
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = initials,
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White
            )
        }

        DropdownMenu(
            expanded = dropdownExpanded,
            onDismissRequest = { dropdownExpanded = false },
            modifier = Modifier
                .background(
                    color = DropdownBackground,
                    shape = RoundedCornerShape(12.dp)
                )
        ) {
            if (compact) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(12.dp)
                ) {
                    Text(
                        text = user?.username ?: stringResource(R.string.profile_user),
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = TextPrimary
                    )
                    val userName = user?.name ?: ""
                    if (userName.isNotBlank()) {
                        Text(
                            text = userName,
                            fontSize = 12.sp,
                            color = TextSecondary
                        )
                    }
                }

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(1.dp)
                        .background(GlassBorder.copy(alpha = 0.3f))
                )
            }

            DropdownMenuItem(
                text = {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.Settings,
                            contentDescription = null,
                            tint = DropdownItem,
                            modifier = Modifier.size(18.dp)
                        )
                        Text(
                            text = stringResource(R.string.profile_settings),
                            fontSize = 13.sp,
                            color = DropdownItem
                        )
                    }
                },
                onClick = {
                    dropdownExpanded = false
                    onSettingsClick()
                }
            )

            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(1.dp)
                    .background(GlassBorder.copy(alpha = 0.3f))
            )

            DropdownMenuItem(
                text = {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.Logout,
                            contentDescription = stringResource(R.string.profile_logout),
                            tint = DangerColor,
                            modifier = Modifier.size(18.dp)
                        )
                        Text(
                            text = stringResource(R.string.profile_logout),
                            fontSize = 13.sp,
                            color = DangerColor
                        )
                    }
                },
                onClick = {
                    dropdownExpanded = false
                    onLogoutClick()
                }
            )
        }
    }
}
