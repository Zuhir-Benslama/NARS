using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Serializes env-var dependent tests. The NARS_* variables are only read by
/// Program.cs top-level statements (which run exclusively inside these
/// WebApplicationFactory hosts), so mutating them here cannot race with other
/// test collections — but keeping them in one collection keeps them serial.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ProgramStartupCollection
{
    public const string Name = "Program startup env isolation";
}
