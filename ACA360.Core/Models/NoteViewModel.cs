using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class NoteViewModel
    {
        public int NoteId { get; set; }
        public string? NoteMessage { get; set; }
        public string? AuthorName { get; set; }
        public DateTime CreatedOn { get; set; }
        public bool IsMine { get; set; }     // True if I wrote it (for styling)
        public bool IsInternal { get; set; } // True if Staff-Only note
        public string? FormattedDate => CreatedOn.ToString("MMM dd, HH:mm");
    }

    public class AddNoteModel
    {
        public string? EntityType { get; set; } // 'FileLog', 'Employer', etc.
        public int EntityId { get; set; }
        public string? Message { get; set; }
        public bool IsInternal { get; set; }
    }
}
