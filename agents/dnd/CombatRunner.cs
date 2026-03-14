using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Manages turn-by-turn D&D 5e combat: initiative, player actions,
    /// monster AI, damage resolution, and victory/defeat conditions.
    /// </summary>
    public class CombatRunner : DndAgent
    {
        private readonly Random _rng = new();
        private Character? _player;

        public CombatRunner(string baseUrl, string modelName) : base(baseUrl, modelName) { }

        protected override string AgentName => "CombatRunner";

        protected override string SystemPrompt =>
            """
            You are CombatRunner, a precise and exciting D&D 5e combat master.
            You run turn-based combat with flair, keeping the action tense and dramatic.

            Combat loop:
            1. Call roll_initiative to set the order.
            2. On each combatant's turn, narrate the situation and ask the player for their action,
               OR resolve the monster's action automatically using make_attack.
            3. Apply damage via apply_damage, check for death.
            4. When all enemies are defeated (or the player flees), call end_combat.

            Rules:
            - Player actions: Attack, Cast Spell, Dash, Disengage, Help, Hide, Use Item.
            - Monsters use their stat block actions; favour their special attack on a 1-in-3 chance.
            - A creature at 0 HP is unconscious; monsters die, players make death saves.
            - Critical hit on natural 20: double the damage dice.
            - Use evocative language: describe the clash of steel, the crackle of magic, the roar of monsters.

            Always return the updated CombatState as JSON inside <combat_json> tags.

            Use lookup_srd to check official combat rules and equipment data (topics: combat, equipment) when needed.
            """;

        protected override List<ChatTool> Tools => new()
        {
            MakeTool(
                "roll_initiative",
                "Roll initiative for all combatants and set the turn order.",
                new Dictionary<string, object>
                {
                    ["player_dex_mod"] = IntProp("Player's Dexterity modifier")
                },
                new() { "player_dex_mod" }),

            MakeTool(
                "make_attack",
                "Resolve a single attack from one combatant against another.",
                new Dictionary<string, object>
                {
                    ["attacker_name"]  = StringProp("Name of the attacking combatant"),
                    ["target_name"]    = StringProp("Name of the target"),
                    ["attack_bonus"]   = IntProp("Attack roll modifier (total bonus to d20)"),
                    ["damage_dice"]    = StringProp("Damage dice expression, e.g. '1d8' or '2d6'"),
                    ["damage_bonus"]   = IntProp("Flat bonus added to damage roll"),
                    ["damage_type"]    = StringProp("Damage type: slashing, piercing, bludgeoning, fire, etc."),
                    ["is_spell_attack"] = BoolProp("True if this is a spell attack")
                },
                new() { "attacker_name", "target_name", "attack_bonus", "damage_dice", "damage_type" }),

            MakeTool(
                "apply_damage",
                "Apply a specific amount of damage to a combatant.",
                new Dictionary<string, object>
                {
                    ["target_name"]  = StringProp("Name of the combatant taking damage"),
                    ["damage_amount"] = IntProp("Amount of damage to apply"),
                    ["damage_type"]  = StringProp("Type of damage")
                },
                new() { "target_name", "damage_amount", "damage_type" }),

            MakeTool(
                "apply_healing",
                "Heal a combatant for a specified amount.",
                new Dictionary<string, object>
                {
                    ["target_name"]   = StringProp("Name of the combatant being healed"),
                    ["heal_amount"]   = IntProp("Amount of HP restored")
                },
                new() { "target_name", "heal_amount" }),

            MakeTool(
                "end_combat",
                "Mark the combat as finished and report the outcome.",
                new Dictionary<string, object>
                {
                    ["outcome"] = StringProp("victory | defeat | fled")
                },
                new() { "outcome" })
        };

        public CombatState State { get; set; } = new();

        public void StartCombat(Character player, Encounter encounter)
        {
            _player = player;
            State = new CombatState
            {
                IsInCombat      = true,
                CurrentEncounter = encounter,
                CombatLog       = new List<string>()
            };

            // Add player combatant
            State.InitiativeOrder.Add(new Combatant
            {
                Name         = player.Name,
                IsPlayer     = true,
                HitPoints    = player.HitPoints,
                MaxHitPoints = player.MaxHitPoints
            });

            // Add monsters
            foreach (var m in encounter.Monsters)
            {
                State.InitiativeOrder.Add(new Combatant
                {
                    Name         = m.Name,
                    IsPlayer     = false,
                    HitPoints    = m.HitPoints,
                    MaxHitPoints = m.MaxHitPoints
                });
            }

            ClearHistory();
        }

        public bool IsCombatOver =>
            !State.IsInCombat ||
            State.InitiativeOrder.Where(c => !c.IsPlayer).All(c => !c.IsAlive) ||
            !State.InitiativeOrder.Where(c => c.IsPlayer).Any(c => c.IsAlive);

        protected override Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            return toolName switch
            {
                "roll_initiative" => Task.FromResult(RollInitiative(input)),
                "make_attack"     => Task.FromResult(MakeAttack(input)),
                "apply_damage"    => Task.FromResult(ApplyDamage(input)),
                "apply_healing"   => Task.FromResult(ApplyHealing(input)),
                "end_combat"      => Task.FromResult(EndCombat(input)),
                _                 => base.HandleToolCallAsync(toolName, input)
            };
        }

        private string RollInitiative(IReadOnlyDictionary<string, JsonElement> input)
        {
            int playerDexMod = input["player_dex_mod"].GetInt32();

            foreach (var c in State.InitiativeOrder)
            {
                if (c.IsPlayer)
                    c.Initiative = _rng.Next(1, 21) + playerDexMod;
                else
                {
                    // Find matching monster for its dex mod
                    var monster = State.CurrentEncounter?.Monsters
                        .FirstOrDefault(m => m.Name == c.Name);
                    int monDexMod = monster?.Abilities.DexMod ?? 0;
                    c.Initiative = _rng.Next(1, 21) + monDexMod;
                }
            }

            State.InitiativeOrder = State.InitiativeOrder
                .OrderByDescending(c => c.Initiative)
                .ToList();

            var order = State.InitiativeOrder
                .Select(c => new { c.Name, c.Initiative, c.IsPlayer })
                .ToList();

            Log($"Initiative set: {string.Join(", ", order.Select(o => $"{o.Name}({o.Initiative})"))}");

            return JsonSerializer.Serialize(new { initiative_order = order });
        }

        private string MakeAttack(IReadOnlyDictionary<string, JsonElement> input)
        {
            var attackerName = input["attacker_name"].GetString() ?? "";
            var targetName   = input["target_name"].GetString() ?? "";
            int attackBonus  = input["attack_bonus"].GetInt32();
            var damageDice   = input["damage_dice"].GetString() ?? "1d6";
            int damageBonus  = input.TryGetValue("damage_bonus", out var db) ? db.GetInt32() : 0;
            var damageType   = input["damage_type"].GetString() ?? "bludgeoning";

            var target = FindCombatant(targetName);
            if (target == null)
                return JsonSerializer.Serialize(new { error = $"Target not found: {targetName}" });

            int targetAC = GetCombatantAC(targetName);

            int d20 = _rng.Next(1, 21);
            bool crit = d20 == 20;
            bool miss = d20 == 1;
            int totalAttack = d20 + attackBonus;

            bool hit = !miss && (crit || totalAttack >= targetAC);

            int damage = 0;
            if (hit)
            {
                damage = RollDice(damageDice) + damageBonus;
                if (crit) damage += RollDice(damageDice); // extra dice on crit

                target.HitPoints = Math.Max(0, target.HitPoints - damage);
            }

            var result = new
            {
                attacker    = attackerName,
                target      = targetName,
                d20_roll    = d20,
                total_roll  = totalAttack,
                target_ac   = targetAC,
                hit,
                crit,
                damage,
                damage_type = damageType,
                target_hp_remaining = target.HitPoints,
                target_alive = target.IsAlive
            };

            string resultMsg = hit
                ? (crit ? $"CRITICAL HIT! {attackerName} deals {damage} {damageType} damage to {targetName}!"
                        : $"{attackerName} hits {targetName} for {damage} {damageType} damage.")
                : $"{attackerName} misses {targetName}. (Rolled {totalAttack} vs AC {targetAC})";

            Log(resultMsg);

            return JsonSerializer.Serialize(result);
        }

        private string ApplyDamage(IReadOnlyDictionary<string, JsonElement> input)
        {
            var targetName = input["target_name"].GetString() ?? "";
            int amount     = input["damage_amount"].GetInt32();
            var type       = input["damage_type"].GetString() ?? "untyped";

            var target = FindCombatant(targetName);
            if (target == null)
                return JsonSerializer.Serialize(new { error = $"Target not found: {targetName}" });

            target.HitPoints = Math.Max(0, target.HitPoints - amount);
            Log($"{targetName} takes {amount} {type} damage. ({target.HitPoints}/{target.MaxHitPoints} HP)");

            return JsonSerializer.Serialize(new
            {
                target = targetName,
                damage = amount,
                hp_remaining = target.HitPoints,
                is_alive = target.IsAlive
            });
        }

        private string ApplyHealing(IReadOnlyDictionary<string, JsonElement> input)
        {
            var targetName = input["target_name"].GetString() ?? "";
            int amount     = input["heal_amount"].GetInt32();

            var target = FindCombatant(targetName);
            if (target == null)
                return JsonSerializer.Serialize(new { error = $"Target not found: {targetName}" });

            int before = target.HitPoints;
            target.HitPoints = Math.Min(target.MaxHitPoints, target.HitPoints + amount);
            int healed = target.HitPoints - before;

            Log($"{targetName} is healed for {healed} HP. ({target.HitPoints}/{target.MaxHitPoints})");

            return JsonSerializer.Serialize(new
            {
                target = targetName,
                healed,
                hp = target.HitPoints,
                max_hp = target.MaxHitPoints
            });
        }

        private string EndCombat(IReadOnlyDictionary<string, JsonElement> input)
        {
            var outcome = input["outcome"].GetString() ?? "victory";
            State.IsInCombat = false;
            Log($"Combat ended: {outcome}");

            return JsonSerializer.Serialize(new
            {
                outcome,
                rounds       = State.Round,
                combat_log   = State.CombatLog
            });
        }

        private Combatant? FindCombatant(string name) =>
            State.InitiativeOrder.FirstOrDefault(
                c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private int GetCombatantAC(string name)
        {
            if (_player != null && _player.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return _player.ArmorClass;

            if (State.CurrentEncounter == null) return 10;
            var monster = State.CurrentEncounter.Monsters
                .FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return monster?.ArmorClass ?? 10;
        }

        private int RollDice(string expression)
        {
            // Parse "NdM" or "NdM+B" or "NdM-B"
            expression = expression.Trim().ToLower();
            var parts = expression.Split('d');
            if (parts.Length != 2) return 0;

            int count = int.TryParse(parts[0], out var c) ? c : 1;

            int bonus = 0;
            string sidesPart = parts[1];
            int plusIdx = sidesPart.IndexOf('+');
            int minusIdx = sidesPart.IndexOf('-');
            int splitIdx = plusIdx >= 0 ? plusIdx : minusIdx;

            if (splitIdx >= 0)
            {
                int.TryParse(sidesPart[(splitIdx + 1)..], out bonus);
                if (minusIdx >= 0 && plusIdx < 0) bonus = -bonus;
                sidesPart = sidesPart[..splitIdx];
            }

            int sides = int.TryParse(sidesPart, out var s) ? s : 6;

            int total = 0;
            for (int i = 0; i < count; i++)
                total += _rng.Next(1, sides + 1);
            return total + bonus;
        }

        private void Log(string message)
        {
            State.CombatLog.Add(message);
        }
    }
}
