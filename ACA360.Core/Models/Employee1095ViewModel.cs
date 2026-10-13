using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class Employee1095ViewModel
    {
        public Employee? Employee { get; set; }
        public EmployeeCode? Codes { get; set; }
        public int FilingYear { get; set; }
        public Employer? Employer { get; set; } // Helpful for header info
    }
}