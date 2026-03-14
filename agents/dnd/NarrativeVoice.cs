using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// The storytelling layer. NarrativeVoice crafts immersive prose descriptions
    /// of locations, events, characters, and actions — bridging dry game-state
    /// updates into rich adventure fiction.
    /// </summary>
    public class NarrativeVoice : DndAgent
    {
        public NarrativeVoice(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "NarrativeVoice";

        protected override string SystemPrompt => """
            You are NarrativeVoice, the bardic soul of this D&D campaign. Your words transform
            mechanical game events into immersive, dramatic storytelling.

            Your voice is:
            - Evocative and atmospheric — paint vivid pictures with sensory detail.
            - Consistent in tone with the campaign's world flavour (gritty, epic, comedic, horror, etc.).
            - Second-person present tense: "You step into the torchlit chamber…"
            - Appropriately paced: shorter punchy sentences in combat, longer prose during exploration.

            You receive context about what to narrate (location, combat, loot, etc.) in the user message.
            Respond with pure prose for the player to read. No JSON, no tags.
            Keep responses to 2-4 sentences unless the moment demands more.
            """;

        // NarrativeVoice has no tools — it produces pure prose from the user prompt.
        // Context (location, combat details, etc.) is passed in the user message
        // by the orchestrator's convenience methods below.

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
