using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private readonly List<JObject> p5Markers=new List<JObject>();
        private readonly Dictionary<string,long> p5MarkerTimes=new Dictionary<string,long>();
        private readonly HashSet<string> p5MarkerIDs=new HashSet<string>();
        private readonly Queue<string> p5MarkerOrder=new Queue<string>();
        private void SendP5Marker(Vector2 point)
        {
            if(p1Snapshot==null||!s1ClientBaselineReady||s1Paused||s1Waiting||p1Controls.Menu)return;
            var own=((JArray)p1Snapshot["players"]).First(item=>(string)item["player_id"]==playerID);
            Send(WireName("dc.marker.v5"),NetworkManager.ServerClientId,new JObject{["protocol_version"]=Protocol,["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["input_sequence"]=sentSequence,["marker_id"]=Guid.NewGuid().ToString(),["room_id"]=own["room_id"],["x"]=point.x,["y"]=point.y});
        }
        private void ReceiveP5Marker(ulong client,FastBufferReader reader)
        {
            try
            {
                if(!identities.TryGetValue(client,out var identity)||!Budget(actionRates,client,20)||p1World==null||!running||s1Paused||s1Waiting)return;
                var value=Read(reader,1024);RequireFields(value,"protocol_version","run_id","source_generation","connection_generation","input_sequence","marker_id","room_id","x","y");
                if(Integer(value,"protocol_version",Protocol,Protocol)!=Protocol||(string)value["run_id"]!=(string)assignment["run_id"]||Integer(value,"source_generation",1,9007199254740991)!=sourceGeneration||Integer(value,"connection_generation",1,uint.MaxValue)!=(long)identity["connection_generation"])return;
                var sequence=Integer(value,"input_sequence",1,uint.MaxValue);var latest=sequences.TryGetValue(client,out var current)?current:0;
                if(sequence+64<latest||sequence>latest+2)return;
                var id=(string)value["marker_id"];if(!Guid.TryParseExact(id,"D",out _)||p5MarkerIDs.Contains(id))return;
                var player=(string)identity["player_id"];var world=p1World.RuntimeSnapshot();var own=((JArray)world["players"]).First(item=>(string)item["player_id"]==player);var room=(int)own["room_id"];var game=(long)world["elapsed_ms"];
                if((int)own["health"]<=0||Integer(value,"room_id",0,15)!=room||p5MarkerTimes.TryGetValue(player,out var previous)&&game-previous<2000)return;
                if(value["x"].Type!=JTokenType.Float&&value["x"].Type!=JTokenType.Integer||value["y"].Type!=JTokenType.Float&&value["y"].Type!=JTokenType.Integer)return;
                var point=new Vector2((float)value["x"],(float)value["y"]);var layout=assignment["p1_world"]["rooms"][room];
                if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||point.x<1||point.y<1||point.x>(float)layout["width"]-1||point.y>(float)layout["height"]-1)return;
                p5Markers.RemoveAll(marker=>(long)marker["expires_game_ms"]<=game);
                if(p5Markers.Count(marker=>(string)marker["player_id"]==player)>=3)return;
                p5Markers.Add(new JObject{["id"]=id,["player_id"]=player,["room_id"]=room,["x"]=point.x,["y"]=point.y,["expires_game_ms"]=game+5000});p5MarkerTimes[player]=game;p5MarkerIDs.Add(id);p5MarkerOrder.Enqueue(id);while(p5MarkerOrder.Count>96)p5MarkerIDs.Remove(p5MarkerOrder.Dequeue());
                v5ActorSignature=null;v5NextActorState=0;
            }
            catch(Exception){network.DisconnectClient(client);}
        }
        private JArray VisibleP5Markers(int room,long game)
        {
            p5Markers.RemoveAll(marker=>(long)marker["expires_game_ms"]<=game||p3Left.Contains((string)marker["player_id"]));
            return new JArray(p5Markers.Where(marker=>(int)marker["room_id"]==room).Select(marker=>marker.DeepClone()));
        }
        internal async System.Threading.Tasks.Task<bool> ProbeP5Markers(string previousRun)
        {
            if(DcBootstrap.Argument("-dc-p5-lobby-probe","false")!="true"||!P5Loaded)return false;
            if(s1Paused){p1Controls.SetMenu(false);await S1PauseRequest(false,"explicit_resume");}
            p1Controls.SetMenu(false);await System.Threading.Tasks.Task.Delay(350,stopping.Token);
            var own=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);var point=new Vector2((float)own["x"],(float)own["y"]);
            if(!string.IsNullOrEmpty(previousRun))
            {
                Send(WireName("dc.marker.v5"),NetworkManager.ServerClientId,new JObject{["protocol_version"]=Protocol,["run_id"]=previousRun,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["input_sequence"]=sentSequence,["marker_id"]=Guid.NewGuid().ToString(),["room_id"]=own["room_id"],["x"]=point.x,["y"]=point.y});
                await System.Threading.Tasks.Task.Delay(200,stopping.Token);if((p1Snapshot["markers"] as JArray)?.Count>0)return false;
            }
            SendP5Marker(point);var deadline=Time.realtimeSinceStartupAsDouble+2;
            while(Time.realtimeSinceStartupAsDouble<deadline){if((p1Snapshot["markers"] as JArray)?.Any(marker=>(string)marker["player_id"]==playerID)==true)return true;await System.Threading.Tasks.Task.Delay(30,stopping.Token);}return false;
        }
    }

    public sealed partial class DcP1View
    {
        private readonly HashSet<string> p5MarkerSprites=new HashSet<string>();
        private void DrawP5Markers()
        {
            var present=new HashSet<string>();
            foreach(var marker in snapshot["markers"] as JArray??new JArray())
            {
                if((int)marker["room_id"]!=room||(long)marker["expires_game_ms"]<=(long)snapshot["elapsed_ms"])continue;
                var id="weapon:p5.marker:"+marker["id"];var point=Point(marker);present.Add(id);var renderer=Draw(id,Visible(point)?point:EdgePoint(point),DcP5Theme.Teal,Vector2.one*0.3f,15);renderer.sprite=DcP5Art.Get("icon.ward");
            }
            foreach(var id in p5MarkerSprites.Where(id=>!present.Contains(id)).ToArray())if(sprites.TryGetValue(id,out var renderer)){renderer.gameObject.SetActive(false);spritePool.Enqueue(renderer);sprites.Remove(id);}
            p5MarkerSprites.Clear();p5MarkerSprites.UnionWith(present);
        }
    }
}
