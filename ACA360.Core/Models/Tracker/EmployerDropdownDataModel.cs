using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models.Tracker
{
    // ── Master dropdown container returned by sp_GetDropdownDataEmployer_new1_tracker ──
    public class EmployerDropdownDataModel
    {
        // RS 1: Industry
        public List<DropdownItem> Industries { get; set; } = new();
        // RS 2: DataType
        public List<DropdownItem> DataTypes { get; set; } = new();
        // RS 3: Account Managers
        public List<DropdownItem> AccountManagers { get; set; } = new();
        // RS 4: Data Analysts
        public List<DropdownItem> DataAnalysts { get; set; } = new();
        // RS 5: Sales Reps
        public List<DropdownItem> SalesReps { get; set; } = new();
        // RS 6: Firms
        public List<DropdownItem> Firms { get; set; } = new();
        // RS 7: Brokers
        public List<DropdownItem> Brokers { get; set; } = new();
        // RS 8: States
        public List<DropdownItem> States { get; set; } = new();
        // RS 9: Affiliates — SKIPPED (separate logic exists)
        // RS 10: General Contacts
        public List<ContactDropdownItem> GeneralContacts { get; set; } = new();
        // RS 11: Billing Contacts
        public List<ContactDropdownItem> BillingContacts { get; set; } = new();
        // RS 12: Broker Contacts
        public List<ContactDropdownItem> BrokerContacts { get; set; } = new();
        // RS 13: States ACA
        public List<StateACAItem> StatesACA { get; set; } = new();
        // RS 14: Country ACA
        public List<CountryACAItem> CountriesACA { get; set; } = new();
        // RS 15: Broker Entity Details
        public string BrokerPhone { get; set; }
        public string BrokerEmail { get; set; }
    }

    // ── Generic ID/Name pair (covers RS 1–8) ──
    public class DropdownItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    // ── Contact with phone/email (covers RS 10, 11, 12) ──
    public class ContactDropdownItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string? Connect_User { get; set; }
    }

    // ── State ACA (RS 13) ──
    public class StateACAItem
    {
        public int Id { get; set; }
        public string State { get; set; }
        public string Code { get; set; }
    }

    // ── Country ACA (RS 14) ──
    public class CountryACAItem
    {
        public int Id { get; set; }
        public string Country { get; set; }
        public string Code { get; set; }
    }
}