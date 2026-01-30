using StartList_Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace IO_Adapters.Mapping
{
    public static class CategoryCodeBuilder
    {
        public static string Build(AgeGroup group, SexEnum sex, SubGroup sub)
        {
            if(group != AgeGroup.Pripravka && sex != SexEnum.Male && sex != SexEnum.Female)
            {
                sex = SexEnum.Mixed;
            }
            var g = group switch
            {
                AgeGroup.Pripravka => "PR",
                AgeGroup.Mladsi => "ML",
                AgeGroup.Starsi => "ST",
                AgeGroup.Dorost => "DOR",
                AgeGroup.Dospeli => "AD",
                _ => "UNK"
            };

            var sg = sub switch
            {
                SubGroup.Mladsi => "-ML",
                SubGroup.Stredni => "-STR",
                SubGroup.Starsi => "-ST",
                _ => ""
            };

            var s = sex switch
            {
                SexEnum.Mixed => "MIX",
                SexEnum.Male => "M",
                SexEnum.Female => "F",
                _ => "UNK"
            };

            return $"{g}{sg}_{s}";
        }
    }
}
