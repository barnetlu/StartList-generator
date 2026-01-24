using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public interface IScheduler
    {
        public (IReadOnlyList<Heat> Heats, SchedulingReport Report) GenerateWithReport(IReadOnlyList<Competitor> competitors);
        IReadOnlyList<Heat> Generate(IReadOnlyList<Competitor> competitors);
    }
}
