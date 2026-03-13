# Agents

Documentation and prompts for AI agents collaborating on this Godot harness project.

---

## D&D Campaign Agents (`agents/dnd/`)

A suite of five specialist agents powered by **LM Studio** (OpenAI-compatible
local API) that together run a full text-based D&D 5e campaign — character
creation, exploration, encounters, combat, loot, and levelling up.

> **Backend**: LM Studio at `http://localhost:1234/v1`
> **Model**: `glm-4.7-flash-uncensored-heretic-neo-code-imatrix-max`
> No API key required — any string is accepted by the local server.

### Architecture

```
CampaignOrchestrator
├── CharacterCreator   — guided 5e character creation with dice-rolling tools
├── CampaignOrganiser  — world state, quests, XP/loot awards, level-ups
├── EncounterBuilder   — generates scaled combat/social/exploration encounters
├── CombatRunner       — turn-based combat (initiative, attacks, death saves)
└── NarrativeVoice     — converts game-state events into evocative prose
```

All agents extend `DndAgent`, which wraps the Anthropic C# SDK with:
- Adaptive thinking (`ThinkingConfigAdaptive`) for complex decisions
- Automatic tool-call loop (calls tool → feeds result → repeats until `end_turn`)
- Shared conversation history per agent

### State models (`DndModels.cs`)

| Model | Purpose |
|-------|---------|
| `Character` | Full 5e character sheet |
| `AbilityScores` | STR/DEX/CON/INT/WIS/CHA with computed modifiers |
| `Inventory` / `Item` | Equipment and gold tracking |
| `CampaignState` | World location, quests, visited places, campaign log |
| `DungeonState` | Per-dungeon room tracking |
| `Encounter` / `Monster` / `MonsterAction` | Combat encounters with stat blocks |
| `CombatState` / `Combatant` | Live initiative order and HP tracking |

### Campaign phases (`Phase` enum)

```
CharacterCreation → Exploration → Encounter → Combat → LootAndRest → Exploration → …
```

### Quick start

```csharp
// Defaults target LM Studio on localhost:1234 with the glm-4.7 model.
// Pass custom baseUrl / modelName to override.
var orchestrator = new CampaignOrchestrator();

// Start a new campaign — returns opening narration + char creation prompt
string opening = await orchestrator.StartNewCampaignAsync("Shadows of the Forgotten Keep");
Console.WriteLine(opening);

// Player interaction loop
while (true)
{
    Console.Write("\n> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) break;
    var response = await orchestrator.PlayerInputAsync(input);
    Console.WriteLine(response);
}
```

### LM Studio setup

1. Open LM Studio → load `glm-4.7-flash-uncensored-heretic-neo-code-imatrix-max`.
2. Start the **Local Server** on port `1234` (default).
3. No API key needed — any value is accepted.

To use a different model or port, pass the overrides explicitly:

```csharp
var orchestrator = new CampaignOrchestrator(
    baseUrl:   "http://localhost:1234/v1",
    modelName: "your-model-identifier-here");
```

---

## DevTools Agent (`game/DevTools.cs`)

File-based command interface for local coding agents.
Commands via `user://devtools_commands.json`, results in `user://devtools_results.json`.

Supported commands: `screenshot`, `scene_tree`, `validate_scene`, `validate_all_scenes`,
`get_state`, `set_state`, `run_method`, `performance`, `quit`, `ping`,
`input_press`, `input_release`, `input_tap`, `input_clear`, `input_actions`, `input_sequence`.
