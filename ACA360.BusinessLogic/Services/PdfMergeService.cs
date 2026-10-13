using ACA360.BusinessLogic.Interfaces;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class PdfMergeService : IPdfMergeService
    {
        public byte[] MergePdfs(List<byte[]> pdfFiles)
        {

            using var outputStream = new MemoryStream();
            var outputDocument = new PdfDocument();

            foreach (var pdf in pdfFiles)
            {
                using var stream = new MemoryStream(pdf);
                var inputDocument = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            outputDocument.Save(outputStream, false);
            return outputStream.ToArray();
        }
    }
}
