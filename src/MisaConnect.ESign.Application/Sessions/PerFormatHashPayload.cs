using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Application.Sessions;

public abstract record PerFormatHashPayload
{
    public sealed record Pdf(PdfHashOutput Output) : PerFormatHashPayload;
    public sealed record Xml(XmlHashOutput Output) : PerFormatHashPayload;
    public sealed record Word(WordExcelHashOutput Output) : PerFormatHashPayload;
    public sealed record Excel(WordExcelHashOutput Output) : PerFormatHashPayload;
}
