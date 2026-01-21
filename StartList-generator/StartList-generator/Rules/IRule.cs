using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Rules
{
    public interface IRule
    {
        bool IsSatisfied(Heat heat);
    }
}
