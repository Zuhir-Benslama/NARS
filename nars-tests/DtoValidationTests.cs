using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using Xunit;

using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public class DtoValidationTests
{
    /// <summary>
    /// Validates a record instance by checking [Required] attributes on
    /// constructor parameters — the same way .NET 10 MVC model binding
    /// validates record DTOs.  Validator.TryValidateObject only inspects
    /// properties, which misses parameter-level attributes on records.
    /// </summary>
    private static List<ValidationResult> ValidateRecord<T>(T record)
    {
        var results = new List<ValidationResult>();
        var ctor = typeof(T).GetConstructors().First();
        foreach (var param in ctor.GetParameters())
        {
            var required = param.GetCustomAttribute<RequiredAttribute>();
            if (required is null) continue;

            var value = record!
                .GetType()
                .GetProperty(param.Name!, BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(record);

            if (value is null && param.ParameterType.IsValueType)
            {
                results.Add(new ValidationResult($"{param.Name} is required.", [param.Name!]));
            }
            else if (!required.AllowEmptyStrings && value is string s && string.IsNullOrEmpty(s))
            {
                results.Add(new ValidationResult($"{param.Name} is required.", [param.Name!]));
            }
        }
        return results;
    }

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

        var results = ValidateRecord(request);
        Assert.Empty(results);
    }

    [Fact]
    public void SignInRequest_ValidData_PassesValidation()
    {
        var request = new SignInRequest(
            Username: "testuser",
            Password: DefaultPassword
        );

        var results = ValidateRecord(request);
        Assert.Empty(results);
    }

    [Fact]
    public void SignInRequest_EmptyUsername_ReturnsValidationError()
    {
        var request = new SignInRequest(
            Username: "",
            Password: DefaultPassword
        );

        var results = ValidateRecord(request);
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Username"));
    }

    [Fact]
    public void UpdateUserRequest_AllNulls_Valid()
    {
        var request = new UpdateUserRequest(
            Username: null,
            Email: null,
            Password: null
        );

        var results = ValidateRecord(request);

        Assert.Empty(results);
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
