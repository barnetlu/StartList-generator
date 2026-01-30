using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Report
{
    public sealed record FallbackPickInfo(
    int HeatNo,
    string RequestedPool,   // např. "Crossbar: ML_F"
    string UsedPool,        // např. "Crossbar: ST_F"
    string CompetitorName,
    string Club);
}
