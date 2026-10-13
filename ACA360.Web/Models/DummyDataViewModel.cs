using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class DummyDataViewModel
    {
        public List<DummyEmployer> Employers { get; set; } = new();
        public List<int> Years { get; set; } = new();
        public DummyDataRequest Defaults { get; set; } = new();
        public IReadOnlyList<DummyDataGenerator.Issue> Issues { get; set; } = DummyDataGenerator.Issues;
    }
}
