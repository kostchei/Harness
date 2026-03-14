using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Guides the player through creating a D&D 5e character.
    /// Uses tools to roll ability scores, assign race/class bonuses, and
    /// return a fully populated Character object as JSON.
    /// </summary>
    public class CharacterCreator : DndAgent
    {
        private readonly Random _rng = new();

        public CharacterCreator(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "CharacterCreator";

        protected override string SystemPrompt =>
            """
            You are CharacterCreator, an expert Dungeons & Dragons 5th Edition character creation guide.
            Your job is to help the player create a memorable, mechanically sound character.

            Workflow:
            1. Ask the player for their preferred race, class, and backstory concept (or offer to randomise).
            2. Call roll_ability_scores to generate the six ability scores.
            3. Call build_character with the player's choices and the rolled scores to produce the final Character JSON.
            4. Narrate the finished character in an evocative, flavourful paragraph.

            Rules to apply:
            - Use standard D&D 5e races, classes, and starting equipment.
            - Apply racial ability-score bonuses automatically.
            - Set starting HP = max hit die + CON modifier.
            - Include 2 background skills and starting equipment appropriate to the class.

            Always respond in-character as a wise sage welcoming the hero to the world.
            Return the final Character data inside <character_json> tags so the orchestrator can parse it.

            ## SRD Reference — Classes
            """ + SrdRules.Classes + """

            ## SRD Reference — Races
            """ + SrdRules.Races;

        protected override List<ChatTool> Tools => new()
        {
            MakeTool(
                "roll_ability_scores",
                "Roll four d6s and drop the lowest for each of the six ability scores.",
                new Dictionary<string, object>()),

            MakeTool(
                "build_character",
                "Assemble a complete Character record from the provided choices and rolls.",
                new Dictionary<string, object>
                {
                    ["name"]       = StringProp("Character name"),
                    ["race"]       = StringProp("Character race (e.g. Human, Elf, Dwarf)"),
                    ["class_name"] = StringProp("Character class (e.g. Fighter, Wizard, Rogue)"),
                    ["alignment"]  = StringProp("Alignment (e.g. Chaotic Good)"),
                    ["backstory"]  = StringProp("Short backstory paragraph"),
                    ["strength"]     = IntProp("Strength score after racial bonus"),
                    ["dexterity"]    = IntProp("Dexterity score after racial bonus"),
                    ["constitution"] = IntProp("Constitution score after racial bonus"),
                    ["intelligence"] = IntProp("Intelligence score after racial bonus"),
                    ["wisdom"]       = IntProp("Wisdom score after racial bonus"),
                    ["charisma"]     = IntProp("Charisma score after racial bonus"),
                },
                new List<string> { "name", "race", "class_name", "alignment", "backstory",
                    "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" })
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

        // Roll 4d6 drop lowest for each score
        private string RollAbilityScores()
        {
            var scores = new int[6];
            for (int i = 0; i < 6; i++)
            {
                var rolls = new[] { Roll(6), Roll(6), Roll(6), Roll(6) };
                Array.Sort(rolls);
                scores[i] = rolls[1] + rolls[2] + rolls[3]; // drop lowest
            }

            return JsonSerializer.Serialize(new
            {
                strength     = scores[0],
                dexterity    = scores[1],
                constitution = scores[2],
                intelligence = scores[3],
                wisdom       = scores[4],
                charisma     = scores[5],
                note = "Rolled 4d6 drop lowest for each score"
            });
        }

        private string BuildCharacter(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name      = input["name"].GetString() ?? "Unknown Hero";
            var race      = input["race"].GetString() ?? "Human";
            var className = input["class_name"].GetString() ?? "Fighter";
            var alignment = input["alignment"].GetString() ?? "Neutral";
            var backstory = input["backstory"].GetString() ?? "";

            int str = input["strength"].GetInt32();
            int dex = input["dexterity"].GetInt32();
            int con = input["constitution"].GetInt32();
            int intel = input["intelligence"].GetInt32();
            int wis = input["wisdom"].GetInt32();
            int cha = input["charisma"].GetInt32();

            var (hitDie, skills, proficiencies, spells, features, startingAC) =
                GetClassDefaults(className, dex);

            int conMod = (int)Math.Floor((con - 10) / 2.0);
            int maxHp = hitDie + conMod;

            var character = new Character
            {
                Name = name,
                Race = race,
                Class = className,
                Level = 1,
                Alignment = alignment,
                Backstory = backstory,
                Abilities = new AbilityScores
                {
                    Strength     = str,
                    Dexterity    = dex,
                    Constitution = con,
                    Intelligence = intel,
                    Wisdom       = wis,
                    Charisma     = cha
                },
                MaxHitPoints = maxHp,
                HitPoints    = maxHp,
                ArmorClass   = startingAC,
                Speed        = GetRaceSpeed(race),
                Skills       = skills,
                Proficiencies = proficiencies,
                Spells       = spells,
                Features     = features,
                Inventory    = GetStartingInventory(className)
            };

            return JsonSerializer.Serialize(character, new JsonSerializerOptions { WriteIndented = true });
        }

        private static (int hitDie, List<string> skills, List<string> proficiencies,
            List<string> spells, List<string> features, int ac)
            GetClassDefaults(string className, int dex)
        {
            int dexMod = (int)Math.Floor((dex - 10) / 2.0);
            return className.ToLower() switch
            {
                "fighter" => (10,
                    new() { "Athletics", "Intimidation" },
                    new() { "All armor", "Shields", "Simple weapons", "Martial weapons" },
                    new(),
                    new() { "Second Wind", "Fighting Style" },
                    16),
                "wizard" => (6,
                    new() { "Arcana", "Investigation" },
                    new() { "Daggers", "Quarterstaffs", "Light crossbows" },
                    new() { "Mage Hand", "Fire Bolt", "Magic Missile", "Shield", "Sleep" },
                    new() { "Arcane Recovery", "Spellcasting" },
                    13 + dexMod),
                "rogue" => (8,
                    new() { "Stealth", "Thieves' Tools", "Perception", "Deception" },
                    new() { "Light armor", "Simple weapons", "Hand crossbows", "Longswords", "Rapiers", "Shortswords" },
                    new(),
                    new() { "Sneak Attack 1d6", "Thieves' Cant", "Expertise" },
                    11 + dexMod + 2),
                "cleric" => (8,
                    new() { "Medicine", "Religion" },
                    new() { "Light armor", "Medium armor", "Shields", "Simple weapons" },
                    new() { "Sacred Flame", "Guidance", "Cure Wounds", "Bless", "Guiding Bolt" },
                    new() { "Spellcasting", "Divine Domain", "Channel Divinity" },
                    16),
                "ranger" => (10,
                    new() { "Animal Handling", "Survival", "Perception" },
                    new() { "Light armor", "Medium armor", "Shields", "Simple weapons", "Martial weapons" },
                    new() { "Hunter's Mark", "Cure Wounds" },
                    new() { "Favored Enemy", "Natural Explorer", "Spellcasting" },
                    14 + dexMod),
                "paladin" => (10,
                    new() { "Athletics", "Persuasion" },
                    new() { "All armor", "Shields", "Simple weapons", "Martial weapons" },
                    new() { "Divine Smite", "Bless", "Cure Wounds" },
                    new() { "Divine Sense", "Lay on Hands", "Fighting Style", "Spellcasting" },
                    18),
                "barbarian" => (12,
                    new() { "Athletics", "Survival" },
                    new() { "Light armor", "Medium armor", "Shields", "Simple weapons", "Martial weapons" },
                    new(),
                    new() { "Rage", "Unarmored Defense" },
                    10 + dexMod + 3),
                "bard" => (8,
                    new() { "Performance", "Persuasion", "Deception" },
                    new() { "Light armor", "Simple weapons", "Hand crossbows", "Longswords", "Rapiers", "Shortswords" },
                    new() { "Vicious Mockery", "Healing Word", "Thunderwave", "Charm Person" },
                    new() { "Spellcasting", "Bardic Inspiration d6", "Jack of All Trades" },
                    13 + dexMod),
                _ => (8,
                    new() { "Perception", "Survival" },
                    new() { "Simple weapons" },
                    new(),
                    new() { "Adaptable" },
                    10 + dexMod)
            };
        }

        private static int GetRaceSpeed(string race) =>
            race.ToLower() switch
            {
                "dwarf" or "halfling" or "gnome" => 25,
                _ => 30
            };

        private static Inventory GetStartingInventory(string className)
        {
            var items = className.ToLower() switch
            {
                "fighter" => new List<Item>
                {
                    new() { Name = "Chain Mail",    Type = "armor",  IsEquipped = true,  Description = "AC 16, no Stealth" },
                    new() { Name = "Longsword",     Type = "weapon", IsEquipped = true,  Description = "1d8 slashing", Stats = new() { ["damage"] = 8 } },
                    new() { Name = "Shield",        Type = "armor",  IsEquipped = true,  Description = "+2 AC" },
                    new() { Name = "Explorer's Pack", Type = "misc", Description = "Backpack, bedroll, rations, torches, rope" }
                },
                "wizard" => new List<Item>
                {
                    new() { Name = "Arcane Focus",  Type = "misc",   IsEquipped = true,  Description = "Spellcasting focus" },
                    new() { Name = "Dagger",        Type = "weapon", IsEquipped = true,  Description = "1d4 piercing", Stats = new() { ["damage"] = 4 } },
                    new() { Name = "Spellbook",     Type = "misc",   IsEquipped = false, Description = "Contains known spells" },
                    new() { Name = "Scholar's Pack", Type = "misc",  Description = "Books, ink, parchment, candles" }
                },
                "rogue" => new List<Item>
                {
                    new() { Name = "Leather Armor", Type = "armor",  IsEquipped = true,  Description = "AC 11+Dex" },
                    new() { Name = "Shortsword",    Type = "weapon", IsEquipped = true,  Description = "1d6 piercing", Stats = new() { ["damage"] = 6 } },
                    new() { Name = "Thieves' Tools", Type = "misc",  IsEquipped = false, Description = "Pick locks, disarm traps" },
                    new() { Name = "Burglar's Pack", Type = "misc",  Description = "Rope, crowbar, lantern, rations" }
                },
                _ => new List<Item>
                {
                    new() { Name = "Backpack",      Type = "misc",   Description = "Standard adventuring gear" },
                    new() { Name = "Hand Axe",      Type = "weapon", IsEquipped = true, Description = "1d6 slashing", Stats = new() { ["damage"] = 6 } },
                }
            };

            return new Inventory { Gold = 10 + Roll(6, 2), Items = items };
        }

        private static int Roll(int sides, int count = 1)
        {
            var rng = new Random();
            int total = 0;
            for (int i = 0; i < count; i++) total += rng.Next(1, sides + 1);
            return total;
        }
    }
}
