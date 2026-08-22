using System.ComponentModel.DataAnnotations;
using System.Reflection;
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
    ///
    /// If a DTO record adds a [Required] constructor parameter that has no
    /// matching public property (e.g. after a parameter rename), the lookup
    /// below THROWS instead of silently producing wrong results — keeping the
    /// helper in sync with the actual DTO constructors is an explicit
    /// requirement enforced at test time.
    /// </summary>
    private static List<ValidationResult> ValidateRecord<T>(T record)
    {
        var results = new List<ValidationResult>();
        // A record's primary (positional) constructor has the most parameters;
        // ordering by count keeps the choice deterministic if extra ctors
        // (e.g. copy constructors) ever appear.
        var ctor = typeof(T).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        foreach (var param in ctor.GetParameters())
        {
            var required = param.GetCustomAttribute<RequiredAttribute>();
            if (required is null)
            {
                continue;
            }

            var prop = typeof(T).GetProperty(param.Name!, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"DTO {typeof(T).Name} has a [Required] constructor parameter '{param.Name}' " +
                    "with no matching public property — keep constructor parameters and properties in sync.");

            var value = prop.GetValue(record);

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
            CurrentPassword: null,
            Password: null
        );

        var results = ValidateRecord(request);

        Assert.Empty(results);
    }
}
