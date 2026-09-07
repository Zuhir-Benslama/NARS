using Xunit;
using NarsApi.Infrastructure;

namespace NarsApi.Tests;

public class UserRolesTests
{
    [Fact]
    public void IsAdmin_CommuneUser_ReturnsFalse() =>
        Assert.False(UserRoles.IsAdmin(UserRoles.CommuneUser));

    [Theory]
    [InlineData(UserRoles.DairaAdmin)]
    [InlineData(UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.NationalAdmin)]
    public void IsAdmin_AdminRoles_ReturnsTrue(string role) =>
        Assert.True(UserRoles.IsAdmin(role));

    [Fact]
    public void IsAdmin_Null_ReturnsFalse() =>
        Assert.False(UserRoles.IsAdmin(null));

    [Fact]
    public void IsAdmin_UnknownRole_ReturnsFalse() =>
        Assert.False(UserRoles.IsAdmin("unknown_role"));

    [Fact]
    public void AllAdminRoles_HasExpectedCount()
    {
        Assert.Equal(3, UserRoles.AllAdminRoles.Length);
        Assert.Contains(UserRoles.DairaAdmin, UserRoles.AllAdminRoles);
        Assert.Contains(UserRoles.WilayaAdmin, UserRoles.AllAdminRoles);
        Assert.Contains(UserRoles.NationalAdmin, UserRoles.AllAdminRoles);
    }

    [Fact]
    public void AllAdminRoles_DoesNotContainCommuneUser() =>
        Assert.DoesNotContain(UserRoles.CommuneUser, UserRoles.AllAdminRoles);

    [Fact]
    public void IsAdmin_FieldWorker_ReturnsFalse() =>
        Assert.False(UserRoles.IsAdmin(UserRoles.FieldWorker));

    [Fact]
    public void IsCommuneScoped_FieldWorker_ReturnsTrue() =>
        Assert.True(UserRoles.IsCommuneScoped(UserRoles.FieldWorker));

    [Fact]
    public void IsCommuneScoped_CommuneUser_ReturnsTrue() =>
        Assert.True(UserRoles.IsCommuneScoped(UserRoles.CommuneUser));

    [Fact]
    public void IsCommuneScoped_DairaAdmin_ReturnsFalse() =>
        Assert.False(UserRoles.IsCommuneScoped(UserRoles.DairaAdmin));

    [Fact]
    public void RoleConstants_HaveExpectedValues()
    {
        Assert.Equal("commune_user", UserRoles.CommuneUser);
        Assert.Equal("field_worker", UserRoles.FieldWorker);
        Assert.Equal("daira_admin", UserRoles.DairaAdmin);
        Assert.Equal("wilaya_admin", UserRoles.WilayaAdmin);
        Assert.Equal("national_admin", UserRoles.NationalAdmin);
    }

    [Theory]
    [InlineData(UserRoles.FieldWorker)]
    [InlineData(UserRoles.CommuneUser)]
    [InlineData(UserRoles.DairaAdmin)]
    [InlineData(UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.NationalAdmin)]
    public void IsDraftReviewer_ReviewerRoles_ReturnsTrue(string role) =>
        Assert.True(UserRoles.IsDraftReviewer(role));

    [Fact]
    public void IsDraftReviewer_Null_ReturnsFalse() =>
        Assert.False(UserRoles.IsDraftReviewer(null));

    [Fact]
    public void IsDraftReviewer_UnknownRole_ReturnsFalse() =>
        Assert.False(UserRoles.IsDraftReviewer("unknown_role"));
}
