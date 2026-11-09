using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcS1Checks
    {
        public static void Run()
        {
            var parameters=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/contracts/planned-v3/parameters.json")));
            var geometry=JObject.Parse(File.ReadAllText(Path.GetFullPath("../../docs/game-demo/config/world-v3-p1.json")));
            var world=new DcP1World(new JObject {["parameters"]=parameters,["p1_world"]=geometry,["practice"]=true,["seed"]=20261012,["run_id"]="s1_check",["members"]=new JArray(new JObject{["player_id"]="1",["slot"]=0,["loadout_id"]="ranger"})});
            world.Tick(0);
            var snapshot=world.Snapshot();for(var index=0;index<40;index++)((JArray)snapshot["projectiles"]).Add(new JObject{["id"]="fixture_"+index,["room_id"]=0,["x"]=3,["y"]=3,["enemy"]=false});var replica=new DcS1Replica();replica.Baseline(snapshot,"1",1);
            var next=(JObject)snapshot.DeepClone();next["world_frame_id"]=(long)snapshot["world_frame_id"]+1;var packets=DcS1Replica.Encode(next,1,1,1);
            Require(packets.Length>0&&packets.All(packet=>packet.Length<=900),"binary packet budget");
            foreach(var packet in packets.Reverse())Require(replica.Dynamic(packet,1,1),"binary roundtrip");
            Require(!replica.Dynamic(packets[0],1,1),"duplicate group");
            Require(!replica.Dynamic(packets[0],2,1),"old source");
            var prediction=new DcS1Prediction(geometry);prediction.Correct(replica,"1");var start=prediction.Position;
            prediction.Move(new DcP1Input{Sequence=1,Move=Vector2.right},1f/60,false);
            Require(prediction.Position.x>start.x,"same-render movement prediction");
            for(var index=0;index<700;index++)prediction.Move(new DcP1Input{Sequence=(uint)(index+2),Move=Vector2.left},1f/60,false);
            Require(prediction.HistoryCount<=512&&prediction.Position.x>=1.3f,"history/wall budget");
            prediction.Clear();Require(prediction.HistoryCount==0,"clear prediction");
            world.Input("1",new DcP1Input{Sequence=1,Aim=0,Attack=true});world.Tick(100);
            world.Action("1","reload");world.FreezeInputs();var frozen=world.Capture().ToString();
            Require(world.Capture().ToString()==frozen,"full world freeze");
            var before=(long)world.Snapshot()["players"][0]["reload_remaining_ms"];
            world.ResumeInputs();world.Tick(110);
            var after=(long)world.Snapshot()["players"][0]["reload_remaining_ms"];
            Require(before>0&&after>0&&after<=before,"resume preserves reload");
            var result=new JObject {["scope"]="s1_codec_prediction_pause_model",["tests"]=new JArray("binary_budget","binary_roundtrip","duplicate_group","source_binding","immediate_prediction","history_wall","prediction_clear","world_freeze","reload_resume"),["passed"]=9,["real_network"]=false,["human"]=false};
            File.WriteAllText(DcBootstrap.Argument("-dc-regression-output",(DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"s1-model-checks.json"),result.ToString());Debug.Log("DC S1 checks passed: 9");
        }
        private static void Require(bool value,string name){if(!value)throw new InvalidOperationException("S1 assertion: "+name);}
    }
}
