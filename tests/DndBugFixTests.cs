using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using GdUnit4;
using Harness.Agents.Dnd;
using static GdUnit4.Assertions;

namespace Harness.Tests
{
    // ─── Bug 1: AbilityScores modifier math ──────────────────────────────────
    // D&D 5e formula: floor((score - 10) / 2)
    // The old code used C# integer division which truncates toward zero,
    // giving wrong results for odd scores below 10 (e.g. 9 → 0 instead of -1).

    [TestSuite]
    public class AbilityScoreModifierTests
    {
        [TestCase]
        public void Score10_GivesModifier0()
        {
            var scores = new AbilityScores { Strength = 10 };
            AssertThat(scores.StrMod).IsEqual(0);
        }

        [TestCase]
        public void Score9_GivesModifierNeg1()
        {
            // This was the core bug: C# integer division gave 0 instead of -1
            var scores = new AbilityScores { Dexterity = 9 };
            AssertThat(scores.DexMod).IsEqual(-1);
        }

        [TestCase]
        public void Score7_GivesModifierNeg2()
        {
            var scores = new AbilityScores { Constitution = 7 };
            AssertThat(scores.ConMod).IsEqual(-2);
        }

        [TestCase]
        public void Score1_GivesModifierNeg5()
        {
            var scores = new AbilityScores { Intelligence = 1 };
            AssertThat(scores.IntMod).IsEqual(-5);
        }

        [TestCase]
        public void Score20_GivesModifier5()
        {
            var scores = new AbilityScores { Wisdom = 20 };
            AssertThat(scores.WisMod).IsEqual(5);
        }

        [TestCase]
        public void Score11_GivesModifier0()
        {
            // Odd score above 10 — should still be 0, not round up
            var scores = new AbilityScores { Charisma = 11 };
            AssertThat(scores.ChaMod).IsEqual(0);
        }

        [TestCase]
        public void Score8_GivesModifierNeg1()
        {
            // Even score below 10
            var scores = new AbilityScores { Strength = 8 };
            AssertThat(scores.StrMod).IsEqual(-1);
        }

        [TestCase]
        public void AllStandardScores_MatchDnd5eTable()
        {
            // Full D&D 5e ability modifier table for scores 1-20
            var expected = new Dictionary<int, int>
            {
                [1] = -5, [2] = -4, [3] = -4, [4] = -3, [5] = -3,
                [6] = -2, [7] = -2, [8] = -1, [9] = -1, [10] = 0,
                [11] = 0, [12] = 1, [13] = 1, [14] = 2, [15] = 2,
                [16] = 3, [17] = 3, [18] = 4, [19] = 4, [20] = 5
            };

            foreach (var (score, mod) in expected)
            {
                var scores = new AbilityScores { Strength = score };
                AssertThat(scores.StrMod).IsEqual(mod);
            }
        }
    }

    // ─── Bugs 2 & 3: CombatRunner AC lookup and dice parsing ─────────────────
    // We need to call the protected HandleToolCallAsync, so we use a test subclass.

    /// <summary>
    /// Exposes CombatRunner's protected tool handler for direct testing
    /// without needing an LLM connection.
    /// </summary>
    public class TestableCombatRunner : CombatRunner
    {
        public TestableCombatRunner()
            : base("http://localhost:1234/v1", "test-model") { }

        public Task<string> CallToolAsync(
            string toolName, Dictionary<string, JsonElement> input)
            => HandleToolCallAsync(toolName,
                new Dictionary<string, JsonElement>(input, StringComparer.OrdinalIgnoreCase));
    }

    [TestSuite]
    public class CombatRunnerAcTests
    {
        private TestableCombatRunner _runner = null!;

        [Before]
        public void Setup()
        {
            _runner = new TestableCombatRunner();
        }

