using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcP5AuthorityPressure
    {
        public static void Run()
        {
            if(DcV7Profile.Active)throw new InvalidOperationException("Use the v7 standalone legal-body/flight fixture, not the inherited Editor fixture.");
            var parameters=JObject.Parse(File.ReadAllText(DcV7Profile.Active?"../../docs/game-demo/config/parameters-v7-p5.json":DcV6Profile.Active?"../../docs/game-demo/config/parameters-v6-p5.json":"../../docs/game-demo/config/parameters-v5-p4.json"));var geometry=JObject.Parse(File.ReadAllText(DcV7Profile.Active?"../../docs/game-demo/config/world-v7-p5.json":DcV6Profile.Active?"../../docs/game-demo/config/world-v6-p5.json":"../../docs/game-demo/config/world-v5-p4.json"));var config=JObject.Parse(File.ReadAllText(DcV7Profile.Active?"../../docs/game-demo/config/demo-v7-p5.json":DcV6Profile.Active?"../../docs/game-demo/config/demo-v6-p5.json":"../../docs/game-demo/config/demo-v5-p5.json"));var results=new JArray();
            foreach(var kit in new[]{"ranger","runesmith"})
            {
                var members=new JArray(Enumerable.Range(0,4).Select(index=>new JObject{["player_id"]=(index+1).ToString(),["slot"]=index,["loadout_id"]=kit}));
                var manifest=new JObject{["run_id"]="isolated_p5_pressure",["seed"]=20261012,["parameters"]=parameters,["p1_world"]=geometry,["config"]=config,["p2_stage"]=true,["p3_stage"]=true,["p4_stage"]=true,["weapon_stage"]=true,["members"]=members};
                var world=new DcP1World(manifest);var practiceManifest=(JObject)manifest.DeepClone();practiceManifest["practice"]=true;var template=new DcP1World(practiceManifest).Capture();var enemyTemplate=(JObject)template["enemies"][0].DeepClone();var state=world.Capture();
                foreach(var player in state["players"]){var slot=(int)player["Slot"];player["Room"]=slot==0?1:5;player["Position"]=Point(4+slot,8);player["InvulnerableUntil"]=long.MaxValue;player["Upgrades"]=kit=="ranger"?new JArray("ranger_fan","ranger_pierce","frost"):new JArray("runesmith_twin","runesmith_heavy","runesmith_chain");}
                foreach(var room in new[]{1,5}){state["rooms"][room]["Status"]="active";state["rooms"][room]["Wave"]=1;}
                state["enemies"]=new JArray();state["projectiles"]=new JArray();state["turrets"]=new JArray();state["spawn_reservations"]=new JArray();state["serial"]=10000;
                for(var index=0;index<24;index++){var enemy=(JObject)enemyTemplate.DeepClone();enemy["ID"]=index==0?"boss":"pressure_enemy_"+index;enemy["Kind"]=index==0?"boss":index%6==0?"elite":index%3==0?"archer":index%3==1?"guard":"charger";enemy["Room"]=index<13?5:1;enemy["Summon"]=index>0&&index<13;enemy["Position"]=Point(10+index%4*2,4+index/4%3*3);enemy["Origin"]=enemy["Position"].DeepClone();enemy["Health"]=50000;enemy["Maximum"]=100000;enemy["ReadyAt"]=0;enemy["AttackAt"]=0;((JArray)state["enemies"]).Add(enemy);}
                for(var index=0;index<1024;index++)((JArray)state["projectiles"]).Add(new JObject{["ID"]="projectile_"+(index+1).ToString("D6"),["Root"]="pressure_"+index,["Action"]="projectile_"+(index+1).ToString("D6"),["Owner"]="1",["Room"]=index%2==0?5:1,["Position"]=Point(27,3+index%10),["Direction"]=Point(-1,0),["Speed"]=5,["Damage"]=12,["Unbounded"]=true,["Enemy"]=false,["Hit"]=new JArray(),["CollisionRadius"]=0.1f,["NetworkGroup"]=index%43,["SourceLife"]=1});
                var samples=new List<double>();var details=new List<JObject>();var initialGC=GC.CollectionCount(0);var maximum=0;var initialAllocated=GC.GetAllocatedBytesForCurrentThread();
                for(var window=0;window<9;window++)
                {
                    world.Restore(state);
                    for(var tick=1;tick<=30;tick++)
                    {
                        var start=System.Diagnostics.Stopwatch.GetTimestamp();if(tick%8==0)world.CaptureRuntimeCheckpoint();var captured=System.Diagnostics.Stopwatch.GetTimestamp();world.Tick((tick*1000+29)/30);var simulated=System.Diagnostics.Stopwatch.GetTimestamp();var snapshot=world.RuntimeSnapshot();var ended=System.Diagnostics.Stopwatch.GetTimestamp();var factor=1000d/System.Diagnostics.Stopwatch.Frequency;var elapsed=(ended-start)*factor;if(window>0){samples.Add(elapsed);details.Add(new JObject{["tick"]=tick,["total_ms"]=elapsed,["checkpoint_ms"]=(captured-start)*factor,["simulation_ms"]=(simulated-captured)*factor,["snapshot_ms"]=(ended-simulated)*factor});}maximum=Math.Max(maximum,(int)snapshot["projectile_load"]);if(maximum>1024)throw new InvalidOperationException("P5 capacity exceeded");
                    }
                }
                samples.Sort();var allocation=GC.GetAllocatedBytesForCurrentThread()-initialAllocated;results.Add(new JObject{["loadout"]=kit,["players"]=4,["initial_enemies"]=24,["boss_room_entities"]=13,["initial_projectiles"]=1024,["checkpoint_kind"]="typed_projectiles_and_json_control_state",["checkpoint_every_ticks"]=8,["warmup_ticks"]=30,["samples"]=samples.Count,["median_ms"]=samples[samples.Count/2],["p95_ms"]=samples[(int)(samples.Count*0.95)],["p99_ms"]=samples[(int)(samples.Count*0.99)],["maximum_ms"]=samples.Last(),["slowest_ticks"]=new JArray(details.OrderByDescending(item=>(double)item["total_ms"]).Take(8)),["gc0_collections"]=GC.CollectionCount(0)-initialGC,["main_thread_allocated_bytes"]=allocation>0?new JValue(allocation):JValue.CreateNull(),["maximum_projectile_load"]=maximum,["p99_tick_budget_met"]=samples[(int)(samples.Count*0.99)]<=33.333});
            }
            File.WriteAllText(DcV7Profile.Active?(DcBootstrap.DemoEvidenceRoot+"/v7-authority-editor-pressure.json"):(DcBootstrap.DemoEvidenceRoot+"/v6-authority-pressure.json"),new JObject{["scope"]="isolated_modified_state_tick_snapshot_checkpoint_pressure",["modified_world_state"]=true,["real_udp"]=false,["human"]=false,["renderer_fps"]=null,["cpu"]=SystemInfo.processorType,["results"]=results}.ToString());Debug.Log("P5 authority pressure samples recorded");
        }
        private static JObject Point(float horizontal,float vertical)=>new JObject{["x"]=horizontal,["y"]=vertical};
    }
}
