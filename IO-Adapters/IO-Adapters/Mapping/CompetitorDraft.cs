using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{

    public sealed class CompetitorDraft
    {
        public int RowNumber { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string Club { get; init; } = "";
        public int BirthYear { get; init; }

        public string CategoryRaw { get; init; } = "";
        public string? SexRaw { get; init; }
    }
}
