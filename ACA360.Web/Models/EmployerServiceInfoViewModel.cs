using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class EmployerServiceInfoViewModel
    {
        public string? EmployerId { get; set; }
        public string? PlanYear { get; set; }

        public List<ServiceListItem> ServiceList { get; set; } = new List<ServiceListItem>();
        public List<SelectListItem> AuditUsers { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> ImplementationProcesses { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> FTEProcess { get; set; } = new();
        public List<SelectListItem> StateFilingProcess { get; set; } = new List<SelectListItem>();

        public ServiceDetail SelectedService { get; set; } = new ServiceDetail();


        public string? TrackerEmployerId { get; set; }
        public List<SelectListItem> ServiceNames { get; set; } = new List<SelectListItem>();
    }
}
