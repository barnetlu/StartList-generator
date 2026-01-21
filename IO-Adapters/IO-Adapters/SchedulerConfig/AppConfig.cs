using System;
using System.Collections.Generic;
using System.Text;
using StartList_Core.Scheduling;

namespace IO_Adapters.SchedulerConfig
{
    public sealed class AppConfig
    {
        public StartList_Core.Scheduling.SchedulerConfig Scheduler { get; init; } = new();      // Core typ
        public ExcelStyleConfig Excel { get; init; } = new();        // IO typ
    }
}