        [TestCase]
        public async Task PlayerAC_UsesActualArmorClass()
        {
            // Bug 2: GetCombatantAC used to return 10 for the player
            var player = new Character
            {
                Name = "Thorn",
                ArmorClass = 18,
                HitPoints = 50,
                MaxHitPoints = 50,
                Abilities = new AbilityScores { Dexterity = 14 }
            };

            var encounter = new Encounter
            {
                Name = "Test Encounter",
                Monsters = new List<Monster>
                {
                    new Monster
                    {
                        Name = "Goblin",
                        HitPoints = 10, MaxHitPoints = 10,
                        ArmorClass = 13,
                        Abilities = new AbilityScores { Dexterity = 12 },
                        Actions = new List<MonsterAction>
                        {
                            new MonsterAction { Name = "Scimitar", DamageDice = "1d6" }
                        }
                    }
                }
            };

            _runner.StartCombat(player, encounter);

            // Attack the player — the returned JSON should show target_ac = 18
            var input = MakeArgs(new Dictionary<string, object>
            {
                ["attacker_name"] = "Goblin",
                ["target_name"] = "Thorn",
                ["attack_bonus"] = 4,
                ["damage_dice"] = "1d6",
                ["damage_type"] = "slashing"
            });

            var resultJson = await _runner.CallToolAsync("make_attack", input);
            using var doc = JsonDocument.Parse(resultJson);
            var targetAc = doc.RootElement.GetProperty("target_ac").GetInt32();

            AssertThat(targetAc).IsEqual(18);
        }

        [TestCase]
        public async Task MonsterAC_StillWorksCorrectly()
        {
            var player = new Character
            {
                Name = "Thorn",
                ArmorClass = 16,
                HitPoints = 50, MaxHitPoints = 50,
                Abilities = new AbilityScores { Dexterity = 14 }
            };

            var encounter = new Encounter
            {
                Name = "Test",
                Monsters = new List<Monster>
                {
                    new Monster
                    {
                        Name = "Ogre",
                        HitPoints = 59, MaxHitPoints = 59,
                        ArmorClass = 11,
                        Abilities = new AbilityScores { Dexterity = 8 }
                    }
                }
            };

            _runner.StartCombat(player, encounter);

            var input = MakeArgs(new Dictionary<string, object>
            {
                ["attacker_name"] = "Thorn",
                ["target_name"] = "Ogre",
                ["attack_bonus"] = 5,
                ["damage_dice"] = "1d8",
                ["damage_type"] = "slashing"
            });

            var resultJson = await _runner.CallToolAsync("make_attack", input);
            using var doc = JsonDocument.Parse(resultJson);
            var targetAc = doc.RootElement.GetProperty("target_ac").GetInt32();

            AssertThat(targetAc).IsEqual(11);
        }

        private static Dictionary<string, JsonElement> MakeArgs(Dictionary<string, object> raw)
        {
            var json = JsonSerializer.Serialize(raw);
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.Clone();
            return result;
        }
    }

    [TestSuite]
    public class CombatRunnerDiceTests
    {
        private TestableCombatRunner _runner = null!;

        [Before]
        public void Setup()
        {
            _runner = new TestableCombatRunner();

            var player = new Character
            {
                Name = "TestHero",
                ArmorClass = 10, HitPoints = 999, MaxHitPoints = 999,
                Abilities = new AbilityScores { Dexterity = 10 }
            };
            var encounter = new Encounter
            {
                Name = "Dice Test",
                Monsters = new List<Monster>
                {
                    new Monster
                    {
                        Name = "Target Dummy",
                        HitPoints = 9999, MaxHitPoints = 9999,
                        ArmorClass = 1, // AC 1 so almost every attack hits
                        Abilities = new AbilityScores { Dexterity = 10 }
                    }
                }
            };

            _runner.StartCombat(player, encounter);
        }

        [TestCase]
        public async Task DiceWithBonus_IncludesBonus()
        {
            // Bug 3: "1d4+5" used to fail parsing the +5 and default to d6.
            // With the fix, minimum damage from 1d4+5 is 6 (1+5), max is 9 (4+5).
            // We run multiple attacks to confirm damage is always in the valid range.
            // attack_bonus=99 ensures a hit (not a nat-1 miss).
            int minSeen = int.MaxValue;
            int maxSeen = int.MinValue;

            for (int i = 0; i < 100; i++)
            {
                // Reset target HP each iteration
                var dummy = _runner.State.InitiativeOrder.Find(c => !c.IsPlayer)!;
                dummy.HitPoints = 9999;

                var input = MakeArgs(new Dictionary<string, object>
                {
                    ["attacker_name"] = "TestHero",
                    ["target_name"] = "Target Dummy",
                    ["attack_bonus"] = 99,
                    ["damage_dice"] = "1d4+5",
                    ["damage_bonus"] = 0,
                    ["damage_type"] = "bludgeoning"
                });

                var resultJson = await _runner.CallToolAsync("make_attack", input);
                using var doc = JsonDocument.Parse(resultJson);

                var d20 = doc.RootElement.GetProperty("d20_roll").GetInt32();
                if (d20 == 1) continue; // nat 1 auto-miss, skip

                var hit = doc.RootElement.GetProperty("hit").GetBoolean();
                if (!hit) continue;

                var damage = doc.RootElement.GetProperty("damage").GetInt32();
                var crit = doc.RootElement.GetProperty("crit").GetBoolean();
                if (crit) continue; // crits double dice, skip for clean bounds check

                if (damage < minSeen) minSeen = damage;
                if (damage > maxSeen) maxSeen = damage;
            }

            // 1d4+5: min=6, max=9
            AssertThat(minSeen).IsGreaterEqual(6);
            AssertThat(maxSeen).IsLessEqual(9);
        }

