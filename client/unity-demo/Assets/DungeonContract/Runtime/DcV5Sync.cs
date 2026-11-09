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
        public int ProtocolVersion {get;set;}=4;
        public bool P5Presentation{get;set;}
        private readonly Dictionary<uint,JObject> v5Projectiles=new Dictionary<uint,JObject>();

        private static uint V5Handle(string identifier)
        {
            if(identifier==null)return 0;
            var suffix=identifier.LastIndexOf('_');
            return suffix>=0&&uint.TryParse(identifier.Substring(suffix+1),out var handle)?handle:0;
        }

        private static JArray V5Row(JToken projectile)=>new JArray(V5Handle((string)projectile["id"]),Mathf.RoundToInt((float)projectile["x"]*1000),Mathf.RoundToInt((float)projectile["y"]*1000),Mathf.RoundToInt((float)projectile["direction_x"]*1000),Mathf.RoundToInt((float)projectile["direction_y"]*1000),Mathf.RoundToInt((float)projectile["speed"]*1000),Mathf.RoundToInt((float)projectile["radius"]*1000),((bool)projectile["enemy"]?1:0)|((bool?)projectile["rune_bolt"]==true?2:0)|((bool?)projectile["wind_volley"]==true?4:0),(int)projectile["bounces"],(int)projectile["penetrations"],(int)projectile["network_group"],(long?)projectile["source_life"]??0,V5Handle((string)projectile["root"]));

        public static void PackV5Baseline(JObject snapshot,bool complete)
        {
            snapshot["v5_projectiles_complete"]=complete;
            snapshot["v5_projectile_rows"]=complete?new JArray(((JArray)snapshot["projectiles"]).Select(V5Row)):new JArray();
            snapshot["projectiles"]=new JArray();
        }

        private static JObject FromV5Row(JArray row,int room)
        {
            return new JObject{["id"]="projectile_"+((uint)row[0]).ToString("D6"),["room_id"]=room,["x"]=(int)row[1]/1000f,["y"]=(int)row[2]/1000f,["direction_x"]=(int)row[3]/1000f,["direction_y"]=(int)row[4]/1000f,["speed"]=(int)row[5]/1000f,["radius"]=(int)row[6]/1000f,["enemy"]=((int)row[7]&1)!=0,["rune_bolt"]=((int)row[7]&2)!=0,["wind_volley"]=((int)row[7]&4)!=0,["bounces"]=row[8],["penetrations"]=row[9],["network_group"]=row[10],["source_life"]=row[11],["root_handle"]=row[12],["unbounded"]=true,["remaining_range"]=null};
        }

        private void PrepareV5Baseline(JObject value,string playerID)
        {
            var complete=(bool?)value["v5_projectiles_complete"]??true;
            if(complete||Snapshot==null)v5Projectiles.Clear();
            if(value["v5_projectile_rows"] is JArray rows)
            {
                if(rows.Count>1024)throw new InvalidDataException("V5 baseline projectile capacity");
                var room=(int)((JArray)value["players"]).First(item=>(string)item["player_id"]==playerID)["room_id"];
                foreach(var row in rows.Cast<JArray>()){if(row.Count!=13||(int)row[7]<0||(int)row[7]>(P5Presentation?7:3))throw new InvalidDataException("V5 projectile row");v5Projectiles[(uint)row[0]]=FromV5Row(row,room);}
            }
            value["projectiles"]=new JArray(v5Projectiles.Values);
            v5Projectiles.Clear();foreach(var projectile in ((JArray)value["projectiles"]).Cast<JObject>())v5Projectiles[V5Handle((string)projectile["id"])]=projectile;
        }

        private static void V5Header(BinaryWriter writer,int revision,long generation,long connection,int group,JObject snapshot,int count)
        {
            writer.Write((byte)5);writer.Write(generation);writer.Write(connection);writer.Write(revision);writer.Write((byte)group);writer.Write((long)snapshot["world_frame_id"]);writer.Write((long)snapshot["elapsed_ms"]);writer.Write((byte)count);
        }

        private static byte[][] EncodeV5(JObject snapshot,int revision,long generation,long connection,string playerID)
        {
            var room=playerID==null?-1:(int)((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==playerID)["room_id"];
            var packets=new List<byte[]>();var records=Groups.Where(group=>group!="projectiles").SelectMany(group=>((JArray)snapshot[group]).Where(item=>room<0||(int)item["room_id"]==room)).ToArray();
            for(var offset=0;offset<records.Length;offset+=24)
            {
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);var count=Math.Min(24,records.Length-offset);
                V5Header(writer,revision,generation,connection,offset/24,snapshot,count);
                for(var index=offset;index<offset+count;index++)
                {
                    var item=records[index];writer.Write((uint)(index+1));writer.Write(Mathf.RoundToInt((float)item["x"]*1000));writer.Write(Mathf.RoundToInt((float)item["y"]*1000));writer.Write((uint?)item["last_input_sequence"]??0);writer.Write((ushort)Math.Min(65535,(long?)item["dodge_duration_remaining_ms"]??0));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_x"]??0)*1000));writer.Write((short)Mathf.RoundToInt(((float?)item["dodge_direction_y"]??0)*1000));writer.Write((ushort)0);writer.Write((int?)item["health"]??0);writer.Write((int?)item["maximum"]??(int?)item["max_health"]??0);
                }
                packets.Add(stream.ToArray());
            }
            var buckets=Enumerable.Range(0,43).Select(index=>new List<JToken>()).ToArray();
            foreach(var projectile in ((JArray)snapshot["projectiles"]).Where(item=>room<0||(int)item["room_id"]==room))buckets[(int)projectile["network_group"]].Add(projectile);
            for(var group=0;group<43;group++)
            {
                var bucket=buckets[group];if(bucket.Count>24)throw new InvalidDataException("V5 projectile bucket capacity");
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);V5Header(writer,revision,generation,connection,128+group,snapshot,bucket.Count);
                foreach(var projectile in bucket)
                {
                    var row=V5Row(projectile);writer.Write((uint)row[0]);writer.Write((int)row[1]);writer.Write((int)row[2]);writer.Write((short)(int)row[3]);writer.Write((short)(int)row[4]);writer.Write((ushort)(int)row[5]);writer.Write((ushort)(int)row[6]);writer.Write((byte)(int)row[7]);writer.Write((byte)(int)row[8]);writer.Write((byte)(int)row[9]);writer.Write((byte)group);writer.Write((uint)(long)row[11]);writer.Write((uint)row[12]);
                }
                packets.Add(stream.ToArray());
            }
            if(packets.Count>64||packets.Any(packet=>packet.Length>900))throw new InvalidDataException("V5 dynamic budget");
            return packets.ToArray();
        }

        private bool DynamicV5(byte[] bytes,long generation,long connection)
        {
            if(bytes.Length<39||bytes.Length>900)return false;
            using var reader=new BinaryReader(new MemoryStream(bytes));
            if(reader.ReadByte()!=5||reader.ReadInt64()!=generation||reader.ReadInt64()!=connection)return false;
            var revision=reader.ReadInt32();var group=reader.ReadByte();var frame=reader.ReadInt64();var game=reader.ReadInt64();var count=reader.ReadByte();
            if(revision!=Revision||Snapshot==null||count>24||bytes.Length!=39+32*count||group>=64&&group<128||group>=171||frame<=(long)Snapshot["world_frame_id"])return false;
            if(groupFrames.TryGetValue(group,out var previous)&&frame<=previous)return false;
            var seen=new HashSet<uint>();
            for(var index=0;index<count;index++)
            {
                var handle=reader.ReadUInt32();if(handle==0||!seen.Add(handle))return false;reader.BaseStream.Position+=8;
                if(group>=128)
                {
                    var horizontal=reader.ReadInt16();var vertical=reader.ReadInt16();var speed=reader.ReadUInt16();var radius=reader.ReadUInt16();var flags=reader.ReadByte();var bounces=reader.ReadByte();var penetrations=reader.ReadByte();var bucket=reader.ReadByte();reader.BaseStream.Position+=8;
                    var squared=(long)horizontal*horizontal+(long)vertical*vertical;
                    if(bucket!=group-128||speed==0||radius==0||bounces>1||penetrations>2||flags>(P5Presentation?7:3)||squared<996000||squared>1004000)return false;
                    if(v5Projectiles.TryGetValue(handle,out var existing)&&(int)existing["network_group"]!=bucket)return false;
                }
                else {if(handle!=group*24+index+1||!entities.ContainsKey(handle))return false;reader.BaseStream.Position+=20;}
            }
            reader.BaseStream.Position=39;
            groupFrames[group]=frame;
            var live=new HashSet<uint>();
            for(var index=0;index<count;index++)
            {
                var handle=reader.ReadUInt32();var horizontal=reader.ReadInt32();var vertical=reader.ReadInt32();
                if(group>=128)
                {
                    var directionHorizontal=reader.ReadInt16();var directionVertical=reader.ReadInt16();var speed=reader.ReadUInt16();var radius=reader.ReadUInt16();var flags=reader.ReadByte();var bounces=reader.ReadByte();var penetrations=reader.ReadByte();var bucket=reader.ReadByte();var source=reader.ReadUInt32();var root=reader.ReadUInt32();
                    if(bucket!=group-128||handle==0||speed==0||radius==0||bounces>1||penetrations>2)return false;
                    live.Add(handle);
                    if(!v5Projectiles.TryGetValue(handle,out var projectile))
                    {
                        var row=new JArray(handle,horizontal,vertical,directionHorizontal,directionVertical,speed,radius,flags,bounces,penetrations,bucket,source,root);
                        projectile=FromV5Row(row,(int)((JArray)Snapshot["players"]).First(item=>(string)item["player_id"]==ownID)["room_id"]);((JArray)Snapshot["projectiles"]).Add(projectile);v5Projectiles[handle]=projectile;
                    }
                    else
                    {
                        PutV5(projectile,"x",horizontal/1000f);PutV5(projectile,"y",vertical/1000f);PutV5(projectile,"direction_x",directionHorizontal/1000f);PutV5(projectile,"direction_y",directionVertical/1000f);PutV5(projectile,"bounces",(int)bounces);PutV5(projectile,"penetrations",(int)penetrations);
                    }
                    TrackPoint("projectiles:"+(string)projectile["id"],PointV5(projectile));
                }
                else
                {
                    var ack=reader.ReadUInt32();var dodge=reader.ReadUInt16();var directionHorizontal=reader.ReadInt16();var directionVertical=reader.ReadInt16();reader.ReadUInt16();var health=reader.ReadInt32();var maximum=reader.ReadInt32();
                    if(!entities.TryGetValue(handle,out var item))continue;
                    item["x"]=horizontal/1000f;item["y"]=vertical/1000f;item["health"]=health;if(maximum>0)item["maximum"]=maximum;TrackPoint(keys[handle],PointV5(item));
                    if((string)item["player_id"]==ownID){OwnPosition=PointV5(item);Ack=ack;OwnDodgeRemaining=dodge;OwnDodgeDirection=new Vector2(directionHorizontal,directionVertical)/1000;GameMS=game;LatestFrame=frame;}
                }
            }
            if(group>=128)
            {
                foreach(var handle in v5Projectiles.Where(pair=>(int)pair.Value["network_group"]==group-128&&!live.Contains(pair.Key)).Select(pair=>pair.Key).ToArray()){tracks.Remove("projectiles:"+(string)v5Projectiles[handle]["id"]);v5Projectiles[handle].Remove();v5Projectiles.Remove(handle);}
            }
            return true;
        }
        private static Vector2 PointV5(JToken value)=>new Vector2((float)value["x"],(float)value["y"]);
        private static void PutV5(JObject target,string key,object value){if(target[key] is JValue cell)cell.Value=value;else target[key]=new JValue(value);}
    }
}
