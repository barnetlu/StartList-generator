using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling.Report;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Interfaces
{
    internal interface IStartListWriter
    {
        void Write(string outputPath, IReadOnlyList<Heat> heats, AppConfig excelCfg, SchedulingReport? report = null, bool onlyClubsSheet = false);
    }
}
