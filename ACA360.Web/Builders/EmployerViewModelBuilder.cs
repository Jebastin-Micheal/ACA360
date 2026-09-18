using ACA360.Core.Models;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Linq;
using System.Collections.Generic;
using System;

namespace ACA360.Web.Builders
{
    /// <summary>
    /// Responsible for assembling all Employer-related ViewModels from domain data.
    /// Keeps EmployerController thin by moving ViewModel construction here.
    /// </summary>
    public class EmployerViewModelBuilder
    {
        // ===================================================================
        // Static dropdown options shared across ViewModels
        // ===================================================================

        private static readonly List<SelectListItem> _originCodeOptions = new()
        {
            new SelectListItem { Value = "", Text = "-- Select Origin Code --" },
            new SelectListItem { Value = "A", Text = "A" },
            new SelectListItem { Value = "B", Text = "B" },
            new SelectListItem { Value = "C", Text = "C" },
            new SelectListItem { Value = "D", Text = "D" },
            new SelectListItem { Value = "E", Text = "E" },
            new SelectListItem { Value = "F", Text = "F" },
            new SelectListItem { Value = "G", Text = "G" },
            new SelectListItem { Value = "H", Text = "H" }
        };

        private static readonly List<SelectListItem> _formTypeOptions = new()
        {
            new SelectListItem { Value = "", Text = "-- Select Form Type --" },
            new SelectListItem { Value = "1095C", Text = "1095C" },
            new SelectListItem { Value = "1095B", Text = "1095B" }
        };

        // ===================================================================
        // 1. BUILD EMPLOYER COMMON DETAILS VIEWMODEL
        //    Replaces ~60 lines from GetCommonDetails action in EmployerController
        // ===================================================================

        /// <summary>
        /// Builds the EmployerDetailsViewModel for the Common Details tab.
        /// Handles the DB mismatch where State may be stored as a code ("NY") or a numeric ID ("36").
        /// </summary>
        public EmployerDetailsViewModel BuildDetailsViewModel(
      Employer employer,
      EmployerInfoModel? info,
      EmployerDropdownDataModel dropdowns,
      EmployerDropdownDataModel billingDropdowns)
        {
            // Resolve State: DB may store a code ("NY") or an ID ("36"). We need both.
            var (stateId, stateCode) = ResolveState(employer.State, dropdowns);

            return new EmployerDetailsViewModel
            {
                EmployerId = employer.Id,
                ACA_EmployerId = employer.Aca_Id ?? 0,
                EIN = employer.EIN,
                Name = employer.Name,
                Type = employer.Type,
                FilingYear = employer.FilingYear,
                Address1 = employer.Address1,
                Address2 = employer.Address2,
                City = employer.City,
                State = stateCode,         // Resolved short code (e.g. "NY")
                StateId = employer.StateId ?? stateId,
                ZipCode = employer.ZipCode,
                Country = employer.Country,
                IsForeignAddress = employer.IsForeignAddress,
                IsCorrected = employer.IsCorrected,
                enableCallCenter = employer.enableCallCenter,
                Employer = info,
                DropdownData = dropdowns,

                StateList = dropdowns.State?
                    .Select(s => new SelectListItem
                    {
                        Value = s.Key.ToString(),
                        Text = s.Value,
                        Selected = s.Key.ToString() == (employer.StateId ?? stateId)
                    }).ToList() ?? new(),

                CountryList = dropdowns.Country?
                    .Select(c => new SelectListItem
                    {
                        Value = c.Key.ToString(),
                        Text = c.Value,
                        Selected = c.Value.Equals(employer.Country, StringComparison.OrdinalIgnoreCase)
                    }).ToList() ?? new(),
                FirmList = dropdowns.Firms?
             .Select(f => new SelectListItem
             {
                 Value = f.Key.ToString(),
                 Text = f.Value,
                 Selected = f.Key.ToString() == info?.FirmId?.ToString()
             }).ToList() ?? new(),

                BrokerList = dropdowns.Brokers?
             .Select(b => new SelectListItem
             {
                 Value = b.Key.ToString(),
                 Text = b.Value,
                 Selected = b.Key.ToString() == info?.BrokerId?.ToString()
             }).ToList() ?? new(),

                AccountManagerList = dropdowns.AccountManagers?
             .Select(a => new SelectListItem
             {
                 Value = a.Key.ToString(),
                 Text = a.Value,
                 Selected = a.Key.ToString() == info?.AccountManagerId?.ToString()
             }).ToList() ?? new(),

                SalesRepList = dropdowns.SalesReps?
             .Select(s => new SelectListItem
             {
                 Value = s.Key.ToString(),
                 Text = s.Value,
                 Selected = s.Key.ToString() == info?.SalesRepId?.ToString()
             }).ToList() ?? new(),

                DataAnalystList = dropdowns.DataAnalysts?
             .Select(d => new SelectListItem
             {
                 Value = d.Key.ToString(),
                 Text = d.Value,
                 Selected = d.Key.ToString() == info?.DataAnalyst?.ToString()
             }).ToList() ?? new(),
                RawContacts = dropdowns.Contacts ?? new(),
                RawBillingContacts = billingDropdowns.BillingContacts ?? new()
            };
        }

