using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public interface IScheduler
    {
        IReadOnlyList<Heat> Generate(IReadOnlyList<Competitor> competitors);
    }
}
