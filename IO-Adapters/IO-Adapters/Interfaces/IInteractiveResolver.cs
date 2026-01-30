using IO_Adapters.Mapping;
using StartList_Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Interfaces
{
    public interface IInteractiveResolver
    {
        SexEnum ResolveSex(CompetitorDraft draft, AgeGroup group);
        int ResolveBirthYear(CompetitorDraft draft, string? birthRaw);
        (AgeGroup, SubGroup) ResolveAgeGroupMismatch(CompetitorDraft draft, AgeGroup parsedGroup, SubGroup parsedSub, AgeGroup expectedGroup, SubGroup expectedSub, IReadOnlySet<AgeGroup> noSubGroupIfGroup);
    }
}
