using System.Collections.Generic;
using ACA360.Core.Models.Tracker;

namespace ACA360.Core.ViewModels
{
    // Generic List ViewModel to reuse pagination logic
    public class PartnerListViewModel<T>
    {
        public IEnumerable<T> Items { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int BrokerCount { get; set; }
        public int TotalPages => (int)System.Math.Ceiling((double)TotalItems / PageSize);
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }
    }
}