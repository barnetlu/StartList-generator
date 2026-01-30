using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling.Report
{
    public sealed record FallbackEvent(
        int HeatNumber,
        string CategoryCode,
        FallbackReason Reason,
        string Club,
        string CompetitorName
    );
}
