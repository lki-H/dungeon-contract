using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcP2PressureChecks
    {
        public static void Run()
        {
            var parameters=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/config/parameters-v4-p2-1.json")));var geometry=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/config/world-v4-p2-1.json")));var results=new JArray();
            foreach(var room in new[]{1,5})
            {
                var members=new JArray(Enumerable.Range(0,4).Select(index=>new JObject{["player_id"]=(index+1).ToString(),["slot"]=index,["loadout_id"]=index%2==0?"ranger":"runesmith"}));
                var world=new DcP1World(new JObject{["parameters"]=parameters,["p1_world"]=geometry,["p2_stage"]=true,["weapon_stage"]=true,["members"]=members,["run_id"]="isolated_pressure_fixture",["seed"]=20261012});
                var practice=new DcP1World(new JObject{["parameters"]=parameters,["p1_world"]=geometry,["p2_stage"]=true,["weapon_stage"]=true,["members"]=members,["practice"]=true,["run_id"]="fixture_template",["seed"]=20261012});
                var initial=world.Capture();var template=(JObject)practice.Capture()["enemies"][0];initial["rooms"][room]["Status"]="active";initial["rooms"][room]["Wave"]=room==5?1:3;initial["covers"]=new JArray();initial["traps"]=new JArray();initial["enemies"]=new JArray();initial["projectiles"]=new JArray();
                foreach(var player in initial["players"]){player["Room"]=room;player["Position"]=Point(3+(int)player["Slot"]%2,6+(int)player["Slot"]/2);}
                for(var index=0;index<20;index++){var enemy=(JObject)template.DeepClone();enemy["ID"]="pressure_enemy_"+index;enemy["Room"]=room;enemy["Kind"]=room==5&&index==0?"boss":index%3==0?"archer":"guard";enemy["Position"]=Point(12+index%5*2,4+index/5*2);enemy["Origin"]=enemy["Position"].DeepClone();enemy["ReadyAt"]=0;enemy["AttackAt"]=0;((JArray)initial["enemies"]).Add(enemy);}
                for(var index=0;index<128;index++)((JArray)initial["projectiles"]).Add(new JObject{["ID"]="pressure_flight_"+index,["Action"]="pressure_action_"+index,["Owner"]="1",["Room"]=room,["Position"]=Point(27,3+index%10),["Direction"]=Point(-1,0),["Speed"]=10,["Damage"]=16,["Unbounded"]=true,["Enemy"]=false,["Hit"]=new JArray(),["CollisionRadius"]=0.1f});
                var samples=new System.Collections.Generic.List<double>();var initialGC=GC.CollectionCount(0);var maximumFlight=0;
                for(var window=0;window<20;window++)
                {
                    world.Restore(initial);for(var frame=1;frame<=30;frame++){var watch=System.Diagnostics.Stopwatch.GetTimestamp();world.Tick(frame*1000L/30);var value=world.RuntimeSnapshot();samples.Add((System.Diagnostics.Stopwatch.GetTimestamp()-watch)*1000d/System.Diagnostics.Stopwatch.Frequency);maximumFlight=Math.Max(maximumFlight,(int)value["projectile_load"]);if(maximumFlight>128)throw new InvalidOperationException("Pressure fixture exceeded live plus reserved budget");}
                }
                var sorted=samples.OrderBy(item=>item).ToArray();results.Add(new JObject{["room_kind"]=room==5?"boss":"ordinary",["frozen_players"]=4,["initial_enemies"]=20,["initial_projectiles"]=128,["samples"]=samples.Count,["median_tick_and_snapshot_ms"]=sorted[sorted.Length/2],["p95_ms"]=sorted[(int)(sorted.Length*0.95)],["p99_ms"]=sorted[(int)(sorted.Length*0.99)],["max_ms"]=sorted.Last(),["gc0"]=GC.CollectionCount(0)-initialGC,["max_projectile_load"]=maximumFlight,["tick_budget_33ms_met"]=sorted[(int)(sorted.Length*0.99)]<=33.333});
            }
            File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"p2-authority-pressure.json",new JObject{["scope"]="isolated_modified_state_cpu_pressure_fixture",["cpu"]=SystemInfo.processorType,["configuration_modified"]=false,["runtime_fixture_modified"]=true,["real_udp"]=false,["renderer_fps"]=null,["human"]=false,["results"]=results}.ToString());Debug.Log("DC authority pressure fixture completed");
        }
        private static JObject Point(float x,float y)=>new JObject{["x"]=x,["y"]=y};
    }
}
