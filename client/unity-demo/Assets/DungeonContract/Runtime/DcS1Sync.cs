using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcS1Replica
    {
        private struct Pose { public Vector2 Point;public double At; }
        private sealed class Track { public Vector2 Current;public readonly List<Pose> Samples=new List<Pose>(); }
        private readonly Dictionary<uint,JObject> entities=new Dictionary<uint,JObject>();
        private readonly Dictionary<uint,string> keys=new Dictionary<uint,string>();
        private readonly Dictionary<string,Track> tracks=new Dictionary<string,Track>();
        public JObject Snapshot { get; private set; }
        public int Revision { get; private set; }
        public long LatestFrame { get; private set; }
        public long GameMS { get; private set; }
        public uint Ack { get; private set; }
        public void Clear(){V6Spawned=0;V6Terminated=0;V6Bounced=0;V6Gaps=0;V6Interest=0;V6Watermark=0;V6NeedsBaseline=false;v6Tombstones.Clear();v6Unknown.Clear();entities.Clear();keys.Clear();tracks.Clear();groupFrames.Clear();v5Projectiles.Clear();Snapshot=null;Revision=0;LatestFrame=0;GameMS=0;Ack=0;}
        public Vector2 OwnPosition { get; private set; }
        public Vector2 OwnDodgeDirection { get; private set; }
        public long OwnDodgeRemaining { get; private set; }
        public float OwnDodgeSpeed {get;private set;}=8;
        private string ownID;
        private readonly Dictionary<int,long> groupFrames=new Dictionary<int,long>();
        public static readonly string[] Groups={"players","enemies","projectiles","turrets","covers","fields","traps"};
        public static string Key(string group,JToken item)=>group+":"+(group=="players"?(string)item["player_id"]:(string)item["id"]);
        public void Baseline(JObject value,string playerID,int revision)
        {
            if((int?)value["protocol_version"]>=6){ProtocolVersion=(int)value["protocol_version"];PrepareV6Baseline(value,playerID);}
            else if((int?)value["protocol_version"]==5){ProtocolVersion=5;PrepareV5Baseline(value,playerID);}
            Snapshot=value;ownID=playerID;Revision=revision;entities.Clear();keys.Clear();groupFrames.Clear();
            uint handle=0;
            foreach(var group in Groups.Where(group=>ProtocolVersion<5||group!="projectiles"))foreach(var item in ((JArray)value[group]).Cast<JObject>()){entities[++handle]=item;keys[handle]=Key(group,item);TrackPoint(keys[handle],new Vector2((float)item["x"],(float)item["y"]),ProtocolVersion>=6?(long)value["elapsed_ms"]/1000d:(double?)null);}
            var live=new HashSet<string>(keys.Values);if(ProtocolVersion>=5)live.UnionWith(v5Projectiles.Values.Select(item=>"projectiles:"+(string)item["id"]));foreach(var key in tracks.Keys.Where(key=>!live.Contains(key)).ToArray())tracks.Remove(key);
            var own=((JArray)value["players"]).First(item=>(string)item["player_id"]==ownID);
            OwnPosition=new Vector2((float)own["x"],(float)own["y"]);Ack=(uint)own["last_input_sequence"];OwnDodgeDirection=new Vector2((float)own["dodge_direction_x"],(float)own["dodge_direction_y"]);OwnDodgeRemaining=(long)own["dodge_duration_remaining_ms"];OwnDodgeSpeed=(float?)own["dodge_speed"]??8;GameMS=(long)value["elapsed_ms"];LatestFrame=(long)value["world_frame_id"];v6ClockAt=Time.realtimeSinceStartupAsDouble;
        }
        private double v6ClockAt;
        private void TrackPoint(string key,Vector2 point,double? stamp=null)
        {
            var time=stamp??Time.realtimeSinceStartupAsDouble;
            if(!tracks.TryGetValue(key,out var track)){track=new Track();tracks[key]=track;}
            track.Current=point;track.Samples.Add(new Pose{Point=point,At=time});if(track.Samples.Count>12)track.Samples.RemoveAt(0);
        }
        public bool Dynamic(byte[] bytes,long generation,long connection)
        {
            if(ProtocolVersion>=6)return DynamicV6(bytes,generation,connection);
            if(ProtocolVersion==5)return DynamicV5(bytes,generation,connection);
            if(bytes.Length>900||bytes.Length<39)return false;
            using var reader=new BinaryReader(new MemoryStream(bytes));
            if(reader.ReadByte()!=4||reader.ReadInt64()!=generation||reader.ReadInt64()!=connection)return false;
            var revision=reader.ReadInt32();var group=reader.ReadByte();var frame=reader.ReadInt64();var game=reader.ReadInt64();var count=reader.ReadByte();
            if(revision!=Revision||group>15||count>48||bytes.Length!=39+count*24)return false;
            if(Snapshot==null||frame<=(long)Snapshot["world_frame_id"])return false;
            if(groupFrames.TryGetValue(group,out var previous)&&frame<=previous)return false;
            groupFrames[group]=frame;
            for(var index=0;index<count;index++)
            {
                var handle=reader.ReadUInt32();var horizontal=reader.ReadInt32()/1000f;var vertical=reader.ReadInt32()/1000f;var ack=reader.ReadUInt32();var dodge=reader.ReadUInt16();var directionHorizontal=reader.ReadInt16()/1000f;var directionVertical=reader.ReadInt16()/1000f;reader.ReadUInt16();
                if(!entities.TryGetValue(handle,out var item))continue;
                item["x"]=horizontal;item["y"]=vertical;TrackPoint(keys[handle],new Vector2(horizontal,vertical));
                if((string)item["player_id"]==ownID){OwnPosition=new Vector2(horizontal,vertical);Ack=ack;OwnDodgeRemaining=dodge;OwnDodgeDirection=new Vector2(directionHorizontal,directionVertical);GameMS=game;LatestFrame=frame;}
            }
            return true;
        }
        public Vector2 Interpolated(string key,Vector2 fallback)
        {
            if(!tracks.TryGetValue(key,out var track))return fallback;
            var target=ProtocolVersion>=6?GameMS/1000d+Math.Min(0.1,Math.Max(0,Time.realtimeSinceStartupAsDouble-v6ClockAt))-0.067:Time.realtimeSinceStartupAsDouble-0.067;
            if(track.Samples.Count==0)return fallback;
            for(var index=1;index<track.Samples.Count;index++)if(track.Samples[index].At>=target){var previous=track.Samples[index-1];var current=track.Samples[index];return Vector2.Lerp(previous.Point,current.Point,(float)((target-previous.At)/Math.Max(0.001,current.At-previous.At)));}
            return track.Current;
        }
        public IEnumerable<KeyValuePair<string,Vector2>> Visuals()
        {
            foreach(var pair in tracks)yield return new KeyValuePair<string,Vector2>(pair.Key,Interpolated(pair.Key,pair.Value.Current));
        }
        public static byte[][] Encode(JObject snapshot,int revision,long generation,long connection,int protocol=4,string playerID=null)
        {
            if(protocol>=6)return EncodeV6(snapshot,revision,generation,connection,playerID);
            if(protocol==5)return EncodeV5(snapshot,revision,generation,connection,playerID);
            var records=new List<JToken>();
            foreach(var group in Groups)foreach(var item in (JArray)snapshot[group])records.Add(item);
            var packets=new List<byte[]>();var chunk=0;
            for(var offset=0;offset<records.Count;offset+=32)
            {
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
                var count=Math.Min(32,records.Count-offset);writer.Write((byte)4);writer.Write(generation);writer.Write(connection);writer.Write(revision);writer.Write((byte)chunk++);writer.Write((long)snapshot["world_frame_id"]);writer.Write((long)snapshot["elapsed_ms"]);writer.Write((byte)count);
                for(var index=offset;index<offset+count;index++)
                {
                    var item=records[index];writer.Write((uint)(index+1));writer.Write(Mathf.RoundToInt((float)item["x"]*1000));writer.Write(Mathf.RoundToInt((float)item["y"]*1000));writer.Write((uint?)item["last_input_sequence"]??0);writer.Write((ushort)Math.Min(65535,(long?)item["dodge_duration_remaining_ms"]??0));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_x"]??0)*1000));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_y"]??0)*1000));writer.Write((ushort)0);
                }
                packets.Add(stream.ToArray());
            }
            if(packets.Count>16)throw new InvalidDataException("Dynamic group budget");
            return packets.ToArray();
        }
    }

    public sealed partial class DcS1Prediction
    {
        private struct Step { public ushort Aim;public uint Sequence;public float Delta;public Vector2 Move;public double EndGame;public bool Dodge;public Vector2 Direction; }
        private readonly Queue<Step> history=new Queue<Step>();
        private readonly JObject geometry;
        private JObject snapshot;
        private int room=-1;
        private double game;
        private double dashUntil;
        private Vector2 direction;
        private Vector2 lastDirection;
        private uint confirmed;
        private Vector2 renderOffset;
        private bool charging;
        private float meleeMoveFactor=1;
        private bool BlocksNormalEnemies=>(bool?)geometry["parameters"]?["player"]?["normal_enemy_body_blocks"]??((string)geometry["lifecycle_stage"]!="p2_room_camera_projectile");
        private bool alive;
        private float dodgeSpeed=8;
        private bool V6=>(int?)geometry["parameters"]?["versions"]?["battle_protocol"]>=6;
        private float Radius=>V6?(float)geometry["parameters"]["player"]["collision_radius_milli_tiles"]/1000:0.3f;
        private float Speed=>V6?(float)geometry["parameters"]["player"]["move_milli_tiles_per_second"]/1000:5;
        private int SpaceDuration=>V6?(int)geometry["parameters"]["player"]["dodge_duration_ms"]:250;
        private float SpaceSpeed=>V6?(float)geometry["parameters"]["player"]["dodge_distance_milli_tiles"]/(int)geometry["parameters"]["player"]["dodge_duration_ms"]:8;
        public Vector2 Position {get;private set;}
        public Vector2 RenderPosition=>Position+renderOffset;
        public int Corrections {get;private set;}
        public int HistoryCount=>history.Count;
        public DcS1Prediction(JObject world){geometry=DcP1Definition.NormalizeGeometry(world);}
        public void Correct(DcS1Replica replica,string player)
        {
            snapshot=replica.Snapshot;var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==player);var nextRoom=(int)own["room_id"];
            if(V7){v7Kit=(string)own["loadout_id"];v7Facing=(string)own["body_facing"]??"down";}
            alive=(int)own["health"]>0&&!((bool?)own["left"]??false);
            var reset=room!=nextRoom||replica.Ack<confirmed||(int)own["health"]<=0;
            if(reset){history.Clear();room=nextRoom;game=replica.GameMS;dashUntil=0;lastDirection=Vector2.zero;renderOffset=Vector2.zero;}
            charging=(bool)own["charging"];dodgeSpeed=replica.OwnDodgeSpeed;meleeMoveFactor=(string)own["active_weapon_id"]=="rune_hammer"&&own["melee_action"] is JObject?0.8f:1;
            confirmed=replica.Ack;var before=RenderPosition;Position=replica.OwnPosition;dashUntil=replica.GameMS+replica.OwnDodgeRemaining;direction=replica.OwnDodgeDirection;
            while(history.Count>0&&(history.Peek().EndGame<=replica.GameMS||history.Peek().Sequence<replica.Ack))history.Dequeue();
            foreach(var step in history){if(V7)v7Aim=step.Aim;if(step.Dodge){direction=step.Direction;dashUntil=step.EndGame-step.Delta*1000+SpaceDuration;}Advance(step.Move,step.Delta,step.EndGame);}
            if(history.Count==0)game=replica.GameMS;
            renderOffset=reset||Vector2.Distance(before,Position)>=1.5f?Vector2.zero:before-Position;
            if(V7&&!V7PredictionLegal(Position+renderOffset,PredictedBody))renderOffset=Vector2.zero;
            if(Vector2.Distance(before,Position)>0.12f)Corrections++;
        }
        public void Move(DcP1Input input,float delta,bool dodge)
        {
            if(snapshot==null||room<0||!alive)return;
            if(V7)v7Aim=input.Aim;
            delta=Math.Min(delta,0.05f);game+=delta*1000;
            renderOffset=Vector2.Lerp(renderOffset,Vector2.zero,Mathf.Clamp01(delta/0.08f));
            if(input.Move.sqrMagnitude>0.01f)lastDirection=input.Move.normalized;
            if(V6&&game<dashUntil)dodge=false;
            if(dodge){dodgeSpeed=SpaceSpeed;direction=input.Move.sqrMagnitude>0.01f?input.Move.normalized:lastDirection.sqrMagnitude>0.01f?lastDirection:DcP1World.Aim(input.Aim);dashUntil=game+SpaceDuration-(V7?delta*1000:0);}
            var step=new Step{Aim=input.Aim,Sequence=input.Sequence,Move=input.Move,Delta=delta,EndGame=game,Dodge=dodge,Direction=direction};history.Enqueue(step);while(history.Count>512)history.Dequeue();
            Advance(input.Move,delta,game);
        }
        private void Advance(Vector2 move,float delta,double at)
        {
            if(V7){AdvanceV7Prediction(move,delta,at);return;}
            var layout=(JObject)geometry["rooms"][room];var dodging=at<dashUntil;var velocity=dodging?direction*dodgeSpeed:move*Speed*(charging?0.7f:meleeMoveFactor);
            if(V6&&dodging){AdvanceV6Dash(layout,velocity*delta,move,delta);return;}
            var before=Position;
            Position=RoomGeometry.Move(layout,Position,velocity*delta,Radius,point=>
                ((JArray)snapshot["covers"]).Any(item=>(int)item["room_id"]==room&&(int)item["health"]>0&&Vector2.Distance(point,new Vector2((float)item["x"],(float)item["y"]))<0.45f+Radius)
                ||((JArray)snapshot["enemies"]).Any(item=>(int)item["room_id"]==room&&(!BlocksNormalEnemies?(string)item["kind"]=="elite"||(string)item["kind"]=="boss":!dodging||(string)item["kind"]=="elite"||(string)item["kind"]=="boss")&&Vector2.Distance(point,new Vector2((float)item["x"],(float)item["y"]))<((string)item["kind"]=="boss"?0.9f+Radius:(string)item["kind"]=="elite"?0.6f+Radius:0.42f+Radius)&&Vector2.Distance(point,new Vector2((float)item["x"],(float)item["y"]))<Vector2.Distance(before,new Vector2((float)item["x"],(float)item["y"]))));
        }
        private void AdvanceV6Dash(JObject layout,Vector2 delta,Vector2 move,float dt){
            var end=Position+delta;var hit=DcV6Geometry.Terrain(layout,Position,end,Radius,true);
            foreach(var c in (JArray)snapshot["covers"]){if((int)c["room_id"]!=room||(int)c["health"]<=0)continue;var t=DcV6Geometry.Circle(Position,end,new Vector2((float)c["x"],(float)c["y"]),0.45f+Radius,true);if(t.HasValue&&(!hit.HasValue||t<hit.Value.Time))hit=new DcV6Geometry.Contact{Time=t.Value};}
            foreach(var e in (JArray)snapshot["enemies"]){if((int)e["room_id"]!=room||((string)e["kind"]!="elite"&&(string)e["kind"]!="boss"))continue;var t=DcV6Geometry.Circle(Position,end,new Vector2((float)e["x"],(float)e["y"]),((string)e["kind"]=="boss"?0.9f:0.6f)+Radius,true);if(t.HasValue&&(!hit.HasValue||t<hit.Value.Time))hit=new DcV6Geometry.Contact{Time=t.Value};}
            if(!hit.HasValue){Position=end;return;}Position=Vector2.Lerp(Position,end,Math.Max(0,hit.Value.Time-0.0001f));dashUntil=game;var before=Position;var remaining=dt*(1-hit.Value.Time);Position=RoomGeometry.Move(layout,Position,move*Speed*(charging?0.7f:meleeMoveFactor)*remaining,Radius,point=>((JArray)snapshot["covers"]).Any(c=>(int)c["room_id"]==room&&(int)c["health"]>0&&Vector2.Distance(point,new Vector2((float)c["x"],(float)c["y"]))<0.45f+Radius)||((JArray)snapshot["enemies"]).Any(e=>(int)e["room_id"]==room&&((string)e["kind"]=="elite"||(string)e["kind"]=="boss")&&Vector2.Distance(point,new Vector2((float)e["x"],(float)e["y"]))<((string)e["kind"]=="boss"?0.9f:0.6f)+Radius&&Vector2.Distance(point,new Vector2((float)e["x"],(float)e["y"]))<Vector2.Distance(before,new Vector2((float)e["x"],(float)e["y"]))));
        }
        public void Clear(){history.Clear();dashUntil=0;renderOffset=Vector2.zero;}
    }
}
