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
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Name { get; set; }
        public string ColorClass { get; set; }
    }

    public class EmailItem
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string SenderName { get; set; }
        public string SenderEmail { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string LabelId { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public bool IsRead { get; set; }
        public bool IsStarred { get; set; }
        public bool IsDeleted { get; set; }

        // Joined Properties
        public string LabelName { get; set; }
        public string ColorClass { get; set; }
    }
}
