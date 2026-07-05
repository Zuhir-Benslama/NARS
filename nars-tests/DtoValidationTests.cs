using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using Xunit;

using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public class DtoValidationTests
{
    [Fact]
    public void AuthorizedAdminSignupRequest_AllValid_PassesValidation()
    {
        var request = new AuthorizedAdminSignupRequest(
            AdminUsername: "admin1",
            AdminPassword: DefaultPassword,
            Name: "Test User",
            Email: DefaultEmail,
            Phone: AltPhone,
            Username: "testuser",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.True(isValid);
    }

    [Fact]
    public void SignInRequest_ValidData_PassesValidation()
    {
        var request = new SignInRequest(
            Username: "testuser",
            Password: DefaultPassword
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.True(isValid);
    }

    [Fact]
    public void SignInRequest_EmptyUsername_ReturnsValidationError()
    {
        var request = new SignInRequest(
            Username: "",
            Password: DefaultPassword
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.False(isValid);
    }

    [Fact]
    public void UpdateUserRequest_AllNulls_Valid()
    {
        var request = new UpdateUserRequest(
            Username: null,
            Email: null,
            Password: null
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.True(isValid);
    }

    [Fact]
    public void AuthorizedAdminSignupRequest_CanBeSerialized()
    {
        var request = new AuthorizedAdminSignupRequest(
            AdminUsername: "admin1",
            AdminPassword: DefaultPassword,
            Name: "Test User",
            Email: DefaultEmail,
            Phone: AltPhone,
            Username: "testuser",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        );

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<AuthorizedAdminSignupRequest>(json);

        Assert.Equal("admin1", deserialized!.AdminUsername);
        Assert.Equal(DefaultEmail, deserialized.Email);
        Assert.Equal(1, deserialized.CommuneId);
    }

    [Fact]
    public void SignInRequest_CanBeSerialized()
    {
        var request = new SignInRequest(
            Username: "testuser",
            Password: DefaultPassword
        );

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<SignInRequest>(json);

        Assert.Equal("testuser", deserialized!.Username);
        Assert.Equal(DefaultPassword, deserialized.Password);
    }
}
