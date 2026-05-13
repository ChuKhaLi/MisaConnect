namespace MisaConnect.EInvoice.Domain.Pdf;

public readonly record struct PdfDocument(byte[] Content, string ContentType = "application/pdf");
