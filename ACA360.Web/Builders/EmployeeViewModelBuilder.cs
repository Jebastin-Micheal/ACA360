using ACA360.Core.Models;
using ACA360.Web.Models;
using System.Collections.Generic;

namespace ACA360.Web.Builders
{
    /// <summary>
    /// Assembles ViewModels and hydrates ViewBags for the EmployeeController.
    /// Extracts setup boilerplate away from the controller actions.
    /// </summary>
    public class EmployeeViewModelBuilder
    {
        private static readonly List<string> _line14Codes = new()
        {
            "1A", "1B", "1C", "1D", "1E", "1F", "1G", "1H", "1J", "1K", "1L", "1M", "1N", "1O", "1P", "1Q", "1R", "1S", "1T", "1U"
        };

        private static readonly List<string> _line16Codes = new()
        {
            "2A", "2B", "2C", "2D", "2E", "2F", "2G", "2H"
        };

        /// <summary>
        /// Populates the common ViewBag configuration required by Employee tabs/modals.
        /// </summary>
        public void PopulateViewBag(dynamic viewBag, string filingYear)
        {
            viewBag.Line14Codes = _line14Codes;
            viewBag.Line16Codes = _line16Codes;
            viewBag.FilingYear  = filingYear;
        }

        /// <summary>
        /// Constructs the details ViewModel for editing an existing employee.
        /// </summary>
        public EmployeeListViewModel BuildDetailsViewModel(EmployeeBasicDetails details)
        {
            return new EmployeeListViewModel
            {
                SelectedEmployeeDetails = details
            };
        }

        /// <summary>
        /// Constructs a blank details ViewModel for adding a new employee.
        /// </summary>
        public EmployeeListViewModel BuildNewEmployeeViewModel(string employerId, string employername, string filingyear)
        {
            return new EmployeeListViewModel
            {
                SelectedEmployeeDetails = new EmployeeBasicDetails
                {
                    EmployeeID = null,
                    EmployerId = employerId,
                    EmployerName = employername,
                    FilingYear = filingyear,
                    IsForeign = 0,
                    IsExPatriot = 0,
                    IsCorrected = 0,
                }
            };
        }

        /// <summary>
        /// Constructs the unified 1095-C ViewModel.
        /// </summary>
        public Employee1095ViewModel Build1095ViewModel(
            EmployeeBasicDetails employee,
            Employer employer,
            EmployeeCode codes,
            int year)
        {
            return new Employee1095ViewModel
            {
                Employee   = employee,
                Employer   = employer,
                Codes      = codes,
                FilingYear = year
            };
        }
    }
}
