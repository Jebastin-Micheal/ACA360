using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>One error the IRS reported: on a 1095 (UniqueRecordId) or on the 1094 / the whole transmission.</summary>
    public sealed record IrsAckError(string? UniqueSubmissionId, string? UniqueRecordId, string? Code, string? Text, string? Xpath)
    {
        /// <summary>The Record ID of the 1095 the error is on (ReceiptId|SubmissionId|RecordId), or null.</summary>
        public int? RecordId => UniqueRecordId?.Split('|') is { Length: 3 } p && int.TryParse(p[2], out var id) ? id : null;
    }

    /// <summary>
    /// What an IRS acknowledgement says about a transmission. ErrorFileMissing: the
    /// acknowledgement says its Error Data File is attached but the file uploaded does not
    /// contain it (an MTOM reference only), so the errors have to come from the Error Data File.
    /// </summary>
    public sealed record IrsAcknowledgement(string? Status, string? ReceiptId, IReadOnlyList<IrsAckError> Errors, bool ErrorFileMissing = false)
    {
        /// <summary>
        /// The IrsSubmissionLog status for the IRS status: 1 transmitted / still processing,
        /// 2 accepted, 3 accepted with errors (or partially accepted), 4 rejected.
        /// </summary>
        public int StatusId => Normalised switch
        {
            "accepted" => 2,
            "accepted with errors" or "partially accepted" => 3,
            "rejected" => 4,
            _ => 1,
        };

        public bool NotFound => Normalised == "not found";

        private string Normalised => string.Join(' ', (Status ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        /// <summary>
        /// Reads an acknowledgement as the IRS returns it (IRS Publication 5165, section 6):
        /// <list type="bullet">
        /// <item>the status response (ACABulkRequestTransmitterStatusDetailResponse: TransmissionStatusCd, ReceiptId, ErrorMessageDetail);</item>
        /// <item>the Error Data File downloaded from the AIR UI (TransmitterErrorDetailGrp entries with
        /// SubmissionLevelStatusCd, UniqueSubmissionId or UniqueRecordId, and ErrorMessageDetail);</item>
        /// <item>the manifest response (ACATransmitterManifestRespDtl: SubmissionStatusCd, ReceiptId).</item>
        /// </list>
        /// Elements are matched by name, not namespace, because each tax year has its own namespace.
        /// </summary>
        public static IrsAcknowledgement Parse(Stream xml)
        {
            XDocument doc;
            try
            {
                using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                doc = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                throw new FormatException("This is not an XML file. Upload the acknowledgement or Error Data File from the IRS AIR system.", ex);
            }

            IEnumerable<XElement> All(string name) => doc.Descendants().Where(e => e.Name.LocalName == name);
            string? First(XElement? scope, string name) =>
                (scope ?? doc.Root!).Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;

            var errors = new List<IrsAckError>();

            // The status response can carry the Error Data File itself (BulkExchangeFile, base64;
            // compressed or not). Read it when it is there; say so when only a reference to an
            // MTOM attachment is.
            bool errorFileMissing = false;
            IrsAcknowledgement? attached = null;
            foreach (var file in All("BulkExchangeFile"))
            {
                var content = string.Concat(file.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
                if (content.Length == 0) { errorFileMissing |= file.HasElements; continue; }
                // An attachment that cannot be read leaves the status and receipt usable.
                try { attached = ParseAttachment(content); }
                catch (Exception ex) when (ex is FormatException or InvalidDataException) { errorFileMissing = true; }
            }
            foreach (var group in All("TransmitterErrorDetailGrp"))
            {
                var submission = First(group, "UniqueSubmissionId");
                var record = First(group, "UniqueRecordId");
                var details = group.Descendants().Where(e => e.Name.LocalName == "ErrorMessageDetail").ToList();
                if (details.Count == 0) errors.Add(new IrsAckError(submission, record, null, null, null));
                foreach (var d in details)
                    errors.Add(new IrsAckError(submission, record, First(d, "ErrorMessageCd"), First(d, "ErrorMessageTxt"), First(d, "XpathContent")));
            }
            // Errors on the transmission itself (status response, manifest response).
            foreach (var d in All("ErrorMessageDetail").Where(d => !d.Ancestors().Any(a => a.Name.LocalName == "TransmitterErrorDetailGrp")))
                errors.Add(new IrsAckError(null, null, First(d, "ErrorMessageCd"), First(d, "ErrorMessageTxt"), First(d, "XpathContent")));

            var status = First(null, "TransmissionStatusCd")
                         ?? First(null, "SubmissionStatusCd")
                         ?? WorstSubmissionLevelStatus(All("SubmissionLevelStatusCd").Select(e => e.Value.Trim()))
                         // An Error Data File comes only with errors; without a stated status they did not stop it.
                         ?? (errors.Any(e => e.RecordId != null) ? "Accepted with Errors" : null);

            // The receipt: stated in a status / manifest response, otherwise the first part of the
            // identifiers in an Error Data File (ReceiptId|SubmissionId[|RecordId]).
            var receipt = First(null, "ReceiptId")
                          ?? errors.Select(e => (e.UniqueRecordId ?? e.UniqueSubmissionId)?.Split('|')[0]).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

            if (attached != null)
            {
                errors.AddRange(attached.Errors);
                status ??= attached.Status;
                receipt ??= attached.ReceiptId;
            }

            if (status == null && errors.Count == 0 && receipt == null)
                throw new FormatException("This XML file is not an IRS AIR acknowledgement: it has no transmission status, receipt or errors.");

            return new IrsAcknowledgement(status, receipt, errors, errorFileMissing);
        }

        /// <summary>The Error Data File inside a status response: base64, as XML, gzip or zip.</summary>
        private static IrsAcknowledgement ParseAttachment(string base64)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException ex) { throw new FormatException("The acknowledgement's attached Error Data File (BulkExchangeFile) is not readable.", ex); }

            Stream content = new MemoryStream(bytes);
            if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
                content = new GZipStream(content, CompressionMode.Decompress);
            else if (bytes.Length > 2 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K')
            {
                var zip = new ZipArchive(content, ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) ?? zip.Entries.FirstOrDefault()
                    ?? throw new FormatException("The acknowledgement's attached Error Data File is an empty zip.");
                var unzipped = new MemoryStream();
                using (var entryStream = entry.Open()) entryStream.CopyTo(unzipped);
                unzipped.Position = 0;
                content = unzipped;
            }
            using (content) return Parse(content);
        }

        /// <summary>An Error Data File can hold one status per 1094; the transmission is as good as its worst one.</summary>
        private static string? WorstSubmissionLevelStatus(IEnumerable<string> statuses)
        {
            var all = statuses.Select(s => s.ToLowerInvariant()).ToList();
            if (all.Count == 0) return null;
            if (all.All(s => s == "rejected")) return "Rejected";
            if (all.Contains("rejected")) return "Partially Accepted";
            if (all.Any(s => s.Contains("errors"))) return "Accepted with Errors";
            return "Accepted";
        }
    }
}
