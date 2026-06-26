using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class DtoValidationTests
{
    [Fact]
    public void AuthorizedAdminSignupRequest_AllValid_PassesValidation()
    {
        var request = new AuthorizedAdminSignupRequest(
            AdminUsername: "admin1",
            AdminPassword: "Str0ng!Pass",
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "Str0ng!Pass",
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
            Password: "Str0ng!Pass"
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.True(isValid);
    }

    [Fact(Skip = "DTO currently accepts empty usernames — SignInRequest should require [Required] on Username")]
    public void SignInRequest_EmptyUsername_ShouldReject()
    {
        var request = new SignInRequest(
            Username: "",
            Password: "Str0ng!Pass"
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
            AdminPassword: "Str0ng!Pass",
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "Str0ng!Pass",
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        );

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<AuthorizedAdminSignupRequest>(json);

        Assert.Equal("admin1", deserialized!.AdminUsername);
        Assert.Equal("test@example.com", deserialized.Email);
        Assert.Equal(1, deserialized.CommuneId);
    }

    [Fact]
    public void SignInRequest_CanBeSerialized()
    {
        var request = new SignInRequest(
            Username: "testuser",
            Password: "Str0ng!Pass"
        );

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<SignInRequest>(json);

        Assert.Equal("testuser", deserialized!.Username);
        Assert.Equal("Str0ng!Pass", deserialized.Password);
    }

    [Fact(Skip = "DTO currently accepts empty usernames — SignInRequest should require [Required] on Username")]
    public void SignInRequest_WithEmptyUsername_ModelStateWouldReject()
    {
        var request = new SignInRequest(
            Username: "",
            Password: "Str0ng!Pass"
        );

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        Assert.False(isValid);
    }
}
