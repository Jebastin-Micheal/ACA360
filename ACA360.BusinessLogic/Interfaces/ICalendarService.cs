using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ICalendarService
    {
        List<CalendarEventModel> GetEventsByUser(long userId);
        int AddEvent(long userId, CalendarEventModel model);
        void UpdateEvent(long userId, CalendarEventModel model);
        void DeleteEvent(long userId, int id);
    }
}