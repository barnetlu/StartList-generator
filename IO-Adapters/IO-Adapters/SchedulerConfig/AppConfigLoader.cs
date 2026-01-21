using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.SchedulerConfig
{
    public static class AppConfigLoader
    {
        public static AppConfig Load(string path)
        {
            var scheduler = SchedulerConfigLoader.LoadSchedulerConfig(path);
            return new AppConfig
            {
                Scheduler = SchedulerConfigLoader.LoadSchedulerConfig(path),
                Excel = ExcelStyleConfigLoader.Load(path, scheduler.TrackPlan.TotalLanes)

            };
        }
    }
}
