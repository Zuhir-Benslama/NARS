using Xunit;
using NarsApi.Infrastructure;

namespace NarsApi.Tests;

public class UserRolesTests
{
    [Fact]
    public void CommuneUser_Is_Not_Admin()
    {
        Assert.False(UserRoles.IsAdmin(UserRoles.CommuneUser));
    }

    [Theory]
    [InlineData(UserRoles.DairaAdmin)]
    [InlineData(UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.NationalAdmin)]
    public void Admin_Roles_Are_Admin(string role)
    {
        Assert.True(UserRoles.IsAdmin(role));
    }

    [Fact]
    public void Null_Is_Not_Admin()
    {
        Assert.False(UserRoles.IsAdmin(null));
    }

    [Fact]
    public void Unknown_Role_Is_Not_Admin()
    {
        Assert.False(UserRoles.IsAdmin("unknown_role"));
    }

    [Fact]
    public void AllAdminRoles_Contains_Three_Roles()
    {
        Assert.Equal(3, UserRoles.AllAdminRoles.Length);
        Assert.Contains(UserRoles.DairaAdmin, UserRoles.AllAdminRoles);
        Assert.Contains(UserRoles.WilayaAdmin, UserRoles.AllAdminRoles);
        Assert.Contains(UserRoles.NationalAdmin, UserRoles.AllAdminRoles);
    }

    [Fact]
    public void AllAdminRoles_Does_Not_Contain_CommuneUser()
    {
        Assert.DoesNotContain(UserRoles.CommuneUser, UserRoles.AllAdminRoles);
    }

    [Fact]
    public void Role_Constants_Have_Expected_Values()
    {
        Assert.Equal("commune_user", UserRoles.CommuneUser);
        Assert.Equal("daira_admin", UserRoles.DairaAdmin);
        Assert.Equal("wilaya_admin", UserRoles.WilayaAdmin);
        Assert.Equal("national_admin", UserRoles.NationalAdmin);
    }
}
