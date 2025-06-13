using System.Collections.Generic;

namespace SUSModder.Models
{
    public class Role
    {
        public int Id { get; set; }
        public int ModId { get; set; } // Ta właściwość musi istnieć
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public List<Ability> Abilities { get; set; } = new();
    }

    public class Ability
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
