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
        private readonly DcV5BaselineTracker v5BaselineRooms=new DcV5BaselineTracker();
        private double v5NextActorState;
        private string v5ActorSignature;
        private long v5ActorFrame=-1;
        private long v5ReliableJsonSentBytes;
        private readonly Dictionary<ulong,Queue<double>> v5AckRates=new Dictionary<ulong,Queue<double>>();
        private readonly Dictionary<ulong,DcV8ActorDelta> v8ActorDeltas=new Dictionary<ulong,DcV8ActorDelta>();
        private readonly Queue<(JObject value,int bytes)> v8PendingActorStates=new Queue<(JObject,int)>();
        private int v8PendingActorBytes;

        private bool CompleteV5NetworkBaseline(ulong client,JObject visible,string player)
        {
            var room=(int)((JArray)visible["players"]).First(item=>(string)item["player_id"]==player)["room_id"];
            var complete=v5BaselineRooms.Complete(client,room)||s1Paused||s1Waiting||p1ResultConfirmed;
            if(complete)v5BaselineRooms.Sent(client,room,(int)visible["state_revision"]);return complete;
        }
        private void V5BaselineAck(ulong connection,FastBufferReader reader)
        {
            try
            {
                if(!identities.ContainsKey(connection)||!Budget(v5AckRates,connection,60)){network.DisconnectClient(connection);return;}
                var value=Read(reader,1024);RequireFields(value,"protocol_version","run_id","source_generation","connection_generation","state_revision");
                if(!identities.TryGetValue(connection,out var identity)||Integer(value,"protocol_version",Protocol,Protocol)!=Protocol||(string)value["run_id"]!=(string)assignment["run_id"]||Integer(value,"source_generation",1,9007199254740991)!=sourceGeneration||Integer(value,"connection_generation",1,uint.MaxValue)!=(long)identity["connection_generation"])return;
                v5BaselineRooms.Acknowledge(connection,(int)Integer(value,"state_revision",1,int.MaxValue));
                if(DcV6Profile.Active&&v6Streams.TryGetValue(connection,out var stream)&&stream.AcknowledgeBaseline((int)value["state_revision"])){s1EntitySignature=null;s1NextState=0;}
            }
            catch(Exception error){RecordProbeNetwork("server.baseline_ack:"+connection,error);network.DisconnectClient(connection,"baseline_ack_invalid");}
        }

        private void SendV5ActorState(JObject snapshot)
        {
            foreach(var peer in v8ActorDeltas.Keys.Where(peer=>!identities.ContainsKey(peer)).ToArray())v8ActorDeltas.Remove(peer);
            var signature=string.Join("|",((JArray)snapshot["enemies"]).Select(item=>item["id"]+":"+item["warning"]+":"+item["phase"]+":"+item["barrage_active"]).Concat(((JArray)snapshot["players"]).Select(item=>item["player_id"]+":"+item["life_state"]+":"+item["active_weapon_id"]+":"+(item["melee_action"] is JObject melee?(string)melee["phase"]:""))));
            if(signature==v5ActorSignature&&Time.realtimeSinceStartupAsDouble<v5NextActorState)return;
            v5ActorSignature=signature;v5NextActorState=Time.realtimeSinceStartupAsDouble+0.1;
            foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)))
            {
                if(DcV6Profile.Active&&v6Streams.TryGetValue(identity.Key,out var waiting)&&waiting.AwaitingBaseline)continue;
                var visible=DcP21Interest.ForPlayer(snapshot,(string)identity.Value["player_id"],true);
                if(DcV6Profile.P5((string)configuration["definition_id"]))visible["markers"]=VisibleP5Markers((int)((JArray)visible["players"]).First(item=>(string)item["player_id"]==(string)identity.Value["player_id"])["room_id"],(long)snapshot["elapsed_ms"]);
                visible.Remove("projectiles");visible.Remove("covers");visible.Remove("turrets");
                // These messages merge into a revision-bound baseline. Repeating all
                // unchanged actor properties fills the reliable transport under jitter.
                if(DcV8Profile.Active){if(!v8ActorDeltas.TryGetValue(identity.Key,out var delta))v8ActorDeltas[identity.Key]=delta=new DcV8ActorDelta();visible=delta.Encode(visible,s1StateRevision);}
                var value=new JObject{["protocol_version"]=Protocol,["run_id"]=assignment["run_id"],["source_generation"]=sourceGeneration,["connection_generation"]=identity.Value["connection_generation"],["state_revision"]=s1StateRevision,["frame"]=p1World.WorldTick,["state"]=visible};
                Send(WireName("dc.actor-state.v5"),identity.Key,value,NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void V5ActorState(ulong connection,FastBufferReader reader)
        {
            if(connection!=NetworkManager.ServerClientId)return;
            var value=Read(reader,65536);
            if(!V5ActorBinding(value))return;
            var state=(JObject)value["state"];
            if(((JArray)state["enemies"]).Count>24||((JArray)state["players"]).Count>4||((JArray)state["spawn_reservations"]).Count>24)return;
            if(DcV8Profile.Active&&(int)value["state_revision"]>s1Replica.Revision){
                // Actor deltas can overtake a chunked baseline. Keep every delta
                // until that revision arrives; a later delta omits prior changes.
                var bytes=System.Text.Encoding.UTF8.GetByteCount(value.ToString(Newtonsoft.Json.Formatting.None));
                if(v8PendingActorStates.Count>=32||v8PendingActorBytes+bytes>262144){ClearV8ActorStates();s1Replica.RequireV6Baseline();return;}
                v8PendingActorStates.Enqueue((value,bytes));v8PendingActorBytes+=bytes;return;
            }
            ApplyV5ActorState(value);
        }
        private bool V5ActorBinding(JObject value)=>(int)value["protocol_version"]==Protocol&&(string)value["run_id"]==runID&&(long)value["source_generation"]==sourceGeneration&&(long)value["connection_generation"]==connectionGeneration;
        private void ClearV8ActorStates(){v8PendingActorStates.Clear();v8PendingActorBytes=0;}
        private void FlushV8ActorStates(){
            var count=v8PendingActorStates.Count;
            while(count-->0){var pending=v8PendingActorStates.Dequeue();v8PendingActorBytes-=pending.bytes;
                if(!V5ActorBinding(pending.value))continue;
                if((int)pending.value["state_revision"]>s1Replica.Revision){v8PendingActorStates.Enqueue(pending);v8PendingActorBytes+=pending.bytes;continue;}
                ApplyV5ActorState(pending.value);
            }
        }
        private void ApplyV5ActorState(JObject value)
        {
            if((int)value["state_revision"]!=s1Replica.Revision||(long)value["frame"]<=v5ActorFrame||p1Snapshot==null)return;
            v5ActorFrame=(long)value["frame"];var state=(JObject)value["state"];
            foreach(var group in new[]{"players","enemies"})foreach(var incoming in (JArray)state[group])
            {
                var key=group=="players"?"player_id":"id";var target=((JArray)p1Snapshot[group]).FirstOrDefault(item=>(string)item[key]==(string)incoming[key]) as JObject;
                if(target==null)continue;
                if(group=="players"&&(string)incoming["player_id"]==playerID&&(uint)incoming["weapon_revision"]<p1Controls.WeaponRevision)continue;
                foreach(var property in ((JObject)incoming).Properties())if(property.Name!="x"&&property.Name!="y"&&property.Name!="last_input_sequence"&&property.Name!="health"&&property.Name!="maximum")target[property.Name]=property.Value.DeepClone();
            }
            foreach(var group in new[]{"rooms","spawn_reservations","effects","fields","traps","team_rooms","markers"})if(state[group]!=null)p1Snapshot[group]=state[group].DeepClone();
            p1Snapshot["reinforcement_remaining"]=state["reinforcement_remaining"];if(DcV6Profile.Active)p1Snapshot["dynamic_elapsed_ms"]=Math.Max((long?)p1Snapshot["dynamic_elapsed_ms"]??0,(long)state["elapsed_ms"]);
        }
    }

    // State replacement is coalescible; projectile lifecycle and combat facts are not.
    // Existing clients already merge actor properties and optional top-level groups.
    public sealed class DcV8ActorDelta
    {
        private JObject previous;
        private int revision;
        private static bool PositionOnly(string key)=>key=="x"||key=="y"||key=="last_input_sequence"||key=="health"||key=="maximum";
        public JObject Encode(JObject current,int currentRevision)
        {
            var prior=revision==currentRevision?previous:null;
            var result=new JObject();
            foreach(var group in new[]{"players","enemies"}){
                var key=group=="players"?"player_id":"id";
                var old=((JArray)prior?[group]??new JArray()).ToDictionary(row=>(string)row[key]);
                var changes=new JArray();
                foreach(var row in ((JArray)current[group]).Cast<JObject>()){
                    old.TryGetValue((string)row[key],out var before);var changed=new JObject{[key]=row[key].DeepClone()};
                    foreach(var property in row.Properties())if(property.Name!=key&&!PositionOnly(property.Name)&&(before==null||!JToken.DeepEquals(property.Value,before[property.Name])))changed[property.Name]=property.Value.DeepClone();
                    if(changed.Count>1){if(group=="players")changed["weapon_revision"]=row["weapon_revision"].DeepClone();changes.Add(changed);}
                }
                result[group]=changes;
            }
            foreach(var group in new[]{"rooms","effects","fields","traps","team_rooms","markers"})if(current[group]!=null&&(prior==null||!JToken.DeepEquals(current[group],prior[group])))result[group]=current[group].DeepClone();
            // The existing v5+ reader validates this bounded list on every message.
            result["spawn_reservations"]=current["spawn_reservations"].DeepClone();
            result["reinforcement_remaining"]=current["reinforcement_remaining"]?.DeepClone();result["elapsed_ms"]=current["elapsed_ms"].DeepClone();
            previous=(JObject)current.DeepClone();revision=currentRevision;return result;
        }
    }
}
