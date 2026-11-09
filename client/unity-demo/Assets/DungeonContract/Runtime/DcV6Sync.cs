using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public sealed class DcV6LifeRecord {
  private static readonly byte[] SequenceKey=Encoding.UTF8.GetBytes(",\"delivery_sequence\":");
  private readonly byte[] payload;
  public readonly int Room;public readonly string EntityID,Kind;
  public int BudgetBytes=>payload.Length+32;
  internal DcV6LifeRecord(int room,string id,string kind,byte[] bytes){if(bytes.Length>2016||bytes.Length<2||bytes[0]!='{'||bytes[bytes.Length-1]!='}')throw new InvalidDataException("Lifecycle encoded event budget");Room=room;EntityID=id;Kind=kind;payload=bytes;}
  public void Write(MemoryStream output,uint sequence){output.Write(payload,0,payload.Length-1);output.Write(SequenceKey,0,SequenceKey.Length);var number=Encoding.ASCII.GetBytes(sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));output.Write(number,0,number.Length);output.WriteByte((byte)'}');}
  public JObject Object(uint sequence=0){var value=JObject.Parse(Encoding.UTF8.GetString(payload));if(sequence>0)value["delivery_sequence"]=sequence;return value;}
 }
 // Conservative UTF8 size, without constructing a JSON string for every immutable event.
 // Quotes/control/non-ASCII characters are charged at their worst JSON escape size.
 public static class DcV6JsonBudget {
  private static int Text(string text){long n=2;foreach(var c in text??""){n+=c=='"'||c=='\\'?2:c<32||c>126?6:1;if(n>1048576)return 1048577;}return (int)n;}
  public static int Upper(JToken value,int limit=1048576){var visited=0;return Count(value,limit,0,ref visited);}
  private static int Count(JToken value,int limit,int depth,ref int visited){if(depth>12||++visited>16384)return limit+1;long n=0;
   switch(value.Type){case JTokenType.Object:n=2;foreach(var p in ((JObject)value).Properties()){n+=Text(p.Name)+1+Count(p.Value,limit,depth+1,ref visited)+1;if(n>limit)return limit+1;}break;case JTokenType.Array:n=2;foreach(var item in (JArray)value){n+=Count(item,limit,depth+1,ref visited)+1;if(n>limit)return limit+1;}break;case JTokenType.String:n=Text((string)value);break;case JTokenType.Integer:var integer=((JValue)value).Value;if(integer is not long&&integer is not ulong&&integer is not int&&integer is not uint&&integer is not short&&integer is not ushort&&integer is not byte&&integer is not sbyte)throw new InvalidDataException("Unsupported lifecycle integer");n=20;break;case JTokenType.Float:n=32;break;case JTokenType.Boolean:n=5;break;case JTokenType.Null:n=4;break;default:throw new InvalidDataException("Unsupported lifecycle JSON token");}return n>limit?limit+1:(int)n;
  }
 }
 public sealed partial class DcP1World {
  private void V6Snapshot(JObject value,bool cachedFlights=false){
   Put(value,"v6_stage",true);Put(value,"schema_version",6);Put(value,"technical_detail",technicalDetail);
   foreach(var item in ((JArray)value["players"]).Cast<JObject>()){
    var p=players[(string)item["player_id"]];var movement=item["movement_profile"] as JObject;if(movement==null)item["movement_profile"]=movement=new JObject();Put(movement,"speed",Distance(parameters["player"],"move_milli_tiles_per_second"));Put(movement,"radius",PlayerRadius);Put(movement,"hurt_radius",PlayerHurtRadius);Put(movement,"space_distance",Distance(parameters["player"],"dodge_distance_milli_tiles"));Put(movement,"space_duration_ms",Number(parameters["player"],"dodge_duration_ms"));
    Put(item,"dash_source",p.DashSource);var protections=item["protection_sources"] as JObject;if(protections==null)item["protection_sources"]=protections=new JObject();Put(protections,"space",p.SpaceUntil);Put(protections,"gust",p.GustUntil);Put(protections,"hurt",p.HurtUntil);Put(protections,"rescue",p.RescueUntil);Put(protections,"reinforcement",p.ReinforcementUntil);Put(item,"reload_started_ms",p.ReloadStarted);Put(item,"reload_duration_ms",p.ReloadDuration);Put(item,"reload_until_ms",p.ReloadUntil);
    if(item["melee_action"] is JObject attack){var a=p.MeleeAttack;Put(attack,"impact_valid",a.ImpactValid);Put(attack,"preview_valid",a.PreviewValid);Put(attack,"preview_x",a.PreviewImpact.x);Put(attack,"preview_y",a.PreviewImpact.y);Put(attack,"attack_started_ms",now+(a.StartedTick-WorldTick)*1000/30);Put(attack,"phase_started_ms",now+((a.Phase=="active"?a.ActiveTick:a.Phase=="recover"?a.RecoveryTick:a.StartedTick)-WorldTick)*1000/30);Put(attack,"phase_end_ms",now+((a.Phase=="active"?a.RecoveryTick:a.Phase=="recover"?a.EndTick:a.ActiveTick)-WorldTick)*1000/30);}
   }
   var fieldIndex=0;foreach(var item in ((JArray)value["fields"]).Cast<JObject>()){var f=fields[fieldIndex++];Put(item,"owner",f.Owner);Put(item,"owner_life",players[f.Owner].LifeGeneration);Put(item,"cast_id",f.ID);Put(item,"ends_ms",f.Until);}
   var trapIndex=0;foreach(var item in ((JArray)value["traps"]).Cast<JObject>()){var t=traps[trapIndex++];Put(item,"activation_epoch_ms",t.Epoch);Put(item,"activation_generation",t.ActivationGeneration);Put(item,"group",t.Group);Put(item,"cycle_index",t.Cycle);}
   if(V7)V7Snapshot(value,cachedFlights);if(V8)V8Snapshot(value);
   var i=0;foreach(var projectile in ((JArray)value["projectiles"]).Cast<JObject>()){var p=projectiles[i++];Put(projectile,"trajectory_revision",p.TrajectoryRevision);if(cachedFlights&&p.RuntimeSnapshotBound)continue;Put(projectile,"owner",p.Owner);Put(projectile,"damage",p.Damage);Put(projectile,"frost",p.FrostEnabled);Put(projectile,"chain",p.ChainEnabled);}
  }
 }
 // A connection owns its delivery stream. Room filtering occurs before sequence allocation.
 public sealed class DcV6LifecycleStream {
  private struct Delivery {public uint Sequence;public DcV6LifeRecord Record;public JObject Json;public int Bytes;public string Kind=>Record?.Kind??(string)Json["kind"];public string ID=>Record?.EntityID??(string)Json["entity_id"];public JObject Object()=>Record!=null?Record.Object(Sequence):(JObject)Json.DeepClone();}
  public int Room=-1;public uint Interest=1;public uint Sequence;public uint Acknowledged;public uint Sent;
  public int BaselineRevision {get;private set;}public uint BaselineWatermark {get;private set;}public bool AwaitingBaseline=>BaselineRevision!=0;
  private readonly Queue<Delivery> pending=new Queue<Delivery>();private int bytes;
  public readonly Dictionary<uint,uint> Births=new Dictionary<uint,uint>();
  public int Pending=>pending.Count;public int Bytes=>bytes;
  public void ResetRoom(int room,bool force=false){if(Room==room&&!force)return;Room=room;Interest++;pending.Clear();Births.Clear();bytes=0;Acknowledged=Sent=Sequence;BaselineRevision=0;}
  public void BeginBaseline(int revision){if(AwaitingBaseline||revision<1)throw new InvalidDataException("A complete baseline is already in flight");BaselineRevision=revision;BaselineWatermark=Sequence;}
  public bool AcknowledgeBaseline(int revision){if(revision!=BaselineRevision||!AwaitingBaseline)return false;Sent=Math.Max(Sent,BaselineWatermark);Acknowledge(BaselineWatermark);BaselineRevision=0;return true;}
  public bool Add(JObject source){if((int)source["room_id"]!=Room)return true;var value=(JObject)source.DeepClone();value["delivery_sequence"]=Sequence+1;return Enqueue(new Delivery{Json=value,Sequence=Sequence+1,Bytes=DcV6JsonBudget.Upper(value)});}
  public bool Add(DcV6LifeRecord source){if(source.Room!=Room)return true;return Enqueue(new Delivery{Record=source,Sequence=Sequence+1,Bytes=source.BudgetBytes});}
  private bool Enqueue(Delivery value){if(value.Bytes>2048||pending.Count>=2048||bytes+value.Bytes>1048576)return false;Sequence=value.Sequence;pending.Enqueue(value);bytes+=value.Bytes;if(value.Kind=="spawn")Births[DcS1Replica.ProjectileHandle(value.ID)]=Sequence;return true;}
  public void Baseline(JObject state){state["interest_revision"]=Interest;state["lifecycle_watermark"]=Sequence;}
  public void Acknowledge(uint sequence){if(sequence>Sent)throw new InvalidDataException("Lifecycle ACK beyond sent watermark");Acknowledged=Math.Max(Acknowledged,sequence);while(pending.Count>0&&pending.Peek().Sequence<=Acknowledged){var value=pending.Dequeue();bytes-=value.Bytes;if(value.Kind=="terminate")Births.Remove(DcS1Replica.ProjectileHandle(value.ID));}}
  public JObject[] Batches(){if(AwaitingBaseline)return Array.Empty<JObject>();var result=new List<JObject>();var rows=new JArray();var used=128;foreach(var value in pending.Where(e=>e.Sequence>Sent)){if(rows.Count>=64||used+value.Bytes+1>15500){result.Add(new JObject{["interest_revision"]=Interest,["events"]=rows});rows=new JArray();used=128;}rows.Add(value.Object());used+=value.Bytes+1;}if(rows.Count>0)result.Add(new JObject{["interest_revision"]=Interest,["events"]=rows});foreach(var batch in result)Sent=(uint)batch["events"].Last["delivery_sequence"];return result.ToArray();}
  public IEnumerable<byte[]> EncodedBatches(string run,long generation,long connection,int protocol=6){
   if(AwaitingBaseline)yield break;
   var prefix=Encoding.UTF8.GetBytes(FormattableString.Invariant($"{{\"protocol_version\":{protocol},\"run_id\":{JsonConvert.ToString(run)},\"source_generation\":{generation},\"connection_generation\":{connection},\"interest_revision\":{Interest},\"events\":["));
   using var output=new MemoryStream(16384);output.Write(prefix,0,prefix.Length);var count=0;uint last=Sent;
   foreach(var value in pending.Where(e=>e.Sequence>Sent)){
    if(count>0&&(count>=64||output.Length+value.Bytes+3>16384)){output.WriteByte((byte)']');output.WriteByte((byte)'}');Sent=last;yield return output.ToArray();output.SetLength(0);output.Position=0;output.Write(prefix,0,prefix.Length);count=0;}
    if(count++>0)output.WriteByte((byte)',');if(value.Record!=null)value.Record.Write(output,value.Sequence);else{var data=Encoding.UTF8.GetBytes(value.Json.ToString(Formatting.None));output.Write(data,0,data.Length);}last=value.Sequence;
   }
   if(count>0){output.WriteByte((byte)']');output.WriteByte((byte)'}');if(output.Length>16384)throw new InvalidDataException("Encoded lifecycle batch budget");Sent=last;yield return output.ToArray();}
  }
 }
 public sealed partial class DcS1Replica {
  public int V6Spawned {get;private set;} public int V6Terminated {get;private set;} public int V6Bounced {get;private set;} public int V6Gaps {get;private set;}
  public uint V6Interest {get;private set;}public uint V6Watermark {get;private set;}public bool V6NeedsBaseline{get;private set;}
  public void RequireV6Baseline(){V6NeedsBaseline=true;}
  private readonly Dictionary<uint,uint> v6Tombstones=new Dictionary<uint,uint>();
  private readonly Queue<(byte[] bytes,double at)> v6Unknown=new Queue<(byte[],double)>();
  public void CheckV6Pending(){if(v6Unknown.Count>0&&Time.realtimeSinceStartupAsDouble-v6Unknown.Peek().at>0.1){v6Unknown.Clear();V6NeedsBaseline=true;}}
  public int V6TombstoneCount=>v6Tombstones.Count;public int V6UnknownCount=>v6Unknown.Count;
  public static uint ProjectileHandle(string id)=>V5Handle(id);
  private static void WriteV6Flight(BinaryWriter writer,JToken p,uint birth){
   writer.Write(V5Handle((string)p["id"]));writer.Write(Mathf.RoundToInt((float)p["x"]*1000));writer.Write(Mathf.RoundToInt((float)p["y"]*1000));writer.Write((short)Mathf.RoundToInt((float)p["direction_x"]*1000));writer.Write((short)Mathf.RoundToInt((float)p["direction_y"]*1000));writer.Write((ushort)Mathf.RoundToInt((float)p["speed"]*1000));writer.Write((ushort)Mathf.RoundToInt((float)p["radius"]*1000));writer.Write((byte)(((bool)p["enemy"]?1:0)|((bool?)p["rune_bolt"]==true?2:0)|((bool?)p["wind_volley"]==true?4:0)));writer.Write((byte)((bool?)p["unlimited_bounces"]==true?0:(int)p["bounces"]));writer.Write((byte)Math.Max(0,(int)p["penetrations"]));writer.Write((byte)(int)p["network_group"]);writer.Write((uint?)p["trajectory_revision"]??0);writer.Write(birth);
  }
  private static JObject ReadV6Flight(BinaryReader reader,int room){var handle=reader.ReadUInt32();var x=reader.ReadInt32();var y=reader.ReadInt32();var dx=reader.ReadInt16();var dy=reader.ReadInt16();var speed=reader.ReadUInt16();var radius=reader.ReadUInt16();var flags=reader.ReadByte();var bounce=reader.ReadByte();var pierce=reader.ReadByte();var bucket=reader.ReadByte();var revision=reader.ReadUInt32();var birth=reader.ReadUInt32();var squared=(long)dx*dx+(long)dy*dy;if(handle==0||flags>7||bounce>1||pierce>2||bucket>42||speed==0||radius==0||squared<996000||squared>1004000)throw new InvalidDataException("V6 flight row");var result=FromV5Row(new JArray(handle,x,y,dx,dy,speed,radius,flags,bounce,pierce,bucket,0,0),room);result["trajectory_revision"]=revision;result["born_lifecycle_sequence"]=birth;return result;}
  public static void PackV6Baseline(JObject snapshot,bool complete,Func<uint,uint> birth=null){
   snapshot["v6_projectiles_complete"]=complete;var directory=new JArray();var strings=new List<string>();var indexes=new Dictionary<string,int>();Func<string,int> intern=s=>{s=s??"";if(s.Length>80)throw new InvalidDataException("V6 root identifier");if(!indexes.TryGetValue(s,out var index)){index=strings.Count;strings.Add(s);indexes[s]=index;}return index;};
   using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
   if(complete)foreach(var p in (JArray)snapshot["projectiles"]){var handle=V5Handle((string)p["id"]);WriteV6Flight(writer,p,birth?.Invoke(handle)??0);var row=new JArray(handle,intern((string)p["root"]),intern((string)p["owner"]),(long?)p["source_life"]??0,(int?)p["damage"]??0,(bool?)p["frost"]??false,(bool?)p["chain"]??false);if((int?)snapshot["protocol_version"]>=7||(int?)snapshot["schema_version"]>=7){row.Add((long?)p["born_game_ms"]??0);row.Add((long?)p["expires_game_ms"]??0);row.Add(intern((string)p["attack_episode_id"]));row.Add((uint?)p["emission_index"]??0);row.Add((bool?)p["unlimited_bounces"]??false);}directory.Add(row);}
   snapshot["v6_projectile_blob"]=Convert.ToBase64String(stream.ToArray());snapshot["v6_flight_directory"]=directory;snapshot["v6_strings"]=new JArray(strings);snapshot["projectiles"]=new JArray();
  }
  private void PrepareV6Baseline(JObject value,string playerID){
   var room=(int)((JArray)value["players"]).First(p=>(string)p["player_id"]==playerID)["room_id"];var interest=(uint?)value["interest_revision"]??1;var complete=(bool?)value["v6_projectiles_complete"]??true;
   if(interest!=V6Interest){v5Projectiles.Clear();v6Tombstones.Clear();v6Unknown.Clear();V6Watermark=0;}V6Interest=interest;if(complete){V6Watermark=Math.Max(V6Watermark,(uint?)value["lifecycle_watermark"]??0);V6NeedsBaseline=false;}
   if(complete){v6Unknown.Clear();v5Projectiles.Clear();v6Tombstones.Clear();var blob=Convert.FromBase64String((string)value["v6_projectile_blob"]??"");var rows=(JArray)value["v6_flight_directory"]??new JArray();var strings=(JArray)value["v6_strings"]??new JArray();if(blob.Length>32768||blob.Length%32!=0||rows.Count!=blob.Length/32||strings.Count>2052)throw new InvalidDataException("V6 baseline flight budget");using var reader=new BinaryReader(new MemoryStream(blob));for(var i=0;i<rows.Count;i++){var p=ReadV6Flight(reader,room);var row=rows[i];var handle=V5Handle((string)p["id"]);if(handle!=(uint)row[0]||v5Projectiles.ContainsKey(handle))throw new InvalidDataException("V6 baseline handle");p["root"]=strings[(int)row[1]].DeepClone();p["owner"]=strings[(int)row[2]].DeepClone();p["source_life"]=row[3].DeepClone();p["damage"]=row[4].DeepClone();p["frost"]=row[5].DeepClone();p["chain"]=row[6].DeepClone();if(ProtocolVersion>=7){if(row.Count()!=12||(long)row[7]<0||(long)row[8]<0||(long)row[8]>0&&(long)row[8]-(long)row[7]!=6000)throw new InvalidDataException("V7 lifetime directory");p["born_game_ms"]=row[7].DeepClone();p["expires_game_ms"]=row[8].DeepClone();p["attack_episode_id"]=strings[(int)row[9]].DeepClone();p["emission_index"]=row[10].DeepClone();p["unlimited_bounces"]=row[11].DeepClone();}v5Projectiles[handle]=p;}}
   value["projectiles"]=new JArray(v5Projectiles.Values);v5Projectiles.Clear();foreach(var p in ((JArray)value["projectiles"]).Cast<JObject>())v5Projectiles[V5Handle((string)p["id"])]=p;
  }
  public bool ApplyV6Lifecycle(JObject batch){
   if(Snapshot==null||(uint)batch["interest_revision"]!=V6Interest)return false;
   var events=(JArray)batch["events"];if(events.Count>64||Encoding.UTF8.GetByteCount(batch.ToString(Formatting.None))>16384)throw new InvalidDataException("V6 lifecycle batch cap");
   var observedRoom=(int)((JArray)Snapshot["players"]).First(p=>(string)p["player_id"]==ownID)["room_id"];
   var expected=V6Watermark;foreach(var e in events){var sequence=(uint)e["delivery_sequence"];if(sequence<=expected)continue;if(sequence!=expected+1){V6Gaps++;V6NeedsBaseline=true;return false;}expected=sequence;var kind=(string)e["kind"];if(kind!="spawn"&&kind!="bounce"&&kind!="terminate"||DcV6JsonBudget.Upper(e)>2048||V5Handle((string)e["entity_id"])==0||e["state"] is not JObject)throw new InvalidDataException("V6 lifecycle event");var state=(JObject)e["state"];var reason=(string)e["reason"];if((string)state["id"]!=(string)e["entity_id"]||(int)state["room_id"]!=(int)e["room_id"]||(int)e["room_id"]!=observedRoom||(long)e["frame"]<0||(long)e["game_ms"]<0||(int)e["subtick"]<0||(int)e["subtick"]>65535||kind=="terminate"&&(!new[]{"wall","cover","target_hit","melee_block","wind_clear","room_clear","technical_wait","owner_permanent_exit","terminal","expired"}.Contains(reason)||reason=="expired"&&ProtocolVersion<7)||kind!="terminate"&&reason!=null)throw new InvalidDataException("V6 lifecycle identity/reason");}
   foreach(var e in events){var sequence=(uint)e["delivery_sequence"];if(sequence<=V6Watermark)continue;var handle=V5Handle((string)e["entity_id"]);var kind=(string)e["kind"];var state=(JObject)e["state"];var key="projectiles:"+(string)e["entity_id"];
    if(kind=="spawn"){V6Spawned++;
     if(v5Projectiles.ContainsKey(handle)||v6Tombstones.ContainsKey(handle)){V6NeedsBaseline=true;return false;}var p=(JObject)state.DeepClone();p["born_frame"]=e["frame"].DeepClone();p["born_lifecycle_sequence"]=sequence;((JArray)Snapshot["projectiles"]).Add(p);v5Projectiles[handle]=p;TrackPoint(key,PointV5(p),(long)e["game_ms"]/1000d);
    }else if(kind=="terminate"){V6Terminated++;
     JObject cosmetic=(JObject)e.DeepClone();if(v5Projectiles.TryGetValue(handle,out var p)){if((long?)p["born_frame"] is long born&&(long)e["frame"]-born<=1){cosmetic["confirmed_from_x"]=p["x"].DeepClone();cosmetic["confirmed_from_y"]=p["y"].DeepClone();}p.Remove();v5Projectiles.Remove(handle);}tracks.Remove(key);v6Tombstones[handle]=sequence;while(v6Tombstones.Count>4096)v6Tombstones.Remove(v6Tombstones.OrderBy(pair=>pair.Value).First().Key);
     if(Snapshot["v6_contacts"] is not JArray contacts)Snapshot["v6_contacts"]=contacts=new JArray();contacts.Add(cosmetic);while(contacts.Count>64)contacts.First.Remove();
    }else if(v5Projectiles.TryGetValue(handle,out var p)){V6Bounced++;
     if((int)state["trajectory_revision"]>(int)p["trajectory_revision"]){foreach(var property in state.Properties())p[property.Name]=property.Value.DeepClone();tracks.Remove(key);TrackPoint(key,PointV5(p),(long)e["game_ms"]/1000d);}
    }else {V6NeedsBaseline=true;return false;}
    V6Watermark=sequence;
   }
   var waiting=v6Unknown.ToArray();v6Unknown.Clear();foreach(var packet in waiting)if(Time.realtimeSinceStartupAsDouble-packet.at<=0.1)DynamicV6(packet.bytes,BitConverter.ToInt64(packet.bytes,1),BitConverter.ToInt64(packet.bytes,9),false);else V6NeedsBaseline=true;return true;
  }
  public static byte[][] EncodeV6(JObject snapshot,int revision,long generation,long connection,string playerID,uint interest=1,uint watermark=0,Func<uint,uint> birth=null){
   var room=(int)((JArray)snapshot["players"]).First(p=>(string)p["player_id"]==playerID)["room_id"];var packets=new List<byte[]>();
   Action<int,JToken[]> emit=(group,rows)=>{using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((byte)((int?)snapshot["protocol_version"]??(int?)snapshot["schema_version"]??6));writer.Write(generation);writer.Write(connection);writer.Write(revision);writer.Write((byte)group);writer.Write((long)snapshot["world_frame_id"]);writer.Write((long)snapshot["elapsed_ms"]);writer.Write((byte)rows.Length);writer.Write(interest);writer.Write(watermark);
    for(var i=0;i<rows.Length;i++){var item=rows[i];if(group<43)WriteV6Flight(writer,item,birth?.Invoke(V5Handle((string)item["id"]))??0);else{writer.Write((uint)((group-43)*24+i+1));writer.Write(Mathf.RoundToInt((float)item["x"]*1000));writer.Write(Mathf.RoundToInt((float)item["y"]*1000));writer.Write((uint?)item["last_input_sequence"]??0);writer.Write((ushort)Math.Min(65535,(long?)item["dodge_duration_remaining_ms"]??0));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_x"]??0)*1000));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_y"]??0)*1000));writer.Write((ushort)Mathf.RoundToInt(((float?)item["dodge_speed"]??0)*1000));writer.Write((int?)item["health"]??0);writer.Write((int?)item["maximum"]??(int?)item["max_health"]??0);}}
    if(rows.Length>24||stream.Length>900)throw new InvalidDataException("V6 dynamic packet budget");packets.Add(stream.ToArray());};
   var actors=Groups.Where(g=>g!="projectiles").SelectMany(g=>((JArray)snapshot[g]).Where(p=>(int)p["room_id"]==room)).ToArray();if(actors.Length>216)throw new InvalidDataException("V6 actor capacity");for(var i=0;i<actors.Length;i+=24)emit(43+i/24,actors.Skip(i).Take(24).ToArray());
   foreach(var group in ((JArray)snapshot["projectiles"]).Where(p=>(int)p["room_id"]==room).GroupBy(p=>(int)p["network_group"]).OrderBy(g=>g.Key))emit(group.Key,group.ToArray());return packets.ToArray();
  }
  private bool DynamicV6(byte[] bytes,long generation,long connection,bool buffer=true){
   if(bytes.Length<47||bytes.Length>900)return false;using var reader=new BinaryReader(new MemoryStream(bytes));if(reader.ReadByte()!=ProtocolVersion||reader.ReadInt64()!=generation||reader.ReadInt64()!=connection)return false;var revision=reader.ReadInt32();var group=reader.ReadByte();var frame=reader.ReadInt64();var game=reader.ReadInt64();var count=reader.ReadByte();var interest=reader.ReadUInt32();var watermark=reader.ReadUInt32();
   if(Snapshot==null||revision!=Revision||interest!=V6Interest||group>51||count>24||bytes.Length!=47+count*32||frame<=(long)Snapshot["world_frame_id"]||groupFrames.TryGetValue(group,out var previous)&&frame<=previous)return false;
   var room=(int)((JArray)Snapshot["players"]).First(p=>(string)p["player_id"]==ownID)["room_id"];var flights=new List<JObject>();var actors=new List<(uint handle,int x,int y,uint ack,int dodge,short dx,short dy,int hp,int max,int dashSpeed)>();var seen=new HashSet<uint>();var missing=false;
   for(var i=0;i<count;i++)if(group<43){var p=ReadV6Flight(reader,room);var handle=V5Handle((string)p["id"]);if(!seen.Add(handle)||(int)p["network_group"]!=group)return false;if(v5Projectiles.TryGetValue(handle,out var known)?(uint)p["trajectory_revision"]>(uint)known["trajectory_revision"]:!v6Tombstones.ContainsKey(handle)&&(uint)p["born_lifecycle_sequence"]>V6Watermark)missing=true;flights.Add(p);}else{var handle=reader.ReadUInt32();var x=reader.ReadInt32();var y=reader.ReadInt32();var ack=reader.ReadUInt32();var dodge=reader.ReadUInt16();var dx=reader.ReadInt16();var dy=reader.ReadInt16();var dashSpeed=reader.ReadUInt16();var hp=reader.ReadInt32();var max=reader.ReadInt32();if(handle!=(group-43)*24+i+1||!entities.ContainsKey(handle))return false;actors.Add((handle,x,y,ack,dodge,dx,dy,hp,max,dashSpeed));}
   if(missing){if(buffer){if(v6Unknown.Count>=32||v6Unknown.Count>0&&Time.realtimeSinceStartupAsDouble-v6Unknown.Peek().at>0.1){V6NeedsBaseline=true;v6Unknown.Clear();}else v6Unknown.Enqueue((bytes,Time.realtimeSinceStartupAsDouble));}return false;}
   groupFrames[group]=frame;
   foreach(var incoming in flights){var handle=V5Handle((string)incoming["id"]);if(!v5Projectiles.TryGetValue(handle,out var p)||(uint)incoming["trajectory_revision"]!=(uint)p["trajectory_revision"])continue;foreach(var property in incoming.Properties())if(property.Name!="source_life"&&property.Name!="root_handle")p[property.Name]=property.Value.DeepClone();TrackPoint("projectiles:"+(string)p["id"],PointV5(p),game/1000d);}
   foreach(var a in actors){var p=entities[a.handle];if(ProtocolVersion>=7&&p["body_center_x"]!=null){p["body_center_x"]=(float)p["body_center_x"]+a.x/1000f-(float)p["x"];p["body_center_y"]=(float)p["body_center_y"]+a.y/1000f-(float)p["y"];}p["x"]=a.x/1000f;p["y"]=a.y/1000f;p["health"]=a.hp;if(a.max>0)p["maximum"]=a.max;TrackPoint(keys[a.handle],PointV5(p),game/1000d);if((string)p["player_id"]==ownID){OwnPosition=PointV5(p);Ack=a.ack;OwnDodgeRemaining=a.dodge;OwnDodgeSpeed=a.dashSpeed/1000f;p["dodge_speed"]=OwnDodgeSpeed;OwnDodgeDirection=new Vector2(a.dx,a.dy)/1000;GameMS=game;LatestFrame=frame;v6ClockAt=Time.realtimeSinceStartupAsDouble;}}
   Snapshot["dynamic_elapsed_ms"]=Math.Max((long?)Snapshot["dynamic_elapsed_ms"]??0,game);return true;
  }
 }
}
