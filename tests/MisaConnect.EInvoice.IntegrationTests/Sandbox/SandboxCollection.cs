using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Sandbox tests share MISA's auth endpoint and rate limits. Running them
/// in a single non-parallel xUnit collection keeps the auth side
/// deterministic and avoids spurious INVALID_TAXCODE responses when
/// several test classes refresh tokens concurrently.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SandboxCollection
{
    public const string Name = "MISA Sandbox";
}
