using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Top-level coordinator that owns all five specialist agents and drives
    /// the D&D campaign loop:
    ///   Character creation → Explore → Encounter → Combat → Loot → Level up → repeat
    ///
    /// Usage:
    ///   var orchestrator = new CampaignOrchestrator(apiKey);
    ///   await orchestrator.StartNewCampaignAsync("Shadows of the Forgotten Keep");
    ///   string response = await orchestrator.PlayerInputAsync("I search the room for traps");
    /// </summary>
    public class CampaignOrchestrator
    {
        private readonly CharacterCreator   _characterCreator;
        private readonly CampaignOrganiser  _campaignOrganiser;
        private readonly EncounterBuilder   _encounterBuilder;
        private readonly CombatRunner       _combatRunner;
        private readonly NarrativeVoice     _narrativeVoice;

        private Character?     _character;
        private CampaignState  _campaign = new();
        private Phase          _phase    = Phase.CharacterCreation;

        private static readonly JsonSerializerOptions JsonOpts =
            new() { WriteIndented = true };

        public CampaignOrchestrator(string apiKey)
        {
            _characterCreator  = new CharacterCreator(apiKey);
            _campaignOrganiser = new CampaignOrganiser(apiKey);
            _encounterBuilder  = new EncounterBuilder(apiKey);
            _combatRunner      = new CombatRunner(apiKey);
            _narrativeVoice    = new NarrativeVoice(apiKey);
        }

        // ─── Public API ───────────────────────────────────────────────────────

        public Phase CurrentPhase => _phase;
        public Character? Character => _character;
        public CampaignState Campaign => _campaign;

        /// <summary>
        /// Start a brand-new campaign. Returns the opening narration + character creation prompt.
        /// </summary>
        public async Task<string> StartNewCampaignAsync(string campaignName = "The Forgotten Keep")
        {
            _campaign = new CampaignState
            {
                CampaignName = campaignName,
                CurrentLocation = "The Crossroads",
                CurrentLocationDescription = "A windswept crossroads at the edge of civilisation.",
                SessionNumber = 1
            };

            _phase = Phase.CharacterCreation;

            var intro = await _narrativeVoice.DescribeLocation(
                "The Crossroads", "overworld", "wondrous",
                "ancient signpost, distant mountains, a dusty road leading into darkness");

            var charPrompt = await _characterCreator.ChatAsync(
                "Welcome the adventurer and begin the character creation process. " +
                "Ask them to choose a race and class, or offer to create a random character for them. " +
                $"Campaign flavour: {campaignName}");

            return $"{intro}\n\n---\n\n{charPrompt}";
        }

        /// <summary>
        /// Process the player's text input and return the appropriate agent response.
        /// The orchestrator decides which agent(s) to involve based on the current phase.
        /// </summary>
        public async Task<string> PlayerInputAsync(string playerInput)
        {
            return _phase switch
            {
                Phase.CharacterCreation => await HandleCharacterCreationAsync(playerInput),
                Phase.Exploration       => await HandleExplorationAsync(playerInput),
                Phase.Encounter         => await HandleEncounterAsync(playerInput),
                Phase.Combat            => await HandleCombatAsync(playerInput),
                Phase.LootAndRest       => await HandleLootAndRestAsync(playerInput),
                _                       => "Unknown phase — please restart the campaign."
            };
        }

        // ─── Phase handlers ───────────────────────────────────────────────────

        private async Task<string> HandleCharacterCreationAsync(string playerInput)
        {
            var response = await _characterCreator.ChatAsync(playerInput);

            // Try to extract <character_json> from the response
            var characterJson = ExtractTag(response, "character_json");
            if (characterJson != null)
            {
                try
                {
                    _character = JsonSerializer.Deserialize<Character>(characterJson, JsonOpts);
                    if (_character != null)
                    {
                        _campaignOrganiser.InitialiseState(_campaign, _character);
                        _phase = Phase.Exploration;

                        // Log creation and narrate arrival
                        _campaign.CampaignLog.Add($"[Session 1] {_character.Name} the {_character.Race} {_character.Class} enters the world.");

                        var arrival = await _campaignOrganiser.ChatAsync(
                            $"The character {_character.Name} (Level {_character.Level} {_character.Race} {_character.Class}) " +
                            $"has been created. Add a quest: travel to {_campaign.CampaignName} and uncover its secrets. " +
                            $"Suggest the first location to explore. Return updated campaign state in <campaign_json> tags.");

                        SyncCampaignFromResponse(arrival);

                        var locationNarration = await _narrativeVoice.DescribeLocation(
                            _campaign.CurrentLocation,
                            "overworld", "mysterious",
                            $"{_character.Name} sets off on their adventure");

                        return StripTags(response) + "\n\n---\n\n" + locationNarration +
                               "\n\n*Type your action to begin exploring.*";
                    }
                }
                catch (JsonException ex)
                {
                    return $"{response}\n\n*(Error parsing character — please try again: {ex.Message})*";
                }
            }

            return response;
        }

        private async Task<string> HandleExplorationAsync(string playerInput)
        {
            // Let CampaignOrganiser interpret the action and update world state
            var stateJson   = JsonSerializer.Serialize(_campaign, JsonOpts);
            var charJson    = _character != null ? JsonSerializer.Serialize(_character, JsonOpts) : "{}";

            var orgResponse = await _campaignOrganiser.ChatAsync(
                $"Player action: \"{playerInput}\"\n\n" +
                $"Current campaign state:\n{stateJson}\n\n" +
                $"Character state:\n{charJson}\n\n" +
                "Resolve the action. If the player enters a dungeon room or dangerous area, " +
                "decide whether an encounter occurs (50% chance for unexplored rooms). " +
                "If an encounter occurs, set next_phase=encounter in your response. " +
                "Return updated campaign_json and character_json.");

            SyncCampaignFromResponse(orgResponse);
            SyncCharacterFromResponse(orgResponse);

            bool encounterTriggered = orgResponse.Contains("next_phase=encounter", StringComparison.OrdinalIgnoreCase)
                                   || orgResponse.Contains("\"next_phase\": \"encounter\"");

            if (encounterTriggered && _character != null)
            {
                _phase = Phase.Encounter;
                return StripTags(orgResponse) + "\n\n" + await TriggerEncounterAsync();
            }

            // Otherwise narrate the exploration result
            var narration = await _narrativeVoice.DescribeLocation(
                _campaign.CurrentLocation, "dungeon_room", "mysterious",
                ExtractFirstSentence(StripTags(orgResponse)));

            return narration + "\n\n" + StripTags(orgResponse);
        }

        private async Task<string> TriggerEncounterAsync()
        {
            if (_character == null) return "No character loaded.";

            var buildPrompt =
                $"Build an encounter for a Level {_character.Level} {_character.Class} " +
                $"in {_campaign.CurrentLocation}. " +
                "Create 1-3 monsters appropriate for the location. " +
                "Return the Encounter as JSON in <encounter_json> tags.";

            var builderResponse = await _encounterBuilder.ChatAsync(buildPrompt);
            var encounterJson   = ExtractTag(builderResponse, "encounter_json");

            if (encounterJson != null)
            {
                try
                {
                    var encounter = JsonSerializer.Deserialize<Encounter>(encounterJson, JsonOpts);
                    if (encounter != null)
                    {
                        _combatRunner.StartCombat(_character, encounter);
                        _phase = Phase.Combat;

                        var combatNarration = await _narrativeVoice.DescribeCombatStart(
                            encounter.Name,
                            encounter.Monsters.Count,
                            _campaign.CurrentLocation);

                        var combatStart = await _combatRunner.ChatAsync(
                            $"Start combat. Character: {_character.Name}, Dex modifier: {_character.Abilities.DexMod}. " +
                            $"Encounter: {encounter.Name}. Monsters: {string.Join(", ", encounter.Monsters.ConvertAll(m => m.Name))}. " +
                            "Roll initiative and describe the first turn options for the player.");

                        return combatNarration + "\n\n" + StripTags(combatStart);
                    }
                }
                catch (JsonException) { }
            }

            return StripTags(builderResponse);
        }

        private async Task<string> HandleEncounterAsync(string playerInput)
        {
            // Short-circuit: non-combat encounters handled conversationally via organiser
            var response = await _campaignOrganiser.ChatAsync(
                $"Player responds to the encounter: \"{playerInput}\". " +
                "Resolve the encounter narratively. If combat breaks out, set next_phase=combat.");

            SyncCampaignFromResponse(response);

            if (response.Contains("next_phase=combat", StringComparison.OrdinalIgnoreCase))
            {
                _phase = Phase.Combat;
                return StripTags(response) + "\n\n" + await TriggerEncounterAsync();
            }

            _phase = Phase.Exploration;
            return StripTags(response);
        }

        private async Task<string> HandleCombatAsync(string playerInput)
        {
            if (_character == null) return "No character loaded.";

            var combatJson = JsonSerializer.Serialize(_combatRunner.State, JsonOpts);
            var charJson   = JsonSerializer.Serialize(_character, JsonOpts);

            var response = await _combatRunner.ChatAsync(
                $"Player action: \"{playerInput}\"\n\n" +
                $"Combat state:\n{combatJson}\n\nCharacter:\n{charJson}\n\n" +
                "Resolve the player's action and all monster turns. " +
                "Narrate the results. Return updated combat state in <combat_json> tags. " +
                "If combat ends, call end_combat.");

            SyncCombatFromResponse(response);

            if (!_combatRunner.State.IsInCombat)
            {
                return await HandleCombatEndAsync(response);
            }

            // Narrate a significant hit if found in combat log
            var lastLog = _combatRunner.State.CombatLog.Count > 0
                ? _combatRunner.State.CombatLog[^1] : "";

            return StripTags(response);
        }

        private async Task<string> HandleCombatEndAsync(string combatResponse)
        {
            if (_character == null) return StripTags(combatResponse);

            var encounter = _combatRunner.State.CurrentEncounter;
            var outcome   = combatResponse.Contains("defeat") ? "defeat" :
                            combatResponse.Contains("fled")   ? "fled"   : "victory";

            if (outcome == "victory" && encounter != null)
            {
                _phase = Phase.LootAndRest;

                // Award XP and gold via CampaignOrganiser
                var awardResponse = await _campaignOrganiser.ChatAsync(
                    $"The player defeated {encounter.Name}. " +
                    $"Award {encounter.ExperienceReward} XP and {encounter.GoldReward} gold. " +
                    $"Character is now at {_character.ExperiencePoints + encounter.ExperienceReward} XP. " +
                    $"Update campaign state. Return character_json and campaign_json.");

                SyncCampaignFromResponse(awardResponse);
                SyncCharacterFromResponse(awardResponse);

                bool leveledUp = _character.Level > 1 &&
                                 awardResponse.Contains("leveled_up\": true");

                var lootItems = encounter.LootTable.ConvertAll(i => i.Name);
                var lootNarration = await _narrativeVoice.DescribeLoot(
                    lootItems, encounter.GoldReward, _campaign.CurrentLocation);

                string levelUpText = "";
                if (leveledUp)
                    levelUpText = "\n\n" + await _narrativeVoice.DescribeLevelUp(
                        _character.Name, _character.Level, _character.Class);

                return StripTags(combatResponse) + "\n\n" + lootNarration + levelUpText +
                       "\n\n*You may rest, use items, or continue exploring.*";
            }
            else if (outcome == "defeat")
            {
                var deathNarration = await _narrativeVoice.DescribeLocation(
                    "The Void", "dungeon_room", "eerie",
                    $"{_character.Name} falls in battle");

                _phase = Phase.Exploration; // Could implement proper death/respawn
                return StripTags(combatResponse) + "\n\n" + deathNarration;
            }

            _phase = Phase.Exploration;
            return StripTags(combatResponse);
        }

        private async Task<string> HandleLootAndRestAsync(string playerInput)
        {
            var response = await _campaignOrganiser.ChatAsync(
                $"Player action after combat: \"{playerInput}\". " +
                "Handle resting (short rest: roll hit dice, long rest: full HP/spell slots), " +
                "item management, or return to exploration. " +
                "Return updated character_json and campaign_json. " +
                "If player is ready to continue, set next_phase=exploration.");

            SyncCampaignFromResponse(response);
            SyncCharacterFromResponse(response);

            if (response.Contains("next_phase=exploration", StringComparison.OrdinalIgnoreCase))
                _phase = Phase.Exploration;

            return StripTags(response);
        }

        // ─── State sync helpers ───────────────────────────────────────────────

        private void SyncCampaignFromResponse(string response)
        {
            var json = ExtractTag(response, "campaign_json");
            if (json == null) return;
            try
            {
                var updated = JsonSerializer.Deserialize<CampaignState>(json, JsonOpts);
                if (updated != null) _campaign = updated;
            }
            catch (JsonException) { }
        }

        private void SyncCharacterFromResponse(string response)
        {
            var json = ExtractTag(response, "character_json");
            if (json == null) return;
            try
            {
                var updated = JsonSerializer.Deserialize<Character>(json, JsonOpts);
                if (updated != null)
                {
                    _character = updated;
                    _campaignOrganiser.InitialiseState(_campaign, _character);
                }
            }
            catch (JsonException) { }
        }

        private void SyncCombatFromResponse(string response)
        {
            var json = ExtractTag(response, "combat_json");
            if (json == null) return;
            try
            {
                var updated = JsonSerializer.Deserialize<CombatState>(json, JsonOpts);
                if (updated != null) _combatRunner.State = updated;
            }
            catch (JsonException) { }
        }

        // ─── Utility ──────────────────────────────────────────────────────────

        private static string? ExtractTag(string text, string tag)
        {
            var match = Regex.Match(text, $@"<{tag}>(.*?)</{tag}>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string StripTags(string text)
        {
            // Remove any <tag>...</tag> blocks used for machine parsing
            text = Regex.Replace(text, @"<\w+_json>.*?</\w+_json>",
                "", RegexOptions.Singleline);
            return text.Trim();
        }

        private static string ExtractFirstSentence(string text)
        {
            var dot = text.IndexOf('.');
            return dot > 0 ? text[..dot].Trim() : text.Length > 120 ? text[..120] : text;
        }
    }

    /// <summary>Campaign flow phases.</summary>
    public enum Phase
    {
        CharacterCreation,
        Exploration,
        Encounter,
        Combat,
        LootAndRest
    }
}
