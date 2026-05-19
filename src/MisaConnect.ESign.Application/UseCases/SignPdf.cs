using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class SignPdf
{
    private readonly EnsureAccessToken _ensureToken;
    private readonly ListActiveCertificates _listCerts;
    private readonly ICertificateSelector _certSelector;
    private readonly HashPdfDocument _hashPdf;
    private readonly SubmitSignHash _submitSignHash;
    private readonly PollSignStatus _pollStatus;
    private readonly AttachSignature _attachSignature;
    private readonly ISystemClock _clock;
    private readonly SignPdfRequestValidator _validator;
    private readonly Func<TimeSpan> _intervalAccessor;
    private readonly Func<TimeSpan> _totalTimeoutAccessor;

    public SignPdf(
        EnsureAccessToken ensureToken,
        ListActiveCertificates listCerts,
        ICertificateSelector certSelector,
        HashPdfDocument hashPdf,
        SubmitSignHash submitSignHash,
        PollSignStatus pollStatus,
        AttachSignature attachSignature,
        ISystemClock clock,
        SignPdfRequestValidator validator,
        Func<TimeSpan> intervalAccessor,
        Func<TimeSpan> totalTimeoutAccessor)
    {
        _ensureToken = ensureToken;
        _listCerts = listCerts;
        _certSelector = certSelector;
        _hashPdf = hashPdf;
        _submitSignHash = submitSignHash;
        _pollStatus = pollStatus;
        _attachSignature = attachSignature;
        _clock = clock;
        _validator = validator;
        _intervalAccessor = intervalAccessor;
        _totalTimeoutAccessor = totalTimeoutAccessor;
    }

    public async Task<SignPdfWorkResult> ExecuteAsync(SignPdfWorkRequest request, CancellationToken ct)
    {
        _validator.Validate(request);

        var token = await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);

        var activeCerts = await _listCerts.ExecuteAsync(token.Value, ct).ConfigureAwait(false);
        var cert = await _certSelector.SelectAsync(activeCerts, ct).ConfigureAwait(false);

        var hash = await _hashPdf.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            pdfBytes: request.Pdf.Bytes,
            documentId: request.DocumentId,
            signatureInfo: request.SignatureInfo,
            ct: ct).ConfigureAwait(false);

        var transaction = await _submitSignHash.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            userId: token.UserId,
            dataToBeDisplayed: request.DataToBeDisplayed,
            hash: hash,
            documentName: request.DocumentName,
            ct: ct).ConfigureAwait(false);

        var snapshot = await _pollStatus.ExecuteAsync(
            accessToken: token.Value,
            transactionId: transaction.TransactionId,
            interval: _intervalAccessor(),
            totalTimeout: _totalTimeoutAccessor(),
            ct: ct).ConfigureAwait(false);

        var signedBytes = await _attachSignature.ExecuteAsync(
            accessToken: token.Value,
            cert: cert,
            hash: hash,
            signatureData: snapshot.FirstSignatureData ?? string.Empty,
            ct: ct).ConfigureAwait(false);

        return new SignPdfWorkResult(
            SignedPdf: new SignedDocument(signedBytes),
            TransactionId: transaction.TransactionId,
            CertificateKeyAlias: cert.KeyAlias,
            CompletedAtUtc: _clock.UtcNow);
    }
}
