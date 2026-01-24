using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Interfaces
{
    public interface IEntryReader
    {
        IReadOnlyList<Competitor> Read(string filePath);
    }
}
