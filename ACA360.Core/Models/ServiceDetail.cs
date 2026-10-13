using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ServiceDetail
    {
        public string? EmployerId { get; set; }
        public string? TrackerEmployerId { get; set; }
        public string? EmployerServiceId { get; set; }
        public string? ServiceId { get; set; }
        public string? ServiceListId { get; set; }
        public string ? PlanYear { get; set; }
        public string? ServiceName { get; set; }
        public string? Status { get; set; }
        public string? ProposalSent { get; set; }
        public string? ProposalReturned { get; set; }
        public string? ProposalSigned { get; set; }
        public string? ContractSent { get; set; }
        public string? ContractSigned { get; set; }

        public string? ImplementationProcess { get; set; }
        public string? LastUpdated { get; set; } // _implementationproclastupdated
        public string? FollowUpDate { get; set; } // _followupdate

        public string? ProcessStep1094 { get; set; }
        public string? FTETrackingStep_lastupdated { get; set; }
        public string? FTETrackingStep_nextfollowup { get; set; }
       

        public string? StateFilingProcessStep { get; set; }
        public string? StateFilingProcessStepUpdated { get; set; }
        public string? StateFiling_FollowUpDate { get; set; } // _followupdate

        public string? ExtensionFiled { get; set; }
        public string? AuditBy { get; set; }

        // Dropdown list of available AuditBy options
        public List<AuditUser> AuditUsers { get; set; } = new List<AuditUser>();
        public List<Process_steps>? Process { get; set; }

        public List<ImplementationProcess> _implementation { get; set; } = new List<ImplementationProcess>();
        public List<ProcessStep1094> _1094Process { get; set; } = new List<ProcessStep1094>();
        public List<StateFilingProcessStep> _statefilingprocess { get; set; } = new List<StateFilingProcessStep>();
        public string? ReceiptId { get; set; }

        
    }
    // ✅ Define your AuditBy class inside the same namespace
    public class AuditUser
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
    public class ImplementationProcess
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
    public class ProcessStep1094
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
    public class StateFilingProcessStep
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
    public class ServiceListDropdown
    {
        public string? ServiceListId { get; set; }
        public string? Name { get; set; } 
    }
    public class ProcessHistoryDto
    {
        public DateTime CreatedDate { get; set; }
        public string? UpdatedBy { get; set; }
        public string? ProcessName { get; set; }
        public DateTime? NextFollowUpDate { get; set; }
    }
}