        // ===================================================================
        // 2. BUILD EMPLOYER INFO (TRACKER) VIEWMODEL
        //    Replaces ~40 lines from LoadEmployerInfo action
        // ===================================================================

        /// <summary>
        /// Builds the EmployerDetailsViewModel for the Employer Info / Tracker tab.
        /// Projects all four dropdown lists (Firm, Broker, SalesRep, AM, DA) with pre-selected values.
        /// </summary>
        public EmployerDetailsViewModel BuildInfoViewModel(
            EmployerInfoModel info,
            EmployerDropdownDataModel dropdowns,
            EmployerDropdownDataModel contacts,
            EmployerDropdownDataModel billing)
        {
            return new EmployerDetailsViewModel
            {
                Employer = info,
                RawContacts        = contacts.Contacts ?? new(),
                RawBillingContacts = billing.BillingContacts ?? new(),

                FirmList = BuildSelectList(dropdowns.Firms, "-- Select Firm --",
                    selected: kv => info.FirmId == kv.Key),

                BrokerList = BuildSelectList(dropdowns.Brokers, "-- Select Broker --",
                    selected: kv => info.BrokerId == kv.Key),

                SalesRepList = BuildSelectList(dropdowns.SalesReps, "-- Select Sales Rep --",
                    selected: kv => info.SalesRepId == kv.Value),

                AccountManagerList = BuildSelectList(dropdowns.AccountManagers, "-- Select Account Manager --",
                    selected: kv => info.AccountManagerId == kv.Key),

                DataAnalystList = BuildSelectList(dropdowns.DataAnalysts, "-- Select Data Analyst --",
                    selected: kv => kv.Value == info.DataAnalyst)
            };
        }

        // ===================================================================
        // 3. BUILD 1094 VIEWMODEL
        //    Replaces ~30 lines from Employer1094Tab action
        // ===================================================================

        /// <summary>
        /// Builds the Employer1094ViewModel, hydrating it with static option lists.
        /// </summary>
        public Employer1094ViewModel Build1094ViewModel(Employer employer)
        {
            return new Employer1094ViewModel
            {
                EmployerId       = employer.Id,
                FilingYear       = employer.FilingYear,
                EIN              = employer.EIN,
                Name             = employer.Name,
                FormType         = employer.FormType,
                OriginCode       = employer.OriginCode,
                CertA            = employer.CertA,
                CertB            = employer.CertB,
                CertC            = employer.CertC,
                CertD            = employer.CertD,
                MinimumCoverage  = employer.MinimumCoverage ?? new int[13],
                FullTime         = employer.FullTime ?? new int[13],
                Total            = employer.Total ?? new int[13],
                AggregateGroup   = employer.AggregateGroup ?? new int[13],
                Sec4980H         = employer.Sec4980H ?? new int[13],
                DisableAutoCounts = employer.DisableAutoCounts,
                OriginCodeOptions = new List<SelectListItem>(_originCodeOptions)
            };
        }

        // ===================================================================
        // 4. BUILD ADD EMPLOYER VIEWMODEL
        //    Replaces ~80 lines from AddEmployerCommon [GET] action
        // ===================================================================

