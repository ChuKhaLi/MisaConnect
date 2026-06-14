using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.UnitTests.TestSupport;

/// <summary>
/// Test double for <see cref="IMisaCredentialsAccessor"/>. Either returns a fixed
/// credential set, or delegates to a func so a test can vary the value per call
/// (e.g. to prove per-user isolation across two ambient scopes).
/// </summary>
internal sealed class StubMisaCredentialsAccessor : IMisaCredentialsAccessor
{
    private readonly Func<MisaCredentials> _get;

    public StubMisaCredentialsAccessor(
        string clientId = "cid",
        string clientKey = "ckey",
        string userName = "user",
        string password = "pwd")
    {
        var creds = new MisaCredentials(clientId, clientKey, userName, password);
        _get = () => creds;
    }

    public StubMisaCredentialsAccessor(MisaCredentials credentials) => _get = () => credentials;

    public StubMisaCredentialsAccessor(Func<MisaCredentials> get) => _get = get;

    public MisaCredentials Get() => _get();
}
