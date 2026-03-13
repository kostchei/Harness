using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropic.Models.Messages;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// The storytelling layer. NarrativeVoice crafts immersive prose descriptions
    /// of locations, events, characters, and actions — bridging dry game-state
    /// updates into rich adventure fiction.
    /// </summary>
    public class NarrativeVoice : DndAgent
    {
        public NarrativeVoice(string apiKey) : base(apiKey) { }

        protected override string AgentName => "NarrativeVoice";

        protected override string SystemPrompt => """
            You are NarrativeVoice, the bardic soul of this D&D campaign. Your words transform
            mechanical game events into immersive, dramatic storytelling.

            Your voice is:
            - Evocative and atmospheric — paint vivid pictures with sensory detail.
            - Consistent in tone with the campaign's world flavour (gritty, epic, comedic, horror, etc.).
            - Second-person present tense: "You step into the torchlit chamber…"
            - Appropriately paced: shorter punchy sentences in combat, longer prose during exploration.

            You have access to these narration tools:
            - narrate_location: describe a new location the player enters.
            - narrate_combat_start: open a combat encounter dramatically.
            - narrate_combat_hit / narrate_combat_miss: single-line combat flavour.
            - narrate_loot: describe discovering treasure.
            - narrate_level_up: celebrate leveling up.
            - narrate_death_save: describe a near-death moment.
            - narrate_quest: introduce or resolve a quest dramatically.

            Always use exactly one narration tool per response, then provide
            your prose output as plain text (no JSON tags needed — NarrativeVoice
            output is always pure prose for the player to read).
            """;

        protected override List<ToolUnion> Tools => new()
        {
            MakeTool("narrate_location",
                "Generate an atmospheric description of a location.",
                new Dictionary<string, object>
                {
                    ["location_name"] = StringProp("Name of the location"),
                    ["location_type"] = StringProp("dungeon_room | tavern | forest | city | cave | temple | ruins | overworld"),
                    ["atmosphere"]    = StringProp("foreboding | cheerful | mysterious | tense | wondrous | eerie"),
                    ["details"]       = StringProp("Key visual or sensory details to include")
                },
                new() { "location_name", "location_type", "atmosphere" }),

            MakeTool("narrate_combat_start",
                "Open a combat encounter with dramatic flair.",
                new Dictionary<string, object>
                {
                    ["encounter_name"] = StringProp("Encounter or enemy name"),
                    ["enemy_count"]    = IntProp("Number of enemies"),
                    ["setting"]        = StringProp("Where the fight takes place")
                },
                new() { "encounter_name", "enemy_count", "setting" }),

            MakeTool("narrate_combat_hit",
                "Describe a successful hit in combat.",
                new Dictionary<string, object>
                {
                    ["attacker"]    = StringProp("Who is attacking"),
                    ["target"]      = StringProp("Who is being hit"),
                    ["damage"]      = IntProp("Damage dealt"),
                    ["damage_type"] = StringProp("Type of damage"),
                    ["is_crit"]     = BoolProp("Whether this was a critical hit")
                },
                new() { "attacker", "target", "damage", "damage_type" }),

            MakeTool("narrate_combat_miss",
                "Describe a missed attack.",
                new Dictionary<string, object>
                {
                    ["attacker"] = StringProp("Who attacked"),
                    ["target"]   = StringProp("Who was missed")
                },
                new() { "attacker", "target" }),

            MakeTool("narrate_loot",
                "Describe discovering treasure or valuable items.",
                new Dictionary<string, object>
                {
                    ["items"]    = ArrayProp("Names of items found", "string"),
                    ["gold"]     = IntProp("Gold pieces found"),
                    ["location"] = StringProp("Where the loot was found")
                },
                new() { "items", "gold" }),

            MakeTool("narrate_level_up",
                "Celebrate the character reaching a new level.",
                new Dictionary<string, object>
                {
                    ["character_name"] = StringProp("Character's name"),
                    ["new_level"]      = IntProp("The new level reached"),
                    ["class_name"]     = StringProp("Character's class")
                },
                new() { "character_name", "new_level", "class_name" }),

            MakeTool("narrate_death_save",
                "Narrate a near-death or death saving throw moment.",
                new Dictionary<string, object>
                {
                    ["character_name"] = StringProp("Character's name"),
                    ["survived"]       = BoolProp("Whether the character survived")
                },
                new() { "character_name", "survived" }),

            MakeTool("narrate_quest",
                "Dramatically introduce or resolve a quest.",
                new Dictionary<string, object>
                {
                    ["quest_name"]  = StringProp("Quest name"),
                    ["is_complete"] = BoolProp("True if completing, false if starting"),
                    ["details"]     = StringProp("Key story details to weave in")
                },
                new() { "quest_name", "is_complete", "details" })
        };

        // NarrativeVoice tools are purely for Claude to structure its thinking —
        // we don't need to store state from them; just return acknowledgements.
        protected override Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            // Echo back a simple ack so Claude continues to produce the prose.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                tool    = toolName,
                status  = "acknowledged",
                context = JsonSerializer.Serialize(input)
            }));
        }

        // ─── Convenience helpers for the orchestrator ─────────────────────────

        public Task<string> DescribeLocation(string name, string type, string atmosphere, string details = "")
            => ChatAsync($"Narrate this location for the player. " +
                         $"Name: {name}. Type: {type}. Atmosphere: {atmosphere}. " +
                         $"Details: {details}");

        public Task<string> DescribeCombatStart(string encounterName, int enemyCount, string setting)
            => ChatAsync($"Open this combat encounter dramatically. " +
                         $"Encounter: {encounterName}. Enemies: {enemyCount}. Setting: {setting}");

        public Task<string> DescribeHit(string attacker, string target, int damage, string damageType, bool isCrit)
            => ChatAsync($"Narrate this combat hit. " +
                         $"{attacker} hits {target} for {damage} {damageType} damage. Crit: {isCrit}");

        public Task<string> DescribeMiss(string attacker, string target)
            => ChatAsync($"Narrate this missed attack: {attacker} misses {target}.");

        public Task<string> DescribeLoot(List<string> items, int gold, string location)
            => ChatAsync($"Narrate finding loot in {location}: " +
                         $"items=[{string.Join(", ", items)}], gold={gold}gp");

        public Task<string> DescribeLevelUp(string characterName, int newLevel, string className)
            => ChatAsync($"Celebrate {characterName} the {className} reaching level {newLevel}!");

        public Task<string> DescribeQuestUpdate(string questName, bool isComplete, string details)
            => ChatAsync($"{(isComplete ? "Complete" : "Begin")} the quest '{questName}'. Details: {details}");
    }
}
