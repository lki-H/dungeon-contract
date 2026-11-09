using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
namespace DungeonContract {
 public sealed partial class DcP0V3 {
  private string probeOrdinaryRun;
  public bool ProbeOrdinaryActionObserved=>runID!=null&&probeOrdinaryRun==runID;
  private readonly Dictionary<ulong,DcV6LifecycleStream> v6Streams=new Dictionary<ulong,DcV6LifecycleStream>();
  private readonly Dictionary<ulong,Queue<double>> v6AckBudget=new Dictionary<ulong,Queue<double>>();
  private readonly Dictionary<ulong,Queue<double>> v6ResyncBudget=new Dictionary<ulong,Queue<double>>();
  private double v6NextAck;private double v6NextResync;
  private int v6PreviousConnectionGaps;
  private void PrepareV6Network(JObject snapshot){
   var events=p1World.DrainV6LifecycleRecords();
   foreach(var client in v6Streams.Keys.Where(c=>!identities.ContainsKey(c)).ToArray()){v6Streams.Remove(client);v6AckBudget.Remove(client);v6ResyncBudget.Remove(client);}
   foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)).ToArray()){
    if(!v6Streams.TryGetValue(identity.Key,out var stream)){stream=new DcV6LifecycleStream();v6Streams[identity.Key]=stream;}
    var room=(int)((JArray)snapshot["players"]).First(p=>(string)p["player_id"]==(string)identity.Value["player_id"])["room_id"];if(stream.Room!=room){stream.ResetRoom(room);p2BaselinePump.Remove(identity.Key);v5BaselineRooms.Remove(identity.Key);}
    foreach(var e in events)if(!stream.Add(e)){network.DisconnectClient(identity.Key,"V6 observer reliable queue exceeded");v5BaselineRooms.Remove(identity.Key);break;}
   }
  }
  private void SendV6Lifecycle(){foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)).ToArray()){
   if(!v6Streams.TryGetValue(identity.Key,out var stream))continue;
   foreach(var batch in stream.EncodedBatches((string)assignment["run_id"],sourceGeneration,(long)identity.Value["connection_generation"],Protocol)){
    if(batch.Length>16384)throw new System.IO.InvalidDataException("V6 lifecycle byte limit");using var writer=new FastBufferWriter(batch.Length+4,Unity.Collections.Allocator.Temp);writer.WriteValueSafe(batch.Length);writer.WriteBytesSafe(batch);network.CustomMessagingManager.SendNamedMessage(WireName("dc.lifecycle.v6"),identity.Key,writer,NetworkDelivery.ReliableFragmentedSequenced);v5ReliableJsonSentBytes+=batch.Length+4;
   }
  }}
  private bool V6Binding(JObject v,ulong connection)=>identities.TryGetValue(connection,out var identity)&&(int?)v["protocol_version"]==Protocol&&(string)v["run_id"]==(string)assignment["run_id"]&&(long?)v["source_generation"]==sourceGeneration&&(long?)v["connection_generation"]==(long)identity["connection_generation"];
  private void V6LifecycleAck(ulong connection,FastBufferReader reader){try{if(!Budget(v6AckBudget,connection,60))throw new InvalidOperationException();var v=Read(reader,1024);if(!V6Binding(v,connection)||!v6Streams.TryGetValue(connection,out var stream)||(uint)v["interest_revision"]!=stream.Interest)return;var sequence=(uint)v["delivery_sequence"];if(stream.AwaitingBaseline&&sequence>stream.Sent&&sequence<=stream.BaselineWatermark)return;stream.Acknowledge(sequence);}catch(Exception error){RecordProbeNetwork("server.lifecycle_ack:"+connection,error);network.DisconnectClient(connection,"lifecycle_ack_invalid");}}
  private void V6RequestBaseline(ulong connection,FastBufferReader reader){try{if(!Budget(v6ResyncBudget,connection,1))return;var v=Read(reader,1024);if(!V6Binding(v,connection)||!v6Streams.TryGetValue(connection,out var pending)||(uint?)v["interest_revision"]!=pending.Interest||pending.AwaitingBaseline)return;v5BaselineRooms.Remove(connection);s1EntitySignature=null;s1NextState=0;}catch(Exception error){RecordProbeNetwork("server.resync:"+connection,error);network.DisconnectClient(connection);}}
  private void V6LifecycleMessage(ulong connection,FastBufferReader reader){try{if(connection!=NetworkManager.ServerClientId)return;var v=Read(reader,16384);if((int)v["protocol_version"]!=Protocol||(string)v["run_id"]!=runID||(long)v["source_generation"]!=sourceGeneration||(long)v["connection_generation"]!=connectionGeneration)return;
   if(s1Replica.ApplyV6Lifecycle(v)){if(DcV6LifecycleObservation.OrdinaryBirth(v,playerID,s1Replica.V6Watermark))probeOrdinaryRun=runID;p1View?.Receive(p1Snapshot);}
  }catch(Exception error){RecordProbeNetwork("lifecycle",error);network.Shutdown();p1View?.Status("弹体生命周期校验失败，正在恢复连接");}}
  private void UpdateV6Acks(){
   if(!DcV6Profile.Active||role!="client"||!s1ClientBaselineReady||!network.IsConnectedClient)return;
   s1Replica.CheckV6Pending();
   var v=new JObject{["protocol_version"]=Protocol,["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["interest_revision"]=s1Replica.V6Interest,["delivery_sequence"]=s1Replica.V6Watermark};
   if(Time.realtimeSinceStartupAsDouble>=v6NextAck){v6NextAck=Time.realtimeSinceStartupAsDouble+0.1;Send("dc.lifecycle-ack.v6",NetworkManager.ServerClientId,v,NetworkDelivery.ReliableSequenced);}
   if(s1Replica.V6NeedsBaseline&&Time.realtimeSinceStartupAsDouble>=v6NextResync){v6NextResync=Time.realtimeSinceStartupAsDouble+1;Send("dc.resync.v6",NetworkManager.ServerClientId,v,NetworkDelivery.ReliableSequenced);}
  }
  private byte[][] EncodeV6Network(JObject snapshot,KeyValuePair<ulong,JObject> identity){var stream=v6Streams[identity.Key];if(stream.AwaitingBaseline)return Array.Empty<byte[]>();return DcS1Replica.EncodeV6(snapshot,s1StateRevision,sourceGeneration,(long)identity.Value["connection_generation"],(string)identity.Value["player_id"],stream.Interest,stream.Sequence,h=>stream.Births.TryGetValue(h,out var birth)?birth:0);}
 }
 public static class DcV6LifecycleObservation {
  public static bool OrdinaryBirth(JObject message,string player,uint watermark){
   return (message["events"] as JArray??new JArray()).Any(e=>(string)e["kind"]=="spawn"&&(uint?)e["delivery_sequence"]>0&&(uint?)e["delivery_sequence"]<=watermark&&e["state"] is JObject p&&(string)p["owner"]==player&&(bool?)p["enemy"]==false&&((string)p["root"]??"").StartsWith("shot_",StringComparison.Ordinal)&&Guid.TryParse((string)p["attack_episode_id"],out _));
  }
 }

}
