using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class GeographicValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_CommuneIdNonPositive_ReturnsError(int communeId)
    {
        Assert.Equal("commune_id must be a positive integer.",
            GeographicValidator.Validate(UserRoles.CommuneUser, communeId, 1, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_DairaIdNonPositive_ReturnsError(int dairaId)
    {
        Assert.Equal("daira_id must be a positive integer.",
            GeographicValidator.Validate(UserRoles.DairaAdmin, 1, dairaId, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WilayaIdNonPositive_ReturnsError(int wilayaId)
    {
        Assert.Equal("wilaya_id must be a positive integer.",
            GeographicValidator.Validate(UserRoles.WilayaAdmin, 1, 1, wilayaId));
    }

    [Fact]
    public void Validate_CommuneUserWithoutCommune_ReturnsRequired()
    {
        Assert.Equal("commune_id is required for commune_user.",
            GeographicValidator.Validate(UserRoles.CommuneUser, null, 1, 1));
    }

    [Fact]
    public void Validate_CommuneUserWithCommune_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate(UserRoles.CommuneUser, 5, null, null));
    }

    [Fact]
    public void Validate_DairaAdminWithoutDaira_ReturnsRequired()
    {
        Assert.Equal("daira_id is required for daira_admin.",
            GeographicValidator.Validate(UserRoles.DairaAdmin, 1, null, 1));
    }

    [Fact]
    public void Validate_DairaAdminWithDaira_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate(UserRoles.DairaAdmin, 1, 5, null));
    }

    [Fact]
    public void Validate_WilayaAdminWithoutWilaya_ReturnsRequired()
    {
        Assert.Equal("wilaya_id is required for wilaya_admin.",
            GeographicValidator.Validate(UserRoles.WilayaAdmin, 1, 1, null));
    }

    [Fact]
    public void Validate_WilayaAdminWithWilaya_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate(UserRoles.WilayaAdmin, null, null, 5));
    }

    [Fact]
    public void Validate_FieldWorker_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate(UserRoles.FieldWorker, null, null, null));
    }

    [Fact]
    public void Validate_NationalAdmin_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate(UserRoles.NationalAdmin, null, null, null));
    }

    [Fact]
    public void Validate_UnknownRole_ReturnsNull()
    {
        Assert.Null(GeographicValidator.Validate("mystery_role", 1, 1, 1));
    }
}
