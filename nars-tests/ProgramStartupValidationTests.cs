using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Verifies the fail-fast startup validation in Program.cs: misconfiguration must
/// throw before the server starts (no database is ever contacted). Also verifies the
/// database-connectivity guard surfaces as a startup failure.
/// </summary>
[Collection(ProgramStartupCollection.Name)]
public class ProgramStartupValidationTests : IDisposable
{
    private static string FastFailConnStr(int port) =>
        $"Host=127.0.0.1;Port={port};Database=nars;Username=nars;Password=nars;Timeout=1";

    private static readonly string[] EnvKeys =
        ["NARS_DB_PASSWORD", "NARS_JWT_SECRET", "NARS_ADMIN_SIGNUP_TOKEN"];

    private readonly Dictionary<string, string?> _saved = EnvKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable);

    public void Dispose()
    {
        foreach (var (key, value) in _saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static void ClearEnv(string key) => Environment.SetEnvironmentVariable(key, null);

    private static void SetEnv(string key, string value) => Environment.SetEnvironmentVariable(key, value);

    private static InvalidOperationException ExpectStartupFailure(
        WebApplicationFactory<Program> factory, string messageFragment)
    {
        using (factory)
        {
            var ex = Record.Exception(() => factory.CreateClient());
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                if (current is InvalidOperationException ioe
                    && ioe.Message.Contains(messageFragment, StringComparison.Ordinal))
                {
                    return ioe;
                }
            }

            throw new Xunit.Sdk.XunitException(
                $"Expected InvalidOperationException containing '{messageFragment}', got "
                + $"{(ex?.GetType().FullName ?? "no exception")}: {ex?.Message}");
        }
    }

    [Fact]
    public void MissingDbPassword_Throws()
    {
        ClearEnv("NARS_DB_PASSWORD");

        // Inject the placeholder connection string explicitly so the test does
        // not depend on ambient appsettings/env providing it (hermetic like
        // the Jwt tests below).
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=nars;Username=nars;Password=${NARS_DB_PASSWORD}"));
        var ex = ExpectStartupFailure(factory, "Database password is not configured");
        Assert.Contains("NARS_DB_PASSWORD", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingJwtSecret_Throws()
    {
        SetEnv("NARS_DB_PASSWORD", "test");
        ClearEnv("NARS_JWT_SECRET");

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SecretKey", string.Empty));
        ExpectStartupFailure(factory, "Jwt:SecretKey is not configured");
    }

    [Fact]
    public void ShortJwtSecret_Throws()
    {
        SetEnv("NARS_DB_PASSWORD", "test");
        ClearEnv("NARS_JWT_SECRET");

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SecretKey", "too-short"));
        var ex = ExpectStartupFailure(factory, "Jwt:SecretKey must be at least 32 characters");
        Assert.Contains("32 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LowEntropyJwtSecret_Throws()
    {
        SetEnv("NARS_DB_PASSWORD", "test");
        ClearEnv("NARS_JWT_SECRET");

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SecretKey", new string('a', 64)));
        var ex = ExpectStartupFailure(factory, "Jwt:SecretKey does not have enough entropy");
        Assert.Contains("100 bits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSignupToken_Throws()
    {
        SetEnv("NARS_DB_PASSWORD", "test");
        ClearEnv("NARS_ADMIN_SIGNUP_TOKEN");

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SecretKey", AuthTestHelper.TestJwtSecret));
        var ex = ExpectStartupFailure(factory, "AdminSignup:SignupToken is not configured");
        Assert.Contains("NARS_ADMIN_SIGNUP_TOKEN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreachableDatabase_FailsStartup()
    {
        // Hermetic: bind a listener on an OS-assigned free port and never accept.
        // The TCP handshake completes, but no Postgres greeting is ever sent, so
        // the probe times out (ConnectionString Timeout=1) and the connectivity
        // guard surfaces a startup failure. No external dependency, and no reliance
        // on a particular port being closed or blackholed.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        SetEnv("NARS_DB_PASSWORD", "test");
        ClearEnv("NARS_JWT_SECRET");
        ClearEnv("NARS_ADMIN_SIGNUP_TOKEN");

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DefaultConnection", FastFailConnStr(port));
                b.UseSetting("Jwt:SecretKey", AuthTestHelper.TestJwtSecret);
                b.UseSetting("AdminSignup:SignupToken", "test-signup-token");
            });

        ExpectStartupFailure(factory, "Unable to connect to the database");
    }
}
