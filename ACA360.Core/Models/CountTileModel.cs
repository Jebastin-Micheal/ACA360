namespace ACA360.Core.Models
{
    public class CountTileModel
    {
        public string Key { get; set; } = "";
        public string Span { get; set; } = "c3";
        public string Label { get; set; } = "";
        public int Value { get; set; }
        public string Sub { get; set; } = "";

        /// <summary>Boxicons class, e.g. "bx-group".</summary>
        public string Icon { get; set; } = "bx-hash";

        /// <summary>blue | sky | cyan | indigo | navy | warn | danger</summary>
        public string Accent { get; set; } = "blue";
    }
}