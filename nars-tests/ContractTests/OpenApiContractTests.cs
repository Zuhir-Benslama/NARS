using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace NarsApi.Tests.ContractTests;

public class OpenApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SecretKey", AuthTestHelper.TestJwtSecret);
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=nars_contract_test;Username=test;Password=test");

            builder.UseSetting("HostOptions:Validate", "false");

            builder.UseEnvironment("Testing");
        });
    }

    [Fact]
    public async Task OpenApiSpec_ServesJson_WithExpectedStructure()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(body);

        Assert.NotNull(doc);
        Assert.Equal("3.1.1", doc["openapi"]?.GetValue<string>());
        Assert.NotNull(doc["info"]);
        Assert.Equal("NARS - National Addressing Reference System",
            doc["info"]!["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task OpenApiSpec_ContainsAllControllers()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(body);
        var paths = doc!["paths"]!.AsObject();

        Assert.Contains("/api/signin", paths.Select(p => p.Key));
        Assert.Contains("/api/signup", paths.Select(p => p.Key));
        Assert.Contains("/api/field/inspect", paths.Select(p => p.Key));
        Assert.Contains("/api/field/features", paths.Select(p => p.Key));
        Assert.Contains("/api/admin/users", paths.Select(p => p.Key));
        Assert.Contains("/api/field/inspections/{featureId}", paths.Select(p => p.Key));
    }
}
