using Xunit;

namespace NarsApi.Tests.Integration;

/// <summary>
/// Collection definition for PostgreSQL integration tests.
/// All test classes using this collection share a single PostgreSQL container.
/// </summary>
[CollectionDefinition("PostgreSQL Integration")]
public class PostgreSqlCollection : ICollectionFixture<NarsDatabaseFixture>
{
    // This class has no code — it just defines the collection
}
