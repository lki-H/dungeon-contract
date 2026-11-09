using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcQualityChecks
    {
        public static JObject Manifest(string scheme = "ranger", int count = 1)
        {
            return new JObject { ["run_id"] = "run_quality_unit", ["mode"] = "trial", ["seed"] = 20261012, ["config"] = JObject.Parse(File.ReadAllText("../../docs/game-demo/config/demo-v2.json")), ["p1_world"] = JObject.Parse(File.ReadAllText("../../docs/game-demo/config/world-v2.json")), ["members"] = new JArray(Enumerable.Range(0, count).Select(slot => new JObject { ["player_id"] = (1001 + slot).ToString(), ["slot"] = slot, ["loadout_id"] = scheme })), ["task_requirements"] = new JObject() };
        }

        public static void Run()
        {
            var results = new JArray();
            var world = new GrayboxWorld(Manifest()); var original = world.Capture();
            world.Input("1001", new GrayboxInput { Move = Vector2.right, Sequence = 1 }); world.Tick(33); world.Restore(original);
            Require(JToken.DeepEquals(original, world.Capture()), "complete checkpoint restore"); results.Add("checkpoint");
            world.Input("1001", new GrayboxInput { Move = Vector2.right, Sequence = 1 }); Require(world.Action("1001", "dodge"), "dodge accepted"); world.Tick(33);
            var first = world.Snapshot()["players"][0];
            world.Input("1001", new GrayboxInput { Move = Vector2.up, Sequence = 2, Attack = true }); world.Tick(66);
            var second = world.Snapshot()["players"][0];
            Require((float)second["x"] > (float)first["x"] && Math.Abs((float)second["y"] - (float)first["y"]) < 0.001f, "dodge direction locked");
            Require(((JArray)world.Snapshot()["projectiles"]).Count > 0, "dodge allows attack"); results.Add("locked_dodge_and_attack");
            world = new GrayboxWorld(Manifest()); world.Input("1001", new GrayboxInput { Aim = 16384 }); world.Action("1001", "dodge"); world.Tick(33);
            Require((float)world.Snapshot()["players"][0]["y"] > 4, "initial dodge follows aim"); results.Add("initial_aim");
            world = new GrayboxWorld(Manifest("runesmith")); var state = world.Capture(); state["players"][0]["Upgrades"] = new JArray("dual_turret"); world.Restore(state);
            Require(world.Action("1001", "ability", new GrayboxInput { HasPlacement = true, Placement = new Vector2(5, 4), Aim = 0 }), "dual placement accepted");
            Require(((JArray)world.Snapshot()["turrets"]).Count == 2 && !world.Action("1001", "ability"), "dual deployment atomic with cooldown"); results.Add("dual_turret");
            world = new GrayboxWorld(Manifest("runesmith")); Require(!world.Action("1001", "ability", new GrayboxInput { HasPlacement = true, Placement = new Vector2(0, 0) }), "invalid placement refused");
            Require((long)world.Snapshot()["players"][0]["ability_remaining_ms"] == 0, "invalid placement does not consume cooldown"); results.Add("invalid_placement");
            world = new GrayboxWorld(Manifest("ranger", 2)); state = world.Capture();
            state["players"][0]["Room"] = 2; state["players"][0]["Position"] = Point(9, 5); state["players"][1]["Room"] = 2; state["players"][1]["Position"] = Point(10, 5);
            state["rooms"][2]["Status"] = "active"; state["rooms"][2]["Wave"] = 2;
            state["enemies"] = new JArray(new JObject { ["ID"] = "defense_guard", ["Kind"] = "guard", ["Room"] = 2, ["Position"] = Point(14, 5), ["Origin"] = Point(14, 5), ["Health"] = 10000, ["Maximum"] = 10000, ["AttackAt"] = 100000 });
            world.Restore(state); JObject interaction = null;
            for (var elapsed = 33; elapsed <= 4200; elapsed += 33)
            {
                world.Input("1001", new GrayboxInput { Interact = true }); world.Input("1002", new GrayboxInput { Attack = true, Aim = 0 });
                interaction = world.Tick(elapsed).FirstOrDefault(fact => (string)fact["kind"] == "interact") ?? interaction;
            }
            Require(interaction != null && ((JArray)interaction["contributors"]).Any(id => (string)id == "1002"), "actual defense receives eligible credit");
            Require((string)world.Snapshot()["rooms"][2]["status"] == "active" && (bool)world.Snapshot()["rooms"][2]["mechanism_complete"], "mechanism does not bypass remaining enemy"); results.Add("parallel_interaction_and_defense");
            world = new GrayboxWorld(Manifest()); state = world.Capture(); state["players"][0]["Room"] = 2; state["players"][0]["Position"] = Point(3, 5); state["rooms"][2]["Status"] = "active"; state["rooms"][2]["Wave"] = 2;
            state["enemies"] = new JArray(new JObject { ["ID"] = "environment_guard", ["Kind"] = "guard", ["Room"] = 2, ["Position"] = Point(12, 3), ["Health"] = 20, ["Maximum"] = 20, ["WarningUntil"] = 10000, ["Warning"] = "melee" });
            state["traps"][1]["Phase"] = "active"; state["traps"][1]["Until"] = 1000; world.Restore(state);
            Require(world.Tick(33).All(fact => (string)fact["kind"] != "kill") && (int)world.Summary()["participants"][0]["kills"] == 0 && ((JArray)world.Snapshot()["enemies"]).Count == 0, "environment death has no player kill"); results.Add("environment_attribution");
            world = new GrayboxWorld(Manifest()); state = world.Capture(); state["safe_node"] = 0; state["safe_until"] = 20000; world.Restore(state);
            var candidates = (JArray)world.Snapshot()["players"][0]["candidates"]; Require(candidates.Count == 3, "three candidates");
            Require(world.Action("1001", "choose:" + candidates[0]) && !world.Action("1001", "choose:" + candidates[1]), "choice locks once"); world.Tick(33);
            Require(((JArray)world.Snapshot()["players"][0]["upgrades"]).Count == 1 && (int)world.Snapshot()["safe_node"] == -1, "choice applies and opens stage"); results.Add("choice_once");
            world = new GrayboxWorld(Manifest()); state = world.Capture();
            state["players"][0]["Room"] = 1; state["players"][0]["Position"] = Point(3, 3); state["rooms"][1]["Status"] = "active"; state["rooms"][1]["Wave"] = 2;
            state["enemies"] = new JArray(new JObject { ["ID"] = "waiting_archer", ["Kind"] = "archer", ["Room"] = 1, ["Position"] = Point(14, 3), ["Origin"] = Point(14, 3), ["Health"] = 60, ["Maximum"] = 60, ["WarningUntil"] = 6000, ["Warning"] = "shot", ["AttackAt"] = 20000, ["RecoveryCount"] = 2, ["StuckPosition"] = Point(14, 3), ["StuckAt"] = 0, ["Aim"] = Point(-1, 0) });
            world.Restore(state); world.Tick(5999); world.Tick(6000); world.Tick(6033);
            Require(!world.Terminal && (int)world.Capture()["enemies"][0]["RecoveryCount"] == 2, "attack warning time must not count as failed movement"); results.Add("attack_wait_not_stuck");
            var output = DcBootstrap.Argument("-dcQualityOutput", (DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"quality-simulation.json"); File.WriteAllText(output, new JObject { ["kind"] = "quality_authoritative_simulation", ["passed_groups"] = results.Count, ["cases"] = results, ["real_human_playtests"] = 0, ["database_rewards_mutated"] = false }.ToString());
            Debug.Log("DC quality checks passed: " + results.Count);
        }

        public static void Route()
        {
            var outcomes = new JArray();
            foreach (var scheme in new[] { "ranger", "runesmith" })
            {
                var manifest = Manifest(scheme); var world = new GrayboxWorld(manifest); QualityPilot.Configure((JObject)manifest["p1_world"]);
                for (var elapsed = 33; elapsed <= 1200033 && !world.Terminal; elapsed += 33)
                {
                    var snapshot = world.Snapshot(); var input = QualityPilot.Decide(snapshot, "1001"); input.Sequence = (uint)(elapsed / 33); input.HasPlacement = true; input.Placement = QualityPilot.Placement(snapshot, "1001"); world.AdvanceTime(elapsed); world.Input("1001", input);
                    var action = QualityPilot.Action(snapshot, "1001"); if (action != null) world.Action("1001", action, input);
                    world.Tick(elapsed);
                    var checkpoint = world.Capture();
                    if ((int)checkpoint["players"][0]["Health"] <= 0 && checkpoint["players"][0]["Reinforcement"].Type == JTokenType.Null)
                    {
                        var death = (int)checkpoint["players"][0]["Deaths"]; var actions = new JObject(); for (var count = 1; count <= Math.Min(death, 3); count++) actions["spawn_" + count] = new JObject { ["player_id"] = "1001", ["status"] = count == death ? "pending_spawn" : "spawned" }; world.Acknowledge(new JObject { ["reinforcements"] = actions });
                    }
                    else if (checkpoint["players"][0]["Reinforcement"].Type != JTokenType.Null && (bool)checkpoint["players"][0]["SpawnAwaitingAck"])
                    {
                        var actions = new JObject(); for (var count = 1; count <= (int)checkpoint["players"][0]["Deaths"]; count++) actions["spawn_" + count] = new JObject { ["player_id"] = "1001", ["status"] = "spawned" }; world.Acknowledge(new JObject { ["reinforcements"] = actions });
                    }
                }
                outcomes.Add(new JObject { ["scheme"] = scheme, ["outcome"] = world.Outcome, ["snapshot"] = world.Snapshot(), ["summary"] = world.Summary() });
            }
            File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"quality-routes.json", outcomes.ToString());
            foreach (var outcome in outcomes) Debug.Log("Quality route " + outcome["scheme"] + ": " + outcome["outcome"]);
        }

        private static JObject Point(float horizontal, float vertical) => new JObject { ["x"] = horizontal, ["y"] = vertical };
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
