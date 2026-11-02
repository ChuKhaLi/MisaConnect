namespace MisaConnect.ESign.Domain.Certificates;

public sealed record CertificateChain(string Signing, string Intermediate, string Root)
{
    public IReadOnlyList<string> AsList() => new[] { Signing, Intermediate, Root };
}