        [TestCase]
        public async Task DiceWithoutBonus_StillWorks()
        {
            // Regression: plain "1d6" should still work (min=1, max=6)
            int minSeen = int.MaxValue;
            int maxSeen = int.MinValue;

            for (int i = 0; i < 100; i++)
            {
                var dummy = _runner.State.InitiativeOrder.Find(c => !c.IsPlayer)!;
                dummy.HitPoints = 9999;

                var input = MakeArgs(new Dictionary<string, object>
                {
                    ["attacker_name"] = "TestHero",
                    ["target_name"] = "Target Dummy",
                    ["attack_bonus"] = 99,
                    ["damage_dice"] = "1d6",
                    ["damage_bonus"] = 0,
                    ["damage_type"] = "bludgeoning"
                });

                var resultJson = await _runner.CallToolAsync("make_attack", input);
                using var doc = JsonDocument.Parse(resultJson);

                var d20 = doc.RootElement.GetProperty("d20_roll").GetInt32();
                if (d20 == 1) continue;

                var hit = doc.RootElement.GetProperty("hit").GetBoolean();
                if (!hit) continue;

                var damage = doc.RootElement.GetProperty("damage").GetInt32();
                var crit = doc.RootElement.GetProperty("crit").GetBoolean();
                if (crit) continue;

                if (damage < minSeen) minSeen = damage;
                if (damage > maxSeen) maxSeen = damage;
            }

            AssertThat(minSeen).IsGreaterEqual(1);
            AssertThat(maxSeen).IsLessEqual(6);
        }

        [TestCase]
        public async Task DiceWithNegativeBonus_SubtractsCorrectly()
        {
            // "1d8-2": min=max(0, 1-2)=-1 but damage floor is 0 via HP clamping.
            // Actually damage itself can be negative here since it's RollDice result.
            // RollDice(1d8-2) range: -1 to 6. With damage_bonus=0, damage = RollDice result.
            // But the attack only deals the computed damage, which can be 0+ after HP clamping.
            // We just verify the bonus is subtracted (i.e. damage < max of plain 1d8).
            int maxSeen = int.MinValue;

            for (int i = 0; i < 100; i++)
            {
                var dummy = _runner.State.InitiativeOrder.Find(c => !c.IsPlayer)!;
                dummy.HitPoints = 9999;

                var input = MakeArgs(new Dictionary<string, object>
                {
                    ["attacker_name"] = "TestHero",
                    ["target_name"] = "Target Dummy",
                    ["attack_bonus"] = 99,
                    ["damage_dice"] = "1d8-2",
                    ["damage_bonus"] = 0,
                    ["damage_type"] = "bludgeoning"
                });

                var resultJson = await _runner.CallToolAsync("make_attack", input);
                using var doc = JsonDocument.Parse(resultJson);

                var d20 = doc.RootElement.GetProperty("d20_roll").GetInt32();
                if (d20 == 1) continue;

                var hit = doc.RootElement.GetProperty("hit").GetBoolean();
                if (!hit) continue;

                var damage = doc.RootElement.GetProperty("damage").GetInt32();
                var crit = doc.RootElement.GetProperty("crit").GetBoolean();
                if (crit) continue;

                if (damage > maxSeen) maxSeen = damage;
            }

            // 1d8-2: max possible is 8-2=6 (not 8)
            AssertThat(maxSeen).IsLessEqual(6);
        }

        private static Dictionary<string, JsonElement> MakeArgs(Dictionary<string, object> raw)
        {
            var json = JsonSerializer.Serialize(raw);
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.Clone();
            return result;
        }
    }
}
