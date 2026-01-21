using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
    public enum FallbackReason
    {
        RelaxCooldown,     // nebyl možný klub bez porušení cooldownu -> ignorovali jsme cooldown
        ForcedAnyClub      // nebylo možné ani to (typicky kvůli 1 club/heat) -> vzali jsme jakýkoli klub
    }
}
