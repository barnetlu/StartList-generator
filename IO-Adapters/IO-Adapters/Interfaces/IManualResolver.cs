using IO_Adapters.Mapping;
using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Interfaces
{
    public interface IManualResolver
    {
        SexEnum Resolve(CompetitorDraft draft, AgeGroup group);
    }
}
