using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Application.Sessions;

public sealed record SigningSession
{
    public SigningSession(
        string ClientId,
        string TransactionId,
        DocumentFormat Format,
        PerFormatHashPayload HashPayload,
        IReadOnlyList<string> RecordedDocumentIds,
        DateTimeOffset CreatedAtUtc,
        TimeSpan Ttl,
        IReadOnlySet<string> ObservedMessageIds,
        SigningSessionCachedSuccess? CachedSuccess)
    {
        if (string.IsNullOrWhiteSpace(ClientId)) throw new ArgumentException("ClientId must be non-empty.", nameof(ClientId));
        if (string.IsNullOrWhiteSpace(TransactionId)) throw new ArgumentException("TransactionId must be non-empty.", nameof(TransactionId));
        if (Format == DocumentFormat.Unknown) throw new ArgumentException("Format must not be Unknown.", nameof(Format));
        if (!FormatMatchesPayload(Format, HashPayload))
        {
            throw new ArgumentException($"HashPayload variant does not match Format '{Format}'.", nameof(HashPayload));
        }
        if (RecordedDocumentIds is null || RecordedDocumentIds.Count == 0)
        {
            throw new ArgumentException("RecordedDocumentIds must contain at least one document.", nameof(RecordedDocumentIds));
        }
        if (Ttl <= TimeSpan.Zero) throw new ArgumentException("Ttl must be > zero.", nameof(Ttl));

        this.ClientId = ClientId;
        this.TransactionId = TransactionId;
        this.Format = Format;
        this.HashPayload = HashPayload;
        this.RecordedDocumentIds = RecordedDocumentIds;
        this.CreatedAtUtc = CreatedAtUtc;
        this.Ttl = Ttl;
        this.ObservedMessageIds = ObservedMessageIds ?? new HashSet<string>();
        this.CachedSuccess = CachedSuccess;
    }

    public string ClientId { get; init; }
    public string TransactionId { get; init; }
    public DocumentFormat Format { get; init; }
    public PerFormatHashPayload HashPayload { get; init; }
    public IReadOnlyList<string> RecordedDocumentIds { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public TimeSpan Ttl { get; init; }
    public IReadOnlySet<string> ObservedMessageIds { get; init; }
    public SigningSessionCachedSuccess? CachedSuccess { get; init; }

    /// <summary>
    /// The MISA user name of the principal that began the transaction. Webhook
    /// finalize resolves this principal's credentials in <c>CredentialsMode.Dynamic</c>;
    /// <c>null</c> for sessions registered without a user name.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// The terminal (FAILED/CANCELLED) state MISA confirmed for this transaction, recorded
    /// once; later deliveries are answered from it. <c>null</c> until recorded.
    /// </summary>
    public SigningSessionCachedTerminal? CachedTerminal { get; init; }

    private static bool FormatMatchesPayload(DocumentFormat format, PerFormatHashPayload payload) => (format, payload) switch
    {
        (DocumentFormat.Pdf, PerFormatHashPayload.Pdf) => true,
        (DocumentFormat.Xml, PerFormatHashPayload.Xml) => true,
        (DocumentFormat.Word, PerFormatHashPayload.Word) => true,
        (DocumentFormat.Excel, PerFormatHashPayload.Excel) => true,
        _ => false,
    };
}
