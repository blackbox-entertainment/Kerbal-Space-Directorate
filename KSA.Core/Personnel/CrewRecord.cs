using System;

namespace KSA.Core.Personnel
{
    /// <summary>A read-only snapshot of one crew member, as shown in the roster.</summary>
    public class CrewRecord
    {
        public const int MaxStars = 5;

        public string Name { get; }
        public CrewRole Role { get; }
        /// <summary>Experience level, 0 to <see cref="MaxStars"/>.</summary>
        public int Stars { get; }
        public CrewStatus Status { get; }

        public CrewRecord(string name, CrewRole role, int stars, CrewStatus status)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            Name = name;
            Role = role;
            Stars = stars < 0 ? 0 : (stars > MaxStars ? MaxStars : stars);
            Status = status;
        }

        public override string ToString()
        {
            return Name + " (" + Role + ", " + Stars + "*, " + Status + ")";
        }
    }
}
