namespace MisaConnect.EInvoice.Application.Abstractions;

/// <summary>
/// Application-layer view of the delete-options flag (research R-DEL-06).
/// Concrete implementation lives in Infrastructure and wraps
/// <c>IOptions&lt;MisaEInvoiceOptions&gt;.Value.Delete</c>; the Application
/// layer reads the flag through this port to avoid taking a project
/// dependency on Infrastructure's options type.
/// </summary>
public interface IDeleteOptionsAccessor
{
    bool IncludeRawErrorMessage { get; }
}
