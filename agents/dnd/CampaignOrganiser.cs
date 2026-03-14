using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Manages the overall campaign state: world map, quests, NPCs, loot,
    /// and progression. Acts as the "Dungeon Master memory" layer.
    /// </summary>
    public class CampaignOrganiser : DndAgent
    {
        public CampaignOrganiser(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "CampaignOrganiser";

        protected override string SystemPrompt =>
            """
            You are CampaignOrganiser, the meticulous keeper of the campaign's lore and state.
            You track quests, NPC relationships, the world map, loot, and long-term story arcs.

            Responsibilities:
            - Maintain a living, consistent world that reacts to the player's choices.
            - Award experience and gold after encounters; level up the character when thresholds are met.
            - Record campaign events in the log for continuity.
            - Suggest the next dungeon, quest, or story beat when the player asks "where to next?"
            - Keep the difficulty curve appropriate for the character's level.

            Always return updated CampaignState and Character JSON inside
            <campaign_json> and <character_json> tags respectively so the
            orchestrator can parse them.

            ## SRD Reference — Progression & XP
            """ + SrdRules.Progression + """

            ## SRD Reference — Spells
            """ + SrdRules.Spells;

        protected override List<ChatTool> Tools => new()
        {
            MakeTool(
                "log_event",
                "Append a significant story event to the campaign log.",
                new Dictionary<string, object>
                {
                    ["event"] = StringProp("Description of the event to record")
                },
                new() { "event" }),

            MakeTool(
                "award_experience",
                "Award XP and gold to the character after an encounter.",
                new Dictionary<string, object>
                {
                    ["xp"]   = IntProp("Experience points to award"),
                    ["gold"]  = IntProp("Gold pieces to award")
                },
                new() { "xp", "gold" }),

            MakeTool(
                "add_quest",
                "Add a new active quest to the campaign.",
                new Dictionary<string, object>
                {
                    ["quest_name"] = StringProp("Name of the quest"),
                    ["description"] = StringProp("Brief quest description")
                },
                new() { "quest_name", "description" }),

            MakeTool(
                "complete_quest",
                "Mark a quest as completed.",
                new Dictionary<string, object>
                {
                    ["quest_name"] = StringProp("Name of the completed quest")
                },
                new() { "quest_name" }),

            MakeTool(
                "travel_to",
                "Move the player to a new location in the world.",
                new Dictionary<string, object>
                {
                    ["location_name"]        = StringProp("Name of the destination"),
                    ["location_description"] = StringProp("Evocative description of the new location")
                },
                new() { "location_name", "location_description" }),

            MakeTool(
                "add_item_to_inventory",
                "Add a looted or purchased item to the character's inventory.",
                new Dictionary<string, object>
                {
                    ["item_name"]        = StringProp("Item name"),
                    ["item_type"]        = StringProp("Item type: weapon, armor, potion, misc"),
                    ["item_description"] = StringProp("Item description"),
                    ["quantity"]         = IntProp("Number of items")
                },
                new() { "item_name", "item_type", "item_description" })
        };

        // State managed by this agent (shared with orchestrator via JSON round-trip)
        private CampaignState _campaign = new();
        private Character? _character;

        public void InitialiseState(CampaignState campaign, Character character)
        {
            _campaign = campaign;
            _character = character;
        }

        public CampaignState GetCampaignState() => _campaign;
        public Character? GetCharacter() => _character;

        protected override Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            return toolName switch
            {
                "log_event"           => Task.FromResult(LogEvent(input)),
                "award_experience"    => Task.FromResult(AwardExperience(input)),
                "add_quest"           => Task.FromResult(AddQuest(input)),
                "complete_quest"      => Task.FromResult(CompleteQuest(input)),
                "travel_to"           => Task.FromResult(TravelTo(input)),
                "add_item_to_inventory" => Task.FromResult(AddItem(input)),
                _                     => base.HandleToolCallAsync(toolName, input)
            };
        }

        private string LogEvent(IReadOnlyDictionary<string, JsonElement> input)
        {
            var ev = input["event"].GetString() ?? "";
            _campaign.CampaignLog.Add($"[Session {_campaign.SessionNumber}] {ev}");
            return JsonSerializer.Serialize(new { logged = ev });
        }

        private string AwardExperience(IReadOnlyDictionary<string, JsonElement> input)
        {
            if (_character == null)
                return "{\"error\": \"No character loaded\"}";

            int xp   = input["xp"].GetInt32();
            int gold = input["gold"].GetInt32();

            _character.ExperiencePoints += xp;
            _character.Inventory.Gold   += gold;

            int oldLevel = _character.Level;
            _character.Level = XpToLevel(_character.ExperiencePoints);

            bool leveledUp = _character.Level > oldLevel;
            if (leveledUp)
                _campaign.CampaignLog.Add(
                    $"[Session {_campaign.SessionNumber}] {_character.Name} reached Level {_character.Level}!");

            return JsonSerializer.Serialize(new
            {
                xp_awarded  = xp,
                gold_awarded = gold,
                total_xp    = _character.ExperiencePoints,
                total_gold  = _character.Inventory.Gold,
                new_level   = _character.Level,
                leveled_up  = leveledUp
            });
        }

        private string AddQuest(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name = input["quest_name"].GetString() ?? "";
            var desc = input["description"].GetString() ?? "";
            var entry = $"{name}: {desc}";
            if (!_campaign.ActiveQuests.Contains(entry))
                _campaign.ActiveQuests.Add(entry);
            return JsonSerializer.Serialize(new { added_quest = name });
        }

        private string CompleteQuest(IReadOnlyDictionary<string, JsonElement> input)
        {
            var name = input["quest_name"].GetString() ?? "";
            var toRemove = _campaign.ActiveQuests.Find(q => q.StartsWith(name));
            if (toRemove != null)
            {
                _campaign.ActiveQuests.Remove(toRemove);
                _campaign.CompletedQuests.Add(toRemove);
            }
            return JsonSerializer.Serialize(new { completed_quest = name });
        }

        private string TravelTo(IReadOnlyDictionary<string, JsonElement> input)
        {
            var loc  = input["location_name"].GetString() ?? "";
            var desc = input["location_description"].GetString() ?? "";

            if (!_campaign.VisitedLocations.Contains(loc))
                _campaign.VisitedLocations.Add(loc);

            _campaign.CurrentLocation            = loc;
            _campaign.CurrentLocationDescription = desc;

            return JsonSerializer.Serialize(new { traveled_to = loc });
        }

        private string AddItem(IReadOnlyDictionary<string, JsonElement> input)
        {
            if (_character == null)
                return "{\"error\": \"No character loaded\"}";

            var item = new Item
            {
                Name        = input["item_name"].GetString() ?? "",
                Type        = input["item_type"].GetString() ?? "misc",
                Description = input["item_description"].GetString() ?? "",
                Quantity    = input.TryGetValue("quantity", out var q) ? q.GetInt32() : 1
            };

            _character.Inventory.Items.Add(item);
            return JsonSerializer.Serialize(new { added_item = item.Name });
        }

        private static int XpToLevel(int xp) => xp switch
        {
            < 300   => 1,
            < 900   => 2,
            < 2700  => 3,
            < 6500  => 4,
            < 14000 => 5,
            < 23000 => 6,
            < 34000 => 7,
            < 48000 => 8,
            < 64000 => 9,
            _       => 10
        };
    }
}
