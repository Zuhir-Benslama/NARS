using System.Text.Json;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using Xunit;

using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public class DtoSerializationTests
{
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
