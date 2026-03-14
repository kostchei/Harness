using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Guides the player through D&D 2024 character creation.
    /// The LLM handles the conversation; all mechanics are deterministic.
    /// Tools: roll_ability_scores, build_character, lookup_srd (inherited).
    /// </summary>
    public class CharacterCreator : DndAgent
    {
        private readonly Random _rng = new();
        private int[]? _lastRolledScores;

        public CharacterCreator(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "CharacterCreator";

        protected override string SystemPrompt =>
            """
            You are CharacterCreator, a wise sage who guides adventurers through D&D 2024 character creation.

            ## 2024 Rules Summary
            - SPECIES (not "race"): provides traits, speed, darkvision — but NO ability score bonuses.
            - BACKGROUND: provides +2 to one ability and +1 to a different ability (player chooses which),
              plus 2 skill proficiencies, 1 tool proficiency, 1 Origin feat, and starting equipment.
            - CLASS: determines hit die, saving throws, armor/weapon proficiencies, features, and spells.
            - Ability scores are rolled (4d6 drop lowest x6), then the player assigns them to STR/DEX/CON/INT/WIS/CHA.
            - Background ability boosts are applied AFTER assignment.

            ## Your Workflow
            1. Greet the player warmly. Ask what kind of character they'd like (or offer to randomise).
            2. Help them choose a SPECIES. Use lookup_srd(topic:"species") to see options and present them.
            3. Help them choose a CLASS. Use lookup_srd(topic:"classes") to see options and present them.
            4. Help them choose a BACKGROUND. Use lookup_srd(topic:"backgrounds") to see options and present them.
            5. Call roll_ability_scores to generate 6 scores.
            6. Present the rolled scores and ask the player to assign each to STR, DEX, CON, INT, WIS, CHA.
               Suggest optimal assignments for their class (e.g. Wizard wants high INT).
            7. Ask which ability gets +2 and which gets +1 from their background (must be different abilities).
            8. Ask for character name, alignment, and backstory.
            9. Call build_character with ALL choices. The tool deterministically computes HP, AC, equipment, etc.
            10. Narrate the finished character in an evocative paragraph.
                Wrap the build_character result in <character_json> tags for the orchestrator.

            ## Important
            - You MUST call roll_ability_scores — do NOT invent ability scores.
            - You MUST call build_character — do NOT compute HP, AC, or equipment yourself.
            - The build_character tool applies background boosts, so pass the BASE rolled scores (before boosts).
            - Respond in-character as a wise sage. Keep responses concise.
            """;

        protected override List<ChatTool> Tools => new()
        {
            MakeTool(
                "roll_ability_scores",
                "Roll 4d6 drop lowest for each of 6 ability scores. Returns 6 scores in descending order for the player to assign.",
                new Dictionary<string, object>()),

            MakeTool(
                "build_character",
                "Deterministically build a complete character from player choices. Applies background ability boosts, calculates HP/AC/equipment from SRD data. Returns the full Character JSON.",
                new Dictionary<string, object>
                {
                    ["name"]           = StringProp("Character name"),
                    ["species"]        = StringProp("Species name (must match SRD: Human, Elf, Dwarf, Halfling, Gnome, Orc, Tiefling, Dragonborn, Goliath, Aasimar)"),
                    ["class_name"]     = StringProp("Class name (must match SRD: Fighter, Wizard, Rogue, Cleric, Ranger, Paladin, Barbarian, Bard)"),
                    ["background"]     = StringProp("Background name (must match SRD: Acolyte, Charlatan, Criminal, Entertainer, Farmer, Guard, Guide, Hermit, Merchant, Noble, Sage, Sailor, Scribe, Soldier, Wayfarer)"),
                    ["alignment"]      = StringProp("Alignment (e.g. Chaotic Good)"),
                    ["backstory"]      = StringProp("Short backstory paragraph"),
                    ["strength"]       = IntProp("BASE Strength score (before background boost)"),
                    ["dexterity"]      = IntProp("BASE Dexterity score (before background boost)"),
                    ["constitution"]   = IntProp("BASE Constitution score (before background boost)"),
                    ["intelligence"]   = IntProp("BASE Intelligence score (before background boost)"),
                    ["wisdom"]         = IntProp("BASE Wisdom score (before background boost)"),
                    ["charisma"]       = IntProp("BASE Charisma score (before background boost)"),
                    ["boost_plus2"]    = StringProp("Ability that gets +2 from background (one of: strength, dexterity, constitution, intelligence, wisdom, charisma)"),
                    ["boost_plus1"]    = StringProp("Ability that gets +1 from background (must be different from boost_plus2)"),
                },
                new List<string> { "name", "species", "class_name", "background", "alignment", "backstory",
                    "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma",
                    "boost_plus2", "boost_plus1" })
        };

        protected override Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            return toolName switch
            {
                "roll_ability_scores" => Task.FromResult(RollAbilityScores()),
                "build_character"     => Task.FromResult(BuildCharacter(input)),
                _                     => base.HandleToolCallAsync(toolName, input)
            };
        }

        // ── Roll 4d6 drop lowest x6, return sorted descending ───────────────

        private string RollAbilityScores()
        {
            var scores = new int[6];
            for (int i = 0; i < 6; i++)
            {
                var dice = new[] { _rng.Next(1, 7), _rng.Next(1, 7), _rng.Next(1, 7), _rng.Next(1, 7) };
                Array.Sort(dice);
                scores[i] = dice[1] + dice[2] + dice[3]; // drop lowest
            }

            Array.Sort(scores);
            Array.Reverse(scores); // descending
            _lastRolledScores = scores;

            return JsonSerializer.Serialize(new
            {
                scores,
                note = "6 scores rolled (4d6 drop lowest each), sorted high to low. Player assigns each to STR/DEX/CON/INT/WIS/CHA."
            });
        }

        // ── Deterministic character build from SRD data ─────────────────────

        private string BuildCharacter(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name       = input["name"].GetString() ?? "Unknown Hero";
            var species    = input["species"].GetString() ?? "Human";
            var className  = input["class_name"].GetString() ?? "Fighter";
            var background = input["background"].GetString() ?? "Soldier";
            var alignment  = input["alignment"].GetString() ?? "Neutral";
            var backstory  = input["backstory"].GetString() ?? "";
            var boost2Ability = (input["boost_plus2"].GetString() ?? "strength").ToLower();
            var boost1Ability = (input["boost_plus1"].GetString() ?? "constitution").ToLower();

            // Base scores from rolling (before background boosts)
            int str   = input["strength"].GetInt32();
            int dex   = input["dexterity"].GetInt32();
            int con   = input["constitution"].GetInt32();
            int intel = input["intelligence"].GetInt32();
            int wis   = input["wisdom"].GetInt32();
            int cha   = input["charisma"].GetInt32();

            // Apply background ability boosts (+2 / +1), cap at 20
            ApplyBoost(ref str, ref dex, ref con, ref intel, ref wis, ref cha, boost2Ability, 2);
            ApplyBoost(ref str, ref dex, ref con, ref intel, ref wis, ref cha, boost1Ability, 1);

            // Look up SRD data
            var classData   = ParseClassData(className);
            var speciesData = ParseSpeciesData(species);
            var bgData      = ParseBackgroundData(background);

            // Calculate derived stats
            int conMod = Mod(con);
            int dexMod = Mod(dex);
            int maxHp  = classData.HitDie + conMod;
            int ac     = CalculateStartingAC(className, dexMod);
            int speed  = speciesData.Speed;

            // Skills from background
            var skills = new List<string>(bgData.Skills);

            // Proficiencies from class + background tool
            var proficiencies = new List<string>();
            proficiencies.AddRange(classData.ArmorProf.Select(a => a + " armor"));
            proficiencies.AddRange(classData.WeaponProf.Select(w => w + " weapons"));
            proficiencies.Add($"Saves: {string.Join(", ", classData.Saves)}");
            if (!string.IsNullOrEmpty(bgData.Tool))
                proficiencies.Add(bgData.Tool);

            // Features: species traits + class level 1 features + background feat
            var features = new List<string>();
            features.AddRange(speciesData.Traits);
            features.AddRange(classData.Level1Features);
            features.Add($"Feat: {bgData.Feat}");

            // Starting equipment from class + background extras
            var items = GetClassStartingItems(className, dexMod);
            foreach (var eq in bgData.Equipment)
            {
                if (!items.Any(i => i.Name.Equals(eq, StringComparison.OrdinalIgnoreCase)))
                    items.Add(new Item { Name = eq, Type = "misc", Description = $"From {background} background" });
            }

            // Spells from class (level 1)
            var spells = GetClassStartingSpells(className);

            var character = new Character
            {
                Name         = name,
                Race         = species,
                Class        = className,
                Background   = background,
                Level        = 1,
                Alignment    = alignment,
                Backstory    = backstory,
                Abilities    = new AbilityScores
                {
                    Strength     = str,
                    Dexterity    = dex,
                    Constitution = con,
                    Intelligence = intel,
                    Wisdom       = wis,
                    Charisma     = cha
                },
                MaxHitPoints  = maxHp,
                HitPoints     = maxHp,
                ArmorClass    = ac,
                Speed         = speed,
                Skills        = skills,
                Proficiencies = proficiencies,
                Spells        = spells,
                Features      = features,
                Inventory     = new Inventory { Gold = bgData.StartingGold, Items = items }
            };

            return JsonSerializer.Serialize(character, new JsonSerializerOptions { WriteIndented = true });
        }

        // ── SRD data parsing helpers ────────────────────────────────────────

        private record ClassInfo(int HitDie, string[] Saves, string[] ArmorProf, string[] WeaponProf, string[] Level1Features);

        private static ClassInfo ParseClassData(string className)
        {
            var doc = JsonDocument.Parse(SrdRules.Classes);
            var key = className.ToLower();
            if (!doc.RootElement.TryGetProperty(key, out var cls))
                throw new ArgumentException($"Unknown class: {className}. Check SRD classes.");

            int hd = cls.GetProperty("hd").GetInt32();
            var saves = cls.GetProperty("saves").EnumerateArray().Select(s => s.GetString()!).ToArray();
            var armor = cls.TryGetProperty("armor", out var a)
                ? a.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();
            var weapons = cls.TryGetProperty("weapons", out var w)
                ? w.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();
            var features = cls.TryGetProperty("features", out var f) && f.TryGetProperty("1", out var f1)
                ? f1.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();

            return new ClassInfo(hd, saves, armor, weapons, features);
        }

        private record SpeciesInfo(int Speed, string[] Traits);

        private static SpeciesInfo ParseSpeciesData(string species)
        {
            var doc = JsonDocument.Parse(SrdRules.Species);
            JsonElement sp = default;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("_")) continue;
                if (prop.Name.Equals(species, StringComparison.OrdinalIgnoreCase))
                {
                    sp = prop.Value;
                    break;
                }
            }
            if (sp.ValueKind == JsonValueKind.Undefined)
                return new SpeciesInfo(30, new[] { "Unknown species" });

            int speed = sp.TryGetProperty("speed", out var spd) ? spd.GetInt32() : 30;
            var traits = sp.TryGetProperty("traits", out var t)
                ? t.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();

            return new SpeciesInfo(speed, traits);
        }

        private record BackgroundInfo(string[] Skills, string Tool, string Feat, string[] Equipment, int StartingGold);

        private static BackgroundInfo ParseBackgroundData(string background)
        {
            var doc = JsonDocument.Parse(SrdRules.Backgrounds);
            JsonElement bg = default;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("_")) continue;
                if (prop.Name.Equals(background, StringComparison.OrdinalIgnoreCase))
                {
                    bg = prop.Value;
                    break;
                }
            }
            if (bg.ValueKind == JsonValueKind.Undefined)
                return new BackgroundInfo(Array.Empty<string>(), "", "Skilled", Array.Empty<string>(), 10);

            var skills = bg.TryGetProperty("skills", out var sk)
                ? sk.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();
            var tool = bg.TryGetProperty("tool", out var tl) ? tl.GetString() ?? "" : "";
            var feat = bg.TryGetProperty("feat", out var ft) ? ft.GetString() ?? "" : "";
            var equipment = bg.TryGetProperty("equipment", out var eq)
                ? eq.EnumerateArray().Select(s => s.GetString()!).ToArray()
                : Array.Empty<string>();

            // Extract gold from last equipment entry if it ends with "gp"
            int gold = 10;
            if (equipment.Length > 0)
            {
                var last = equipment[^1];
                if (last.EndsWith("gp") && int.TryParse(last.Replace("gp", "").Trim(), out var g))
                {
                    gold = g;
                    equipment = equipment[..^1];
                }
            }

            return new BackgroundInfo(skills, tool, feat, equipment, gold);
        }

        // ── Deterministic stat helpers ──────────────────────────────────────

        private static int Mod(int score) => (int)Math.Floor((score - 10) / 2.0);

        private static void ApplyBoost(ref int str, ref int dex, ref int con,
            ref int intel, ref int wis, ref int cha, string ability, int amount)
        {
            switch (ability)
            {
                case "strength":     str   = Math.Min(20, str + amount);   break;
                case "dexterity":    dex   = Math.Min(20, dex + amount);   break;
                case "constitution": con   = Math.Min(20, con + amount);   break;
                case "intelligence": intel = Math.Min(20, intel + amount); break;
                case "wisdom":       wis   = Math.Min(20, wis + amount);   break;
                case "charisma":     cha   = Math.Min(20, cha + amount);   break;
            }
        }

        private static int CalculateStartingAC(string className, int dexMod) =>
            className.ToLower() switch
            {
                "fighter" or "paladin"  => 16,              // chain mail (no dex)
                "cleric"                => 16,              // scale mail + shield
                "barbarian"             => 10 + dexMod + 2, // unarmored defense (assume ~+2 CON)
                "ranger"                => 14 + Math.Min(dexMod, 2), // scale mail
                "rogue"                 => 11 + dexMod,     // leather
                "bard"                  => 11 + dexMod,     // leather
                "wizard"                => 10 + dexMod,     // no armor
                _                       => 10 + dexMod
            };

        private static List<Item> GetClassStartingItems(string className, int dexMod)
        {
            return className.ToLower() switch
            {
                "fighter" => new()
                {
                    new() { Name = "Chain Mail",     Type = "armor",  IsEquipped = true, Description = "AC 16, Stealth disadvantage" },
                    new() { Name = "Longsword",      Type = "weapon", IsEquipped = true, Description = "1d8 slashing, versatile (1d10)", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Shield",         Type = "armor",  IsEquipped = true, Description = "+2 AC" },
                    new() { Name = "Light Crossbow", Type = "weapon", Description = "1d8 piercing, range 80/320", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Bolts (20)",     Type = "ammo",   Description = "Crossbow ammunition" },
                    new() { Name = "Explorer's Pack", Type = "misc",  Description = "Backpack, bedroll, mess kit, torches, rations, waterskin, rope" },
                },
                "wizard" => new()
                {
                    new() { Name = "Quarterstaff",   Type = "weapon", IsEquipped = true, Description = "1d6 bludgeoning, versatile (1d8)", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Arcane Focus",   Type = "misc",   IsEquipped = true, Description = "Spellcasting focus" },
                    new() { Name = "Spellbook",      Type = "misc",   Description = "Contains prepared spells" },
                    new() { Name = "Scholar's Pack",  Type = "misc",  Description = "Backpack, book of lore, ink, pen, parchment" },
                },
                "rogue" => new()
                {
                    new() { Name = "Leather Armor",  Type = "armor",  IsEquipped = true, Description = $"AC {11 + dexMod}" },
                    new() { Name = "Rapier",         Type = "weapon", IsEquipped = true, Description = "1d8 piercing, finesse", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Shortbow",       Type = "weapon", Description = "1d6 piercing, range 80/320", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Arrows (20)",    Type = "ammo",   Description = "Bow ammunition" },
                    new() { Name = "Thieves' Tools", Type = "misc",   Description = "Pick locks, disarm traps" },
                    new() { Name = "Burglar's Pack",  Type = "misc",  Description = "Backpack, crowbar, lantern, oil, rations, rope" },
                },
                "cleric" => new()
                {
                    new() { Name = "Scale Mail",     Type = "armor",  IsEquipped = true, Description = "AC 14+DEX(max 2)" },
                    new() { Name = "Shield",         Type = "armor",  IsEquipped = true, Description = "+2 AC" },
                    new() { Name = "Mace",           Type = "weapon", IsEquipped = true, Description = "1d6 bludgeoning", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Holy Symbol",    Type = "misc",   IsEquipped = true, Description = "Spellcasting focus" },
                    new() { Name = "Priest's Pack",   Type = "misc",  Description = "Backpack, blanket, candles, tinderbox, rations, waterskin" },
                },
                "barbarian" => new()
                {
                    new() { Name = "Greataxe",       Type = "weapon", IsEquipped = true, Description = "1d12 slashing, heavy, two-handed", Stats = new() { ["damage"] = 12 } },
                    new() { Name = "Handaxe",        Type = "weapon", Description = "1d6 slashing, light, thrown (20/60)", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Handaxe",        Type = "weapon", Description = "1d6 slashing, light, thrown (20/60)", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Explorer's Pack", Type = "misc",  Description = "Backpack, bedroll, mess kit, torches, rations, waterskin, rope" },
                },
                "ranger" => new()
                {
                    new() { Name = "Scale Mail",     Type = "armor",  IsEquipped = true, Description = "AC 14+DEX(max 2)" },
                    new() { Name = "Shortsword",     Type = "weapon", IsEquipped = true, Description = "1d6 piercing, finesse, light", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Shortsword",     Type = "weapon", Description = "1d6 piercing, finesse, light", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Longbow",        Type = "weapon", Description = "1d8 piercing, range 150/600", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Arrows (20)",    Type = "ammo",   Description = "Bow ammunition" },
                    new() { Name = "Explorer's Pack", Type = "misc",  Description = "Backpack, bedroll, mess kit, torches, rations, waterskin, rope" },
                },
                "paladin" => new()
                {
                    new() { Name = "Chain Mail",     Type = "armor",  IsEquipped = true, Description = "AC 16, Stealth disadvantage" },
                    new() { Name = "Longsword",      Type = "weapon", IsEquipped = true, Description = "1d8 slashing, versatile (1d10)", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Shield",         Type = "armor",  IsEquipped = true, Description = "+2 AC" },
                    new() { Name = "Holy Symbol",    Type = "misc",   IsEquipped = true, Description = "Spellcasting focus" },
                    new() { Name = "Explorer's Pack", Type = "misc",  Description = "Backpack, bedroll, mess kit, torches, rations, waterskin, rope" },
                },
                "bard" => new()
                {
                    new() { Name = "Leather Armor",  Type = "armor",  IsEquipped = true, Description = $"AC {11 + dexMod}" },
                    new() { Name = "Rapier",         Type = "weapon", IsEquipped = true, Description = "1d8 piercing, finesse", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Dagger",         Type = "weapon", Description = "1d4 piercing, finesse, light, thrown", Stats = new() { ["damage"] = 4 } },
                    new() { Name = "Lute",           Type = "misc",   IsEquipped = true, Description = "Musical instrument, spellcasting focus" },
                    new() { Name = "Entertainer's Pack", Type = "misc", Description = "Backpack, bedroll, costumes, candles, rations, waterskin" },
                },
                _ => new()
                {
                    new() { Name = "Quarterstaff",   Type = "weapon", IsEquipped = true, Description = "1d6 bludgeoning", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Explorer's Pack", Type = "misc",  Description = "Standard adventuring gear" },
                }
            };
        }

        private static List<string> GetClassStartingSpells(string className) =>
            className.ToLower() switch
            {
                "wizard"  => new() { "Mage Hand", "Fire Bolt", "Light", "Magic Missile", "Shield", "Sleep", "Detect Magic", "Mage Armor" },
                "cleric"  => new() { "Sacred Flame", "Guidance", "Spare the Dying", "Cure Wounds", "Bless", "Guiding Bolt", "Shield of Faith" },
                "bard"    => new() { "Vicious Mockery", "Light", "Healing Word", "Thunderwave", "Charm Person", "Faerie Fire" },
                "ranger"  => new() { "Hunter's Mark", "Cure Wounds" },
                "paladin" => new(), // no spells at level 1
                _         => new()
            };
    }
}
