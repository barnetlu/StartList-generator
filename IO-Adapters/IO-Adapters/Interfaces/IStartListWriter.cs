using IO_Adapters.SchedulerConfig;
using StartList_Core.Models;
using StartList_Core.Scheduling;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Interfaces
{
    internal interface IStartListWriter
    {
        void Write(string outputPath, IReadOnlyList<Heat> heats, ExcelStyleConfig excelCfg, SchedulingReport? report = null);
    }
}
