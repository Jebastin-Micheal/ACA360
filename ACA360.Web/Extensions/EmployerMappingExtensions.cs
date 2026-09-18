using ACA360.Core.Models;
using ACA360.Web.Models;

namespace ACA360.Web.Extensions
{
    /// <summary>
    /// Extension methods that map Employer Web ViewModels to Core Domain models.
    /// Keeps POST action methods in EmployerController thin — one line to map, one line to call service.
    /// </summary>
    public static class EmployerMappingExtensions
    {
        // ===================================================================
        // 1. EmployerDetailsViewModel → Employer (UpdateEmployerCommon)
        //    Replaces ~15 manual field assignments in UpdateEmployerCommon action
        // ===================================================================

        /// <summary>
        /// Maps the common details form model to the core Employer domain entity for update operations.
        /// </summary>
        public static Employer ToDomainModel(this EmployerDetailsViewModel vm) => new Employer
        {
            Aca_Id           = vm.ACA_EmployerId,
            EIN              = vm.EIN,
            Name             = vm.Name,
            Type             = vm.Type,
            FilingYear       = vm.FilingYear,
            Address1         = vm.Address1,
            Address2         = vm.Address2,
            City             = vm.City,
            State            = vm.State,
            StateId          = vm.StateId,
            ZipCode          = vm.ZipCode,
            Country          = vm.Country,
            IsForeignAddress  = vm.IsForeignAddress,
            IsCorrected      = vm.IsCorrected,
            enableCallCenter = vm.enableCallCenter
        };

        // ===================================================================
        // 2. AddEmployerViewModel → Employer (SaveEmployer1094Tab)
        //    Replaces ~14 manual field assignments in SaveEmployer1094Tab action
        // ===================================================================

        /// <summary>
        /// Maps the 1094 tab form model to the Employer domain entity for 1094 save operations.
        /// </summary>
        public static Employer To1094DomainModel(this AddEmployerViewModel vm) => new Employer
        {
            Id           = vm._1094View.EmployerId,
            FilingYear   = vm._1094View.FilingYear,
            EIN          = vm._1094View.EIN,
            Name         = vm._1094View.Name,
            FormType     = vm._1094View.FormType,
            OriginCode   = vm._1094View.OriginCode,
            CertA        = vm._1094View.CertA,
            CertB        = vm._1094View.CertB,
            CertC        = vm._1094View.CertC,
            CertD        = vm._1094View.CertD,
            FullTime         = vm._1094View.FullTime,
            Total            = vm._1094View.Total,
            Sec4980H         = vm._1094View.Sec4980H,
            MinimumCoverage  = vm._1094View.MinimumCoverage,
            AggregateGroup   = vm._1094View.AggregateGroup,
            DisableAutoCounts = vm._1094View.DisableAutoCounts
        };

        // ===================================================================
        // 3. AffiliateViewModel → AffiliateDto (AddAffiliate)
        //    Replaces ~10 manual field assignments in AddAffiliate action
        // ===================================================================

        /// <summary>
        /// Maps the web AffiliateViewModel to the core AffiliateDto for service layer calls.
        /// AffiliateId is always 0 for new inserts — the DB assigns the identity.
        /// </summary>
        public static AffiliateDto ToDto(this AffiliateViewModel vm) => new AffiliateDto
        {
            AffiliateId = vm.AffiliateId,
            FilingYear = vm.FilingYear,
            EmployerId = vm.AffiliateId,       // 0 = INSERT, >0 = UPDATE
            ParentEmployerId = vm.ParentEmployerId,
            AffiliateName = vm.AffiliateName,     // ← updated
            EIN = vm.EIN,               // ← updated
            Phone = vm.Phone,             // ← add
            Email = vm.Email,             // ← add
            Address = vm.Address,
            City = vm.City,
            State = vm.State,
            Zip = vm.Zip,
            NumberOfEmployees = vm.NumberOfEmployees
        };

        // ===================================================================
        // 4. ContactViewModel → ContactDto (AddContact)
        //    Replaces ~8 manual field assignments in AddContact action
        // ===================================================================

        /// <summary>
        /// Maps the web ContactViewModel to the core ContactDto for service layer calls.
        /// </summary>
        public static ContactDto ToDto(this ContactViewModel vm) => new ContactDto
        {
            ContactId  = int.TryParse(vm.ContactId, out var id) ? id : 0,
            EmployerId = vm.EmployerId,
            Name       = vm.Name,
            Phone      = vm.Phone,
            Email      = vm.Email,
            IsBilling  = vm.IsBilling
        };
    }
}
