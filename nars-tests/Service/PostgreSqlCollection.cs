using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Collection definition for PostgreSQL integration tests.
/// All test classes using this collection share a single PostgreSQL container.
/// </summary>
[CollectionDefinition(CollectionName)]
public class PostgreSqlCollection : ICollectionFixture<NarsDatabaseFixture>
{
    public const string CollectionName = "PostgreSQL Integration";
}
