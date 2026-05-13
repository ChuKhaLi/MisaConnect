using MisaConnect.EInvoice.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace MisaConnect.EInvoice.Infrastructure.Configuration;

/// <summary>
/// Infrastructure-side implementation of
/// <see cref="IDeleteOptionsAccessor"/> reading the flag from
/// <see cref="IOptions{TOptions}"/> of <see cref="MisaEInvoiceOptions"/>.
/// </summary>
internal sealed class DeleteOptionsAccessor : IDeleteOptionsAccessor
{
    private readonly IOptions<MisaEInvoiceOptions> _options;

    public DeleteOptionsAccessor(IOptions<MisaEInvoiceOptions> options) => _options = options;

    public bool IncludeRawErrorMessage => _options.Value.Delete.IncludeRawErrorMessage;
}
