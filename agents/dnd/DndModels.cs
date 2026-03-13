using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Harness.Agents.Dnd
{
    // ─── Character ────────────────────────────────────────────────────────────

    public class Character
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public int Level { get; set; } = 1;
        public int ExperiencePoints { get; set; } = 0;
        public AbilityScores Abilities { get; set; } = new();
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public int ArmorClass { get; set; }
        public int Speed { get; set; } = 30;
        public List<string> Skills { get; set; } = new();
        public List<string> Proficiencies { get; set; } = new();
        public List<string> Spells { get; set; } = new();
        public Inventory Inventory { get; set; } = new();
        public string Backstory { get; set; } = "";
        public string Alignment { get; set; } = "Neutral";
        public List<string> Features { get; set; } = new();

        [JsonIgnore]
        public int ProficiencyBonus => Level switch
        {
            <= 4 => 2, <= 8 => 3, <= 12 => 4, <= 16 => 5, _ => 6
        };
    }

    public class AbilityScores
    {
        public int Strength { get; set; }
        public int Dexterity { get; set; }
        public int Constitution { get; set; }
        public int Intelligence { get; set; }
        public int Wisdom { get; set; }
        public int Charisma { get; set; }

        [JsonIgnore]
        public int StrMod => (Strength - 10) / 2;
        [JsonIgnore]
        public int DexMod => (Dexterity - 10) / 2;
        [JsonIgnore]
        public int ConMod => (Constitution - 10) / 2;
        [JsonIgnore]
        public int IntMod => (Intelligence - 10) / 2;
        [JsonIgnore]
        public int WisMod => (Wisdom - 10) / 2;
        [JsonIgnore]
        public int ChaMod => (Charisma - 10) / 2;
    }

    public class Inventory
    {
        public int Gold { get; set; } = 0;
        public List<Item> Items { get; set; } = new();
    }

    public class Item
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "misc"; // weapon, armor, potion, misc
        public int Quantity { get; set; } = 1;
        public bool IsEquipped { get; set; } = false;
        public Dictionary<string, int> Stats { get; set; } = new();
    }

    // ─── Campaign ─────────────────────────────────────────────────────────────

    public class CampaignState
    {
        public string CampaignName { get; set; } = "Unnamed Campaign";
        public string CurrentLocation { get; set; } = "";
        public string CurrentLocationDescription { get; set; } = "";
        public List<string> ActiveQuests { get; set; } = new();
        public List<string> CompletedQuests { get; set; } = new();
        public List<string> KnownNpcs { get; set; } = new();
        public List<string> VisitedLocations { get; set; } = new();
        public List<string> CampaignLog { get; set; } = new();
        public DungeonState? CurrentDungeon { get; set; }
        public string WorldFlavour { get; set; } = "classic fantasy";
        public int SessionNumber { get; set; } = 1;
    }

    public class DungeonState
    {
        public string Name { get; set; } = "";
        public int TotalRooms { get; set; }
        public int CurrentRoom { get; set; } = 1;
        public List<string> ClearedRooms { get; set; } = new();
        public List<string> RoomDescriptions { get; set; } = new();
        public bool IsBossRoom { get; set; } = false;
        public string BossName { get; set; } = "";
    }

    // ─── Encounter / Combat ───────────────────────────────────────────────────

    public class Encounter
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "combat"; // combat, social, exploration, puzzle
        public List<Monster> Monsters { get; set; } = new();
        public int ExperienceReward { get; set; }
        public List<Item> LootTable { get; set; } = new();
        public int GoldReward { get; set; }
        public string SpecialConditions { get; set; } = "";
    }

    public class Monster
    {
        public string Name { get; set; } = "";
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public int ArmorClass { get; set; }
        public int Speed { get; set; } = 30;
        public AbilityScores Abilities { get; set; } = new();
        public int ChallengeRating { get; set; }
        public List<MonsterAction> Actions { get; set; } = new();
        public string Description { get; set; } = "";
        public bool IsAlive => HitPoints > 0;
        public int Initiative { get; set; }
    }

    public class MonsterAction
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string AttackBonus { get; set; } = "+0";
        public string DamageDice { get; set; } = "1d6";
        public int DamageBonus { get; set; } = 0;
        public string DamageType { get; set; } = "bludgeoning";
    }

    public class CombatState
    {
        public bool IsInCombat { get; set; } = false;
        public List<Combatant> InitiativeOrder { get; set; } = new();
        public int CurrentTurn { get; set; } = 0;
        public int Round { get; set; } = 1;
        public Encounter? CurrentEncounter { get; set; }
        public List<string> CombatLog { get; set; } = new();
    }

    public class Combatant
    {
        public string Name { get; set; } = "";
        public bool IsPlayer { get; set; }
        public int Initiative { get; set; }
        public int HitPoints { get; set; }
        public int MaxHitPoints { get; set; }
        public bool IsAlive => HitPoints > 0;
        public List<string> Conditions { get; set; } = new(); // poisoned, stunned, etc.
    }

    // ─── Agent messages ───────────────────────────────────────────────────────

    public class AgentResponse
    {
        public string AgentName { get; set; } = "";
        public string Narrative { get; set; } = "";
        public string? UpdatedStateJson { get; set; }
        public List<string> Actions { get; set; } = new();
        public bool RequiresPlayerInput { get; set; } = true;
        public string? PlayerPrompt { get; set; }
    }
}
