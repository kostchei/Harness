using System;
using System.IO;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Loads SRD 5e rules JSON files and exposes them as strings for injection
    /// into agent system prompts. Searches multiple paths so it works both from
    /// the DndServer output directory and the Godot project root.
    /// </summary>
    public static class SrdRules
    {
        private static string? _rulesDir;

        private static string RulesDir => _rulesDir ??= FindRulesDir();

        public static string Classes     => LoadRule("classes.json");
        public static string Races       => LoadRule("races.json");
        public static string Species     => LoadRule("species.json");
        public static string Backgrounds => LoadRule("backgrounds.json");
        public static string Combat      => LoadRule("combat.json");
        public static string Monsters    => LoadRule("monsters.json");
        public static string Progression => LoadRule("progression.json");
        public static string Equipment   => LoadRule("equipment.json");
        public static string Spells      => LoadRule("spells.json");

        private static string LoadRule(string fileName)
        {
            var path = Path.Combine(RulesDir, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"SRD rules file not found: {path}");
            return File.ReadAllText(path);
        }

        private static string FindRulesDir()
        {
            // Candidate paths in priority order:
            // 1. "rules/" next to the running executable (DndServer output)
            // 2. "agents/dnd/rules/" relative to CWD (Godot / dev)
            // 3. "../../../agents/dnd/rules/" from bin output back to project root

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "rules"),
                Path.Combine(AppContext.BaseDirectory, "agents", "dnd", "rules"),
                Path.Combine(Directory.GetCurrentDirectory(), "agents", "dnd", "rules"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "agents", "dnd", "rules"),
            };

            foreach (var dir in candidates)
            {
                var full = Path.GetFullPath(dir);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "classes.json")))
                    return full;
            }

            throw new DirectoryNotFoundException(
                "Could not locate SRD rules directory. Searched:\n" +
                string.Join("\n", candidates));
        }
    }
}
