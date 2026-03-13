using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Generates contextually appropriate encounters for the current dungeon
    /// room or overworld location. Scales difficulty to the character's level
    /// and returns a fully populated Encounter as JSON.
    /// </summary>
    public class EncounterBuilder : DndAgent
    {
        private readonly Random _rng = new();

        public EncounterBuilder(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "EncounterBuilder";

        protected override string SystemPrompt => """
            You are EncounterBuilder, a creative D&D dungeon master who designs compelling encounters.
            You craft encounters that fit the story, location, and character level.

            Types of encounters you create:
            - Combat: monsters scaled to the party level using CR guidelines.
            - Social: NPCs with goals, secrets, and trade opportunities.
            - Exploration: traps, puzzles, environmental hazards.
            - Discovery: treasure rooms, ancient lore, magical anomalies.

            When building a combat encounter, call build_monster for each enemy,
            then call build_encounter to assemble everything.

            Loot guidelines:
            - Common items (potions, coins) for low-CR fights.
            - Uncommon magic items possible from CR 3+.
            - Rare items possible from CR 6+, boss fights, or story rewards.

            Return the finished Encounter as JSON inside <encounter_json> tags.
            """;

        protected override List<ChatTool> Tools => new()
        {
            MakeTool(
                "build_monster",
                "Create a single monster stat block appropriate for the given challenge rating.",
                new Dictionary<string, object>
                {
                    ["name"]             = StringProp("Monster name"),
                    ["challenge_rating"] = IntProp("Challenge rating (0-20)"),
                    ["description"]      = StringProp("Flavour description"),
                    ["special_ability"]  = StringProp("One special ability or attack (optional)")
                },
                new() { "name", "challenge_rating", "description" }),

            MakeTool(
                "build_encounter",
                "Assemble a complete Encounter from monsters and rewards.",
                new Dictionary<string, object>
                {
                    ["name"]               = StringProp("Encounter name"),
                    ["description"]        = StringProp("Scene-setting description"),
                    ["encounter_type"]     = StringProp("combat | social | exploration | discovery"),
                    ["monster_names"]      = ArrayProp("Names of monsters to include (must match previously built monsters)", "string"),
                    ["xp_reward"]          = IntProp("Total XP awarded on completion"),
                    ["gold_reward"]        = IntProp("Gold pieces dropped"),
                    ["special_conditions"] = StringProp("Any special rules for this encounter (optional)")
                },
                new() { "name", "description", "encounter_type", "xp_reward", "gold_reward" }),

            MakeTool(
                "roll_loot",
                "Roll on the loot table for the given rarity tier.",
                new Dictionary<string, object>
                {
                    ["rarity"] = StringProp("common | uncommon | rare | very_rare")
                },
                new() { "rarity" })
        };

        // Monsters staged during a single encounter-building session
        private readonly Dictionary<string, Monster> _stagedMonsters = new();

        public Encounter? LastEncounter { get; private set; }

        protected override Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            return toolName switch
            {
                "build_monster"   => Task.FromResult(BuildMonster(input)),
                "build_encounter" => Task.FromResult(BuildEncounter(input)),
                "roll_loot"       => Task.FromResult(RollLoot(input)),
                _                 => base.HandleToolCallAsync(toolName, input)
            };
        }

        private string BuildMonster(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name = input["name"].GetString() ?? "Unknown";
            var cr   = input["challenge_rating"].GetInt32();
            var desc = input["description"].GetString() ?? "";
            var special = input.TryGetValue("special_ability", out var sa) ? sa.GetString() ?? "" : "";

            // Stat scaling by CR
            int hp  = Math.Max(4,  (cr + 1) * 9 + _rng.Next(-3, 4));
            int ac  = Math.Max(10, 11 + cr / 3);
            int atk = cr + 2;
            int dmg = Math.Max(4, cr * 2 + _rng.Next(1, 5));
            int dmgDie = cr < 3 ? 6 : cr < 6 ? 8 : cr < 10 ? 10 : 12;

            var actions = new List<MonsterAction>
            {
                new()
                {
                    Name = "Attack",
                    AttackBonus = $"+{atk}",
                    DamageDice  = $"1d{dmgDie}",
                    DamageBonus = cr / 2,
                    DamageType  = PickDamageType(name),
                    Description = $"Melee weapon attack: +{atk} to hit, 1d{dmgDie}+{cr/2} damage."
                }
            };

            if (!string.IsNullOrWhiteSpace(special))
            {
                actions.Add(new MonsterAction
                {
                    Name        = "Special",
                    Description = special,
                    DamageDice  = $"2d{dmgDie}",
                    DamageBonus = cr / 3
                });
            }

            var monster = new Monster
            {
                Name           = name,
                HitPoints      = hp,
                MaxHitPoints   = hp,
                ArmorClass     = ac,
                ChallengeRating = cr,
                Description    = desc,
                Abilities      = new AbilityScores
                {
                    Strength     = Math.Min(20, 10 + cr),
                    Dexterity    = 10 + (cr / 4),
                    Constitution = 10 + (cr / 3),
                    Intelligence = Math.Max(4, 8 - cr / 4),
                    Wisdom       = 10,
                    Charisma     = 8
                },
                Actions = actions
            };

            _stagedMonsters[name] = monster;
            return JsonSerializer.Serialize(monster, new JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildEncounter(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name     = input["name"].GetString() ?? "An Encounter";
            var desc     = input["description"].GetString() ?? "";
            var type     = input.TryGetValue("encounter_type", out var t) ? t.GetString() ?? "combat" : "combat";
            var xp       = input["xp_reward"].GetInt32();
            var gold     = input["gold_reward"].GetInt32();
            var special  = input.TryGetValue("special_conditions", out var sc) ? sc.GetString() ?? "" : "";

            var monsters = new List<Monster>();
            if (input.TryGetValue("monster_names", out var namesEl) &&
                namesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in namesEl.EnumerateArray())
                {
                    var mName = n.GetString() ?? "";
                    if (_stagedMonsters.TryGetValue(mName, out var m))
                        monsters.Add(m);
                }
            }

            var encounter = new Encounter
            {
                Name              = name,
                Description       = desc,
                Type              = type,
                Monsters          = monsters,
                ExperienceReward  = xp,
                GoldReward        = gold,
                SpecialConditions = special,
                LootTable         = GenerateLootTable(xp)
            };

            LastEncounter = encounter;
            _stagedMonsters.Clear();

            return JsonSerializer.Serialize(encounter, new JsonSerializerOptions { WriteIndented = true });
        }

        private string RollLoot(IReadOnlyDictionary<string, JsonElement> input)
        {
            var rarity = input["rarity"].GetString() ?? "common";
            var item = rarity switch
            {
                "uncommon"  => UncommonItems[_rng.Next(UncommonItems.Length)],
                "rare"      => RareItems[_rng.Next(RareItems.Length)],
                "very_rare" => VeryRareItems[_rng.Next(VeryRareItems.Length)],
                _           => CommonItems[_rng.Next(CommonItems.Length)]
            };

            return JsonSerializer.Serialize(new { rarity, item });
        }

        private List<Item> GenerateLootTable(int xp)
        {
            var loot = new List<Item>();
            if (xp < 100) return loot;

            if (_rng.Next(100) < 60)
                loot.Add(new Item { Name = "Health Potion", Type = "potion", Description = "Restores 2d4+2 HP" });

            if (xp >= 300 && _rng.Next(100) < 30)
                loot.Add(new Item { Name = UncommonItems[_rng.Next(UncommonItems.Length)], Type = "misc", Description = "An uncommon magical trinket" });

            return loot;
        }

        private static string PickDamageType(string name)
        {
            var n = name.ToLower();
            if (n.Contains("fire") || n.Contains("dragon")) return "fire";
            if (n.Contains("ice") || n.Contains("frost"))   return "cold";
            if (n.Contains("shadow") || n.Contains("ghost")) return "necrotic";
            if (n.Contains("wolf") || n.Contains("bear"))    return "piercing";
            return "bludgeoning";
        }

        private static readonly string[] CommonItems =
        {
            "Potion of Healing", "Torch (3)", "Rations (2 days)", "50ft Hemp Rope",
            "Tinderbox", "Candles (10)", "Piton (5)", "Antitoxin"
        };

        private static readonly string[] UncommonItems =
        {
            "Cloak of Protection (+1 AC)", "Bag of Holding", "Boots of Elvenkind",
            "Gloves of Thievery", "Wand of Magic Missiles (7 charges)",
            "+1 Shortsword", "+1 Shield", "Sending Stones (pair)"
        };

        private static readonly string[] RareItems =
        {
            "+2 Longsword", "Staff of Fire (10 charges)", "Ring of Protection",
            "Winged Boots", "Necklace of Fireballs", "Bracers of Defense",
            "Pearl of Power", "Headband of Intellect"
        };

        private static readonly string[] VeryRareItems =
        {
            "+3 Greatsword", "Amulet of Health", "Cloak of Displacement",
            "Ring of Regeneration", "Manual of Gainful Exercise",
            "Tome of Understanding", "Robe of the Archmagi"
        };
    }
}
