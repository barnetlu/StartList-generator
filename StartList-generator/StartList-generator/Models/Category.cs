using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Models
{
    public class Category
    {
        public string Code { get; }
        public string Name { get; }

        public SexEnum Sex { get; }
        public AgeGroup AgeGroup { get; }
        public SubGroup SubGroup { get; }
        public ObstacleType ObstacleType { get; }

        public int Lanes { get; }
        public bool SeedByPerformance { get; }

        public CategoryKey Key => new(AgeGroup, Sex, SubGroup);

        public Category(string code, string name, SexEnum sex, AgeGroup ageGroup, SubGroup subGroup, ObstacleType obstacleType,  int lanes, bool seedByPerformance)
        {
            Code = code;
            Name = name;
            Sex = sex;
            AgeGroup = ageGroup;
            SubGroup = subGroup;
            ObstacleType = obstacleType;
            Lanes = lanes;
            SeedByPerformance = seedByPerformance;
        }

        public Category()
        {
            Code = string.Empty;
            Name = string.Empty;
            Sex = SexEnum.Mixed;
            AgeGroup = AgeGroup.Unknown;
            SubGroup = SubGroup.None;
            ObstacleType = ObstacleType.Crossbar;
            Lanes = 0;
            SeedByPerformance = false;
        }

        public override string ToString() => $"{Code} ({Name})";
    }

    public readonly record struct CategoryKey(AgeGroup AgeGroup, SexEnum Sex, SubGroup SubGroup);
}
