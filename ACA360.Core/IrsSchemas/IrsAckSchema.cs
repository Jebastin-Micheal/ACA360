using System.Xml.Serialization;

namespace ACA360.Core.IrsSchemas
{
    // The Root of an IRS ACK File
    [XmlRoot("ACABulkRequestTransmitterStatusDetailResponse", Namespace = "urn:us:gov:treasury:irs:ext:aca:air:7.0")]
    public class IrsAckResponse
    {
        [XmlElement("ACATransmitterStatusDetail", Namespace = "urn:us:gov:treasury:irs:ext:aca:air:7.0")]
        public ACATransmitterStatusDetail Detail { get; set; }
    }

    public class ACATransmitterStatusDetail
    {
        public string ReceiptId { get; set; } // The Proof of Filing
        public string TransmissionStatusCd { get; set; } // Accepted, Rejected, AcceptedWithErrors

        // Error Summary
        public ACATransmitterErrorDetailType TransmitErrorDetail { get; set; }
    }

    public class ACATransmitterErrorDetailType
    {
        // If the whole file failed (e.g. Bad TCC), this tells us why
        public string ErrorMessageCd { get; set; }
        public string ErrorMessageTxt { get; set; }
    }
}