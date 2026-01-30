using StartList_Core.Models;
using StartList_Core.Scheduling.Report;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Interfaces
{
    public interface IScheduler
    {
        public (IReadOnlyList<Heat> Heats, SchedulingReport Report) GenerateWithReport(IReadOnlyList<Competitor> competitors);
        IReadOnlyList<Heat> Generate(IReadOnlyList<Competitor> competitors);
    }
}
