using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcGrayboxChecks
    {
        private static JObject Manifest(string loadout)
        {
            return new JObject { ["run_id"] = "run_unit_graybox", ["config"] = JObject.Parse(File.ReadAllText("../../docs/game-demo/config/demo-v1.json")), ["p1_world"] = JObject.Parse(File.ReadAllText("../../docs/game-demo/config/p1-world.v1.json")), ["members"] = new JArray(new JObject { ["player_id"] = "1001", ["slot"] = 0, ["loadout_id"] = loadout }), ["task_requirements"] = new JObject { ["1001"] = new JObject { ["damage:"] = 3, ["kill:"] = 8 } } };
        }

        public static void Run()
        {
            var path = DcBootstrap.Argument("-dcCheckOutput");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("-dcCheckOutput required");
            var report = new JObject { ["kind"] = "pure_authoritative_simulation_checks", ["database_rewards_mutated"] = false, ["cases"] = new JArray() };
            var world = new GrayboxWorld(Manifest("ranger"));
            Require(world.Blocked(new Vector2(32, 6), 0.3f), "locked stage blocks movement");
            Require(world.Blocked(new Vector2(16, 3), 0.3f), "room wall blocks movement");
            Require(!world.Blocked(new Vector2(16, 6), 0.3f), "unlocked door permits passage");
            Require(world.Action("1001", "dodge") && !world.Action("1001", "dodge"), "dodge cooldown rejects second action");
            Require(world.Action("1001", "ability") && !world.Action("1001", "ability"), "ability cooldown rejects second action");
            var initial = world.Capture();
            world.Input("1001", new GrayboxInput { Move = Vector2.right }); world.Tick(33);
            world.Restore(initial);
            Require(JToken.DeepEquals(world.Capture(), initial), "checkpoint restores complete world");
            world.Presence("1001", false);
            Require(!world.Action("1001", "ability"), "disconnected actions denied");
            ((JArray)report["cases"]).Add(new JObject { ["case"] = "walls_cooldowns_checkpoint_presence", ["passed"] = true });
            world = new GrayboxWorld(Manifest("ranger"));
            var testState = world.Capture();
            testState["players"][0]["Position"] = new JObject { ["x"] = 51, ["y"] = 3 };
            testState["players"][0]["Health"] = 50; testState["maximum_room"] = 3;
            world.Restore(testState);
            Require(world.Action("1001", "supply") && !world.Action("1001", "supply"), "supply once per player");
            Require((int)world.Snapshot()["players"][0]["health"] == 80, "supply amount comes from configuration");
            testState = world.Capture(); testState["players"][0]["Position"] = new JObject { ["x"] = 40, ["y"] = 6 }; testState["players"][0]["Health"] = 100; testState["waves"][2] = 1; testState["entered"][2] = true;
            world.Restore(testState);
            for (var time = 100; time < 2000; time += 34) { world.Input("1001", new GrayboxInput { Interact = true }); world.Tick(time); }
            world.Input("1001", new GrayboxInput { Move = Vector2.right }); world.Tick(2000);
            Require((long)world.Snapshot()["players"][0]["interaction_remaining_ms"] == 0, "moving cancels incomplete interaction");
            ((JArray)report["cases"]).Add(new JObject { ["case"] = "supply_and_interaction_interrupt", ["passed"] = true });
            world = new GrayboxWorld(Manifest("ranger"));
            Require(world.Action("1001", "ability"), "ability can start before respawn fixture");
            testState = world.Capture(); testState["players"][0]["Health"] = 0; testState["players"][0]["Deaths"] = 1;
            world.Restore(testState);
            world.Acknowledge(new JObject { ["reinforcements"] = new JObject { ["test_spawn_01"] = new JObject { ["player_id"] = "1001", ["status"] = "pending_spawn" } } });
            Require(world.Tick(2000).All(item => (string)item["kind"] != "reinforcement_spawned"), "respawn waits its configured delay");
            Require(world.Tick(3000).Any(item => (string)item["kind"] == "reinforcement_spawned"), "Go reservation causes one actual spawn");
            world.Acknowledge(new JObject { ["reinforcements"] = new JObject { ["test_spawn_01"] = new JObject { ["player_id"] = "1001", ["status"] = "spawned" } } });
            Require(!world.Action("1001", "ability"), "spawn protection blocks ability");
            world.Tick(4100);
            Require(!world.Action("1001", "ability"), "respawn does not refresh ability cooldown");
            ((JArray)report["cases"]).Add(new JObject { ["case"] = "go_reserved_respawn_protection_and_cooldowns", ["passed"] = true });
            world = new GrayboxWorld(Manifest("ranger")); testState = world.Capture();
            testState["players"][0]["Position"] = new JObject { ["x"] = 87, ["y"] = 6 }; testState["players"][0]["Health"] = 1;
            testState["maximum_room"] = 5; testState["entered"][5] = true;
            for (var room = 1; room < 5; room++) testState["completed"][room] = true;
            testState["enemies"] = new JArray(new JObject { ["ID"] = "boss", ["Kind"] = "boss", ["Position"] = new JObject { ["x"] = 88, ["y"] = 6 }, ["Room"] = 5, ["Health"] = 1, ["Maximum"] = 1, ["FanAt"] = 5000, ["StompAt"] = 7000, ["SummonAt"] = 25000 });
            testState["projectiles"] = new JArray(
                new JObject { ["ID"] = "enemy_final", ["Owner"] = "boss", ["Position"] = new JObject { ["x"] = 87, ["y"] = 6 }, ["Direction"] = new JObject { ["x"] = 1, ["y"] = 0 }, ["Speed"] = 1, ["Remaining"] = 1, ["Damage"] = 1, ["Enemy"] = true },
                new JObject { ["ID"] = "player_final", ["Action"] = "last_attack", ["Owner"] = "1001", ["Position"] = new JObject { ["x"] = 88, ["y"] = 6 }, ["Direction"] = new JObject { ["x"] = 1, ["y"] = 0 }, ["Speed"] = 1, ["Remaining"] = 1, ["Damage"] = 1, ["Enemy"] = false });
            world.Restore(testState); var finalFacts = world.Tick(1000);
            Require(world.Outcome == "success" && finalFacts.Any(item => (string)item["kind"] == "death") && finalFacts.Any(item => (string)item["kind"] == "kill" && (string)item["target_id"] == "boss"), "same-tick success retains real death");
            Require((int)world.Summary()["participants"][0]["deaths"] == 1, "no-death condition receives the final death");
            ((JArray)report["cases"]).Add(new JObject { ["case"] = "last_projectile_success_and_death", ["passed"] = true });
            world = new GrayboxWorld(Manifest("ranger")); testState = world.Capture();
            testState["players"][0]["Position"] = new JObject { ["x"] = 4, ["y"] = 6 };
            testState["enemies"] = new JArray(new JObject { ["ID"] = "range_guard", ["Kind"] = "guard", ["Position"] = new JObject { ["x"] = 6, ["y"] = 6 }, ["Room"] = 1, ["Health"] = 100, ["Maximum"] = 100, ["AttackAt"] = 10000 });
            testState["projectiles"] = new JArray(new JObject { ["ID"] = "short_range", ["Action"] = "range_action", ["Owner"] = "1001", ["Position"] = new JObject { ["x"] = 5, ["y"] = 6 }, ["Direction"] = new JObject { ["x"] = 1, ["y"] = 0 }, ["Speed"] = 100, ["Remaining"] = 0.1, ["Damage"] = 10, ["Enemy"] = false });
            world.Restore(testState); world.Tick(1000);
            Require((long)world.Summary()["participants"][0]["damage"] == 0, "projectile cannot damage beyond remaining range");
            Require(((JArray)world.Capture()["sampled"]).Count == 0, "expired projectile action samples are released");
            testState = world.Capture();
            world.Acknowledge(new JObject { ["task_requirements"] = new JObject { ["1001"] = new JObject() } });
            world.Restore(testState);
            Require(JToken.DeepEquals(world.Capture(), testState), "checkpoint restores task requirements after receipt updates");
            world.Leave("1001"); world.Tick(1034);
            Require(world.Outcome == "failed" && (string)world.TerminalRequest(1, 1)["reason"] == "team_abandoned", "last-player exit is a gameplay terminal");
            ((JArray)report["cases"]).Add(new JObject { ["case"] = "projectile_range_sample_budget_task_checkpoint_and_exit", ["passed"] = true });
            foreach (var loadout in new[] { "ranger", "runesmith" })
            {
                world = new GrayboxWorld(Manifest(loadout));
                var actions = new JObject();
                var deaths = 0;
                var killFacts = 0;
                long time;
                for (time = 0; time < 1200000 && !world.Terminal; time += 34)
                {
                    var snapshot = world.Snapshot();
                    world.Input("1001", GrayboxPilot.Decide(snapshot, "1001"));
                    var action = GrayboxPilot.Action(snapshot, "1001"); if (action != null) world.Action("1001", action);
                    var facts = world.Tick(time);
                    foreach (var fact in facts)
                    {
                        if ((string)fact["kind"] == "kill") killFacts++;
                        if ((string)fact["kind"] == "death")
                        {
                            deaths++;
                            if (deaths <= 3) actions["pure_reinforcement_" + deaths] = new JObject { ["player_id"] = "1001", ["status"] = "pending_spawn" };
                        }
                        if ((string)fact["kind"] == "reinforcement_spawned") actions[(string)fact["reinforcement_action_id"]]["status"] = "spawned";
                    }
                    if (facts.Count > 0) world.Acknowledge(new JObject { ["reinforcements"] = actions });
                }
                var outcome = world.Outcome ?? "simulation_timeout";
                ((JArray)report["cases"]).Add(new JObject { ["case"] = "full_pilot_route", ["loadout"] = loadout, ["outcome"] = outcome, ["elapsed_ms"] = time, ["deaths"] = deaths, ["sampled_kill_facts"] = killFacts, ["summary"] = world.Summary(), ["last_snapshot"] = world.Snapshot() });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString(Formatting.Indented));
            if (((JArray)report["cases"]).Any(item => item["case"]?.ToString() == "full_pilot_route" && (string)item["outcome"] != "success")) throw new InvalidOperationException("A loadout failed authoritative route; inspect recorded evidence");
            Debug.Log("P1 simulation checks recorded; pilot results require inspection: " + path);
        }

        private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("P1 assertion: " + name); }
    }
}