        /// <summary>
        /// Builds the full AddEmployerViewModel for the Add Employer modal.
        /// </summary>
        public AddEmployerViewModel BuildAddEmployerViewModel(
 string filingYear,
 EmployerDropdownDataModel dropdowns,
 List<ServiceListDropdown> services,
 List<AuditUser> auditUsers,
 List<Process_steps> fteSteps,
 List<Process_steps> steps1095,
 List<Process_steps> stateFilingSteps)
        {
            return new AddEmployerViewModel
            {
                Employer = new Employer { FilingYear = filingYear },
                employer_trackerinfo = new EmployerInfoModel(),
                Dropdowns = dropdowns,
                ImportantInfo = new EmployerImportantInfo(),
                ListView = new EmployerListViewModel(),
                DetailsView = new EmployerDetailsViewModel(),
                AuditLogView = new AuditLogViewModel(),   // ✅ restored

                _1094View = new Employer1094ViewModel
                {
                    FormTypeOptions = new List<SelectListItem>(_formTypeOptions),
                    OriginCodeOptions = new List<SelectListItem>(_originCodeOptions)
                },

                EmployerServiceInfo = new EmployerServiceInfoViewModel
                {
                    PlanYear = filingYear,   // ✅ restored

                    // ✅ restored - services list was passed but never used
                    // ✅ Filter nulls + safe ToString
                    ServiceNames = services
    .Where(s => s.ServiceListId != null)   // skip nulls
    .Select(s => new SelectListItem
    {
        Value = s.ServiceListId.ToString(),
        Text = s.Name
    }).ToList(),

                    // ✅ keep flat SelectListItem lists if your view expects them here
                    AuditUsers = auditUsers.Select(u => new SelectListItem
                    {
                        Value = u.Id.ToString(),
                        Text = u.Name
                    }).ToList(),

                    ImplementationProcesses = steps1095.Select(p => new SelectListItem
                    {
                        Value = p.ProcessId.ToString(),
                        Text = p.ProcessName
                    }).ToList(),

                    FTEProcess = fteSteps.Select(p => new SelectListItem
                    {
                        Value = p.ProcessId.ToString(),
                        Text = p.ProcessName
                    }).ToList(),

                    StateFilingProcess = stateFilingSteps.Select(p => new SelectListItem
                    {
                        Value = p.ProcessId.ToString(),
                        Text = p.ProcessName
                    }).ToList(),

                    SelectedService = new ServiceDetail
                    {
                        PlanYear = filingYear,   // ✅ restored
                                                 // only keep the nested lists if ServiceDetail actually uses them
                                                 // and if your view/POST binding expects them here
                        AuditUsers = auditUsers,
                        _implementation = steps1095.Select(p => new ImplementationProcess
                        {
                            Id = p.ProcessId,
                            Name = p.ProcessName
                        }).ToList(),
                        _1094Process = fteSteps.Select(p => new ProcessStep1094
                        {
                            Id = p.ProcessId,
                            Name = p.ProcessName
                        }).ToList(),
                        _statefilingprocess = stateFilingSteps.Select(p => new StateFilingProcessStep
                        {
                            Id = p.ProcessId,
                            Name = p.ProcessName
                        }).ToList()
                    }
                }
            };
        }

        // ===================================================================
        // 5. ENRICH SERVICE DETAIL
        //    Replaces ~30 lines from LoadServiceDetail action
        // ===================================================================

        /// <summary>
        /// Enriches a ServiceDetail model with AuditUsers and all process step lists.
        /// Called after the service detail record is fetched so the view has the full picture.
        /// </summary>
        public void EnrichServiceDetail(
            ServiceDetail detail,
            List<AuditUser> auditUsers,
            List<Process_steps> fteSteps,
            List<Process_steps> steps1095,
            List<Process_steps> stateFilingSteps)
        {
            detail.AuditUsers = auditUsers;

            detail._implementation = steps1095.Select(p => new ImplementationProcess
            {
                Id = p.ProcessId, Name = p.ProcessName
            }).ToList();

            detail._1094Process = fteSteps.Select(p => new ProcessStep1094
            {
                Id = p.ProcessId, Name = p.ProcessName
            }).ToList();

            detail._statefilingprocess = stateFilingSteps.Select(p => new StateFilingProcessStep
            {
                Id = p.ProcessId, Name = p.ProcessName
            }).ToList();
        }

        // ===================================================================
        // PRIVATE HELPERS
        // ===================================================================

        /// <summary>
        /// Resolves the State field from the DB — which may be a code ("NY") or a numeric ID ("36").
        /// Returns (stateId, stateCode) tuple so both are available to the ViewModel.
        /// </summary>
        private static (string stateId, string stateCode) ResolveState(
            string? rawState,
            EmployerDropdownDataModel dropdowns)
        {
            if (string.IsNullOrEmpty(rawState) || dropdowns.State == null || !dropdowns.State.Any())
                return (rawState ?? "", rawState ?? "");

            // Case A: DB stored the short code (e.g. "NY") — find its numeric ID
            if (!int.TryParse(rawState, out _))
            {
                var match = dropdowns.State.FirstOrDefault(s =>
                    s.Value.Equals(rawState, StringComparison.OrdinalIgnoreCase));
                return match.Key != 0
                    ? (match.Key.ToString(), match.Value)
                    : (rawState, rawState);
            }

            // Case B: DB stored the numeric ID (e.g. "36") — find its short code
            if (int.TryParse(rawState, out int parsedId) && dropdowns.State.TryGetValue(parsedId, out var code))
                return (parsedId.ToString(), code);

            return (rawState, rawState);
        }

        /// <summary>
        /// Projects a Dictionary{int,string} into a SelectListItem list with a placeholder header.
        /// </summary>
        private static List<SelectListItem> BuildSelectList(
            Dictionary<int, string>? source,
            string placeholder,
            Func<KeyValuePair<int, string>, bool> selected)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = placeholder }
            };

            if (source != null)
            {
                list.AddRange(source.Select(kv => new SelectListItem
                {
                    Value    = kv.Key.ToString(),
                    Text     = kv.Value,
                    Selected = selected(kv)
                }));
            }

            return list;
        }
    }
}
