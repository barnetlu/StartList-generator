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

        public Discipline Discipline { get; }
        public bool SeedByPerformance { get; }

        public CategoryKey Key => new(AgeGroup, Sex, SubGroup);

        public Category(string name, SexEnum sex, AgeGroup ageGroup, SubGroup subGroup, ObstacleType obstacleType, Discipline discipline, bool seedByPerformance, Func<AgeGroup, SexEnum, SubGroup, string> codeBuilder)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            codeBuilder = codeBuilder ?? throw new ArgumentNullException(nameof(codeBuilder));
            Sex = sex;
            AgeGroup = ageGroup;
            SubGroup = subGroup;
            ObstacleType = obstacleType;
            SeedByPerformance = seedByPerformance;
            Discipline = discipline;
            Code = codeBuilder(ageGroup, sex, subGroup);
        }

        public Category()
        {
            Code = string.Empty;
            Name = string.Empty;
            Sex = SexEnum.Mixed;
            AgeGroup = AgeGroup.Unknown;
            SubGroup = SubGroup.None;
            ObstacleType = ObstacleType.Crossbar;
            Discipline = Discipline.Run60;
            SeedByPerformance = false;
        }

        public override string ToString() => $"{Code} ({Name})";
    }

    public readonly record struct CategoryKey(AgeGroup AgeGroup, SexEnum Sex, SubGroup SubGroup);
}
