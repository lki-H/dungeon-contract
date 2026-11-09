using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcP21Pilot
    {
        private sealed class Motion{public Vector2 Point;public Vector2 Velocity;public long At;}
        private static readonly System.Collections.Generic.Dictionary<string,Motion> motions=new System.Collections.Generic.Dictionary<string,Motion>();
        private static string motionRun;
        private static long motionTime;
        public static DcP1Input Decide(JObject snapshot,JObject geometry,uint sequence,string playerID=null)
        {
            var own=playerID==null?snapshot["players"][0]:snapshot["players"].First(item=>(string)item["player_id"]==playerID);var room=(int)own["room_id"];var position=Point(own);var layout=(JObject)geometry["rooms"][room];
            var input=new DcP1Input{Sequence=sequence,WeaponID=(string)own["active_weapon_id"],WeaponRevision=(uint)own["weapon_revision"]};
            var enemies=((JArray)snapshot["enemies"]).Where(item=>(int)item["room_id"]==room).ToArray();
            var game=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];var run=(string)snapshot["run_id"];if(motionRun!=run||game<motionTime){motions.Clear();motionRun=run;}motionTime=game;
            foreach(var enemy in enemies){var id=(string)enemy["id"];var point=Point(enemy);if(!motions.TryGetValue(id,out var motion)){motion=new Motion{Point=point,At=game};motions[id]=motion;}if(game>motion.At){motion.Velocity=Vector2.ClampMagnitude((point-motion.Point)*1000f/(game-motion.At),6);motion.Point=point;motion.At=game;}}
            foreach(var id in motions.Keys.Where(id=>game-motions[id].At>5000).ToArray())motions.Remove(id);
            var target=enemies.Where(item=>RoomGeometry.ClearLine(layout,position,Point(item))&&!((JArray)snapshot["covers"]).Any(cover=>(int)cover["room_id"]==room&&(int)cover["health"]>0&&Segment(Point(cover),position,Point(item))<0.6f)).OrderBy(item=>Vector2.Distance(position,Point(item))+((string)item["kind"]=="archer"?-3:0)).FirstOrDefault();
            if(enemies.Length>0)
            {
                target=target??enemies.OrderBy(item=>Vector2.Distance(position,Point(item))).First();var offset=Point(target)-position;var speed=(string)own["loadout_id"]=="ranger"?20f:10f;var lead=(bool?)snapshot["v5_stage"]==true&&(string)target["warning"]!=""?Vector2.zero:motions[(string)target["id"]].Velocity*Math.Min(0.7f,offset.magnitude/speed);input.Aim=Angle(offset+lead);input.Attack=true;
                var best=float.MinValue;var move=Vector2.zero;
                var v5=(bool?)snapshot["v5_stage"]==true;
                for(var index=0;index<(v5?49:17);index++)
                {
                    var candidate=index==(v5?48:16)?Vector2.zero:new Vector2(Mathf.Cos(index%16*Mathf.PI/8),Mathf.Sin(index%16*Mathf.PI/8));var horizon=v5?index<16?1.6f:index<32?0.8f:0.35f:1.6f;var endpoint=position+candidate*horizon;
                    if(RoomGeometry.Blocked(layout,endpoint,0.4f)||!RoomGeometry.ClearLine(layout,position,endpoint)||((JArray)snapshot["covers"]).Any(item=>(int)item["room_id"]==room&&(int)item["health"]>0&&Segment(Point(item),position,endpoint)<(v5?Math.Min(0.8f,Vector2.Distance(position,Point(item))-0.001f):0.85f)))continue;
                    var minimum=enemies.Min(item=>Vector2.Distance(endpoint,Point(item)));var distance=Vector2.Distance(endpoint,Point(target));var preferred=v5?((string)own["loadout_id"]=="ranger"?6.5f:5.5f):8;var score=Math.Min(4,minimum)*3-Math.Abs(distance-preferred)*0.45f;
                    var width=(float)layout["width"];var height=(float)layout["height"];score+=Math.Min(2,Math.Min(Math.Min(endpoint.x-1,width-1-endpoint.x),Math.Min(endpoint.y-1,height-1-endpoint.y)))*0.8f;
                    var walkingTime=v5?DcV5Pilot.WalkTime(snapshot,layout,room,position,candidate):0.45f;
                    foreach(var projectile in ((JArray)snapshot["projectiles"]).Where(item=>(int)item["room_id"]==room&&(bool)item["enemy"]))
                    {
                        var relative=Point(projectile)-position;var projectileVelocity=new Vector2((float)projectile["direction_x"],(float)projectile["direction_y"])*((float?)projectile["speed"]??10);var velocity=projectileVelocity-candidate*5;var at=Mathf.Clamp(-Vector2.Dot(relative,velocity)/Math.Max(0.001f,velocity.sqrMagnitude),0,0.45f);var danger=v5?DcV5Pilot.Separation(relative,projectileVelocity,candidate,walkingTime,(bool?)snapshot["v6_stage"]==true?7:5):(relative+velocity*at).magnitude;if(danger<0.9f)score-=20*(0.9f-danger);
                    }
                    foreach(var trap in ((JArray)snapshot["traps"]).Where(item=>(int)item["room_id"]==room))if(Vector2.Distance(endpoint,Point(trap))<1.5f)score-=10;
                    foreach(var enemy in enemies){var kind=(string)enemy["warning"];if(kind=="stomp"&&Vector2.Distance(endpoint,Point(enemy))<3.5f)score-=18;if(kind=="melee"&&Vector2.Distance(endpoint,Point(enemy))<(v5&&(string)enemy["kind"]=="boss"?3.4f:2))score-=14;if(kind=="charge"&&Segment(endpoint,Point(enemy),Point(enemy)+new Vector2((float)enemy["aim_x"],(float)enemy["aim_y"])*(v5&&(string)enemy["kind"]=="boss"?7.2f:4))<1.4f)score-=18;}
                    var orbit=(bool?)snapshot["v5_stage"]==true&&(int)own["slot"]%2==1?-1:1;
                    score+=Vector2.Dot(candidate,new Vector2(-offset.y,offset.x).normalized)*0.2f*orbit;
                    if((bool?)snapshot["v5_stage"]==true)foreach(var teammate in snapshot["players"].Where(item=>(string)item["player_id"]!=(string)own["player_id"]&&(int)item["health"]>0&&(int)item["room_id"]==room))score-=Math.Max(0,2.5f-Vector2.Distance(endpoint,Point(teammate)))*0.8f;
                    if(score>best){best=score;move=candidate;}
                }
                input.Move=move;
            }
            else if((string)snapshot["rooms"][room]["status"]=="cleared")
            {
                var door=geometry["doors"].FirstOrDefault(item=>(int)item["from"]==room&&(int)item["to"]!=13&&(int)item["to"]!=14);
                if(door!=null){var point=RoomGeometry.DoorPoint(layout,(string)door["side"]);input.Move=Vector2.Distance(point,position)<0.6f?((string)door["side"]=="south"?Vector2.down:Vector2.right):RoomGeometry.PathDirection(layout,position,point,0.3f);}
            }
            else if(layout["objective"] is JArray point&&!(bool)snapshot["rooms"][room]["mechanism_complete"])
            {
                var destination=RoomGeometry.Point(point);if(Vector2.Distance(position,destination)<1.2f)input.Interact=true;else input.Move=RoomGeometry.PathDirection(layout,position,destination,0.3f);
            }
            return input;
        }
        public static bool Dodge(JObject snapshot,string playerID=null,Vector2? movement=null){var own=playerID==null?snapshot["players"][0]:snapshot["players"].First(item=>(string)item["player_id"]==playerID);if((bool?)snapshot["v5_stage"]==true)return DcV5Pilot.Dodge(snapshot,(string)own["player_id"],movement);return (long)own["dodge_remaining_ms"]==0&&(((JArray)snapshot["enemies"]).Any(enemy=>(int)enemy["room_id"]==(int)own["room_id"]&&Vector2.Distance(Point(enemy),Point(own))<2.2f)||((JArray)snapshot["projectiles"]).Any(item=>(int)item["room_id"]==(int)own["room_id"]&&(bool)item["enemy"]&&Segment(Point(own),Point(item),Point(item)+new Vector2((float)item["direction_x"],(float)item["direction_y"])*((float?)item["speed"]??10)*0.3f)<0.65f));}
        private static Vector2 Point(JToken value)=>new Vector2((float)value["x"],(float)value["y"]);
        private static ushort Angle(Vector2 value)=>(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(value.y,value.x),Mathf.PI*2)/(Mathf.PI*2)*65535);
        private static float Segment(Vector2 point,Vector2 start,Vector2 end){var delta=end-start;return Vector2.Distance(point,start+delta*Mathf.Clamp01(Vector2.Dot(point-start,delta)/Math.Max(0.001f,delta.sqrMagnitude)));}
    }
}
