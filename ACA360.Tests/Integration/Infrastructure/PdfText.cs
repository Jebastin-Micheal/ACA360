using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>Reads what a generated PDF actually says (the forms are flattened, so the filled values are page text).</summary>
public static class PdfText
{
    public static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    public static string Extract(byte[] bytes)
    {
        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(bytes)));
        var text = new System.Text.StringBuilder();
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
            text.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));
        return text.ToString();
    }

    public static int Pages(byte[] bytes)
    {
        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(bytes)));
        return pdf.GetNumberOfPages();
    }
}
