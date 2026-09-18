using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class AddEmployerViewModel
    {
        public Employer Employer { get; set; } = new Employer();
        public EmployerDropdownDataModel Dropdowns { get; set; } = new EmployerDropdownDataModel();
        public EmployerInfoModel employer_trackerinfo { get; set; } = new EmployerInfoModel(); 
        public EmployerImportantInfo ImportantInfo { get; set; } = new EmployerImportantInfo();
        public EmployerListViewModel ListView { get; set; } = new EmployerListViewModel();
        public EmployerDetailsViewModel DetailsView { get; set; } = new EmployerDetailsViewModel();

        public Employer1094ViewModel _1094View { get; set; } = new Employer1094ViewModel();
        public AuditLogViewModel AuditLogView { get; set; }= new AuditLogViewModel();

        public EmployerServiceInfoViewModel EmployerServiceInfo { get; set; } = new EmployerServiceInfoViewModel();
        public ServiceDetail Service { get; set; } = new ServiceDetail();



        
    }
}
