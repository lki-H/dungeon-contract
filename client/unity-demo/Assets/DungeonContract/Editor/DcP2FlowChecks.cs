using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcP2FlowChecks
    {
        public static void Run()
        {
            var parameters=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/config/parameters-v4-p2-1.json")));var geometry=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/config/world-v4-p2-1.json")));var results=new JArray();
            foreach(var loadout in new[]{"ranger","runesmith"})
            {
                for(var seed=20261012;seed<20261018;seed++)
                {
                var world=new DcP1World(new JObject{["parameters"]=parameters,["p1_world"]=geometry,["practice"]=false,["p2_stage"]=true,["weapon_stage"]=true,["seed"]=seed,["run_id"]="input_only_flow",["members"]=new JArray(new JObject{["player_id"]="1",["slot"]=0,["loadout_id"]=loadout})});
                var watch=System.Diagnostics.Stopwatch.StartNew();world.Tick(0);var snapshot=world.Snapshot();var nextPrimary=0L;var nextSecondary=0L;
                for(var frame=1;frame<=27000&&!world.Terminal;frame++)
                {
                    var game=frame*1000L/30;var input=DcP21Pilot.Decide(snapshot,world.Definition,(uint)frame);world.AdvanceTime(game);
                    if((int)snapshot["players"][0]["room_id"]<8&&((JArray)snapshot["enemies"]).Count>0)
                    {
                        if(game>=nextPrimary){world.Action("1","ability_primary",input);nextPrimary=game+200;}
                        if(game>=nextSecondary){world.Action("1","ability_secondary",input);nextSecondary=game+200;}
                        if(DcP21Pilot.Dodge(snapshot))world.Action("1","dodge",input);
                    }
                    world.Input("1",input);world.Tick(game);snapshot=world.Snapshot();
                    if(frame%3000==0)Debug.Log("DC input flow "+loadout+" game="+game+" room="+snapshot["players"][0]["room_id"]+" health="+snapshot["players"][0]["health"]+" damage="+snapshot["players"][0]["damage"]);
                }
                var result=new JObject{["loadout"]=loadout,["outcome"]=world.Outcome,["reason"]=snapshot["reason"],["elapsed_ms"]=world.GameTime,["room"]=snapshot["players"][0]["room_id"],["health"]=snapshot["players"][0]["health"],["damage"]=snapshot["players"][0]["damage"],["completed"]=snapshot["completed"],["host_compute_ms"]=watch.ElapsedMilliseconds,["input_only"]=true,["mutated_state"]=false,["real_udp"]=false,["human"]=false};results.Add(result);Debug.Log("DC input flow result "+result.ToString(Newtonsoft.Json.Formatting.None));
                File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"p2-flow-final-"+loadout+".json",snapshot.ToString());
                result["seed"]=seed;File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"p2-flow-"+loadout+"-"+seed+".json",result.ToString());if(world.Outcome=="success")break;
                }
            }
            File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"p2-flow-model.json",new JObject{["kind"]="unmodified_configuration_input_only_full_flow",["results"]=results,["success_loadouts"]=results.Count(item=>(string)item["outcome"]=="success"),["human"]=false,["real_udp"]=false}.ToString());
            if(new[]{"ranger","runesmith"}.Any(loadout=>!results.Any(item=>(string)item["loadout"]==loadout&&(string)item["outcome"]=="success")))throw new InvalidOperationException("Input-only success evidence missing for a loadout; failed attempts retained");
        }
    }
}
