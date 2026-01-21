using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public enum MapType { Skupina, Pohlavi, Podskupina }

    public sealed record MapRule<T>(
        string[] Patterns,   // už normalizované
        T Value,        // canonical string (např. "Mladší", "Dívky")
        int Priority = 0,     // pro případ konfliktů
        bool ExactOnly = false
    );

}
