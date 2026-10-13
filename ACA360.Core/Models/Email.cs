using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


// EmailItem & Label
namespace ACA360.Core.Models
{


    public class Label
    {
        public string Id { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string ColorClass { get; set; } = null!;
    }

    public class EmailItem
    {
        public string Id { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string SenderName { get; set; } = null!;
        public string SenderEmail { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string LabelId { get; set; } = null!;
        public DateTime? ReceivedDate { get; set; }
        public bool IsRead { get; set; }
        public bool IsStarred { get; set; }
        public bool IsDeleted { get; set; }

        // Joined Properties
        public string LabelName { get; set; } = null!;
        public string ColorClass { get; set; } = null!;
    }
}
