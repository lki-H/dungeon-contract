using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcV5Pilot
    {
        private static Vector2 Point(JToken value)=>new Vector2((float)value["x"],(float)value["y"]);
        public static float WalkTime(JObject snapshot,JObject layout,int room,Vector2 position,Vector2 direction)
        {
            var modern=(bool?)snapshot["v6_stage"]==true;var end=position+direction*(modern?3.15f:2.25f);var fraction=DcV5Collision.Terrain(layout,position,end,0.29f)??1;
            foreach(var cover in snapshot["covers"].Where(item=>(int)item["room_id"]==room&&(int)item["health"]>0))fraction=System.Math.Min(fraction,DcV5Collision.Circle(position,end,Point(cover),0.74f)??1);
            foreach(var enemy in snapshot["enemies"].Where(item=>(int)item["room_id"]==room&&((string)item["kind"]=="elite"||(string)item["kind"]=="boss")))fraction=System.Math.Min(fraction,DcV5Collision.Circle(position,end,Point(enemy),(string)enemy["kind"]=="boss"?1.19f:0.89f)??1);
            return 0.45f*Mathf.Clamp01(fraction);
        }
        private static float Closest(Vector2 relative,Vector2 velocity,float duration)
        {
            var time=Mathf.Clamp(-Vector2.Dot(relative,velocity)/System.Math.Max(0.001f,velocity.sqrMagnitude),0,duration);return (relative+velocity*time).magnitude;
        }
        public static float Separation(Vector2 relative,Vector2 projectileVelocity,Vector2 walkingDirection,float walkingTime,float speed=5)
        {
            var velocity=projectileVelocity-walkingDirection*speed;return System.Math.Min(Closest(relative,velocity,walkingTime),Closest(relative+velocity*walkingTime,projectileVelocity,0.45f-walkingTime));
        }
        private static bool Threat(JObject snapshot,JToken player,float horizon,float clearance,Vector2? movement=null)
        {
            var position=Point(player);var room=(int)player["room_id"];
            foreach(var projectile in snapshot["projectiles"].Where(item=>(bool)item["enemy"]&&(int)item["room_id"]==room))
            {
                var relative=Point(projectile)-position;var velocity=new Vector2((float)projectile["direction_x"],(float)projectile["direction_y"])*((float?)projectile["speed"]??10)-(movement??Vector2.zero)*((bool?)snapshot["v6_stage"]==true?7:5);
                var approaching=-Vector2.Dot(relative,velocity)/System.Math.Max(0.001f,velocity.sqrMagnitude);
                if(approaching>=0&&approaching<=horizon&&Closest(relative,velocity,horizon)<clearance+((float?)projectile["radius"]??0.1f))return true;
            }
            return snapshot["enemies"].Any(item=>(int)item["room_id"]==room&&Vector2.Distance(position,Point(item))<((string)item["kind"]=="boss"?3.5f:2.5f)&&((string)item["warning"]=="melee"||(string)item["warning"]=="charge"));
        }
        public static string[] Skills(JObject snapshot,string playerID,JObject geometry=null)
        {
            var own=snapshot["players"].First(item=>(string)item["player_id"]==playerID);var actions=new List<string>();
            if((int)own["health"]<=0)return actions.ToArray();
            if((long)own["ability_remaining_ms"]==0&&(long)own["reload_remaining_ms"]==0)actions.Add("ability_primary");
            if((bool?)snapshot["v8_stage"]==true){if(DcV8CoopPilot.ShouldUseDefense(snapshot,playerID,geometry))actions.Add("ability_secondary");return actions.ToArray();}
            if((long)own["secondary_remaining_ms"]>0)return actions.ToArray();
            var ranger=(string)own["loadout_id"]=="ranger";
            var covered=snapshot["fields"].Any(item=>(int)item["room_id"]==(int)own["room_id"]&&((string)item["kind"]=="wind_bind"||!ranger)&&Vector2.Distance(Point(item),Point(own))<2.2f);
            if(covered)return actions.ToArray();
            var threatened=ranger?Threat(snapshot,own,0.9f,1):snapshot["players"].Any(item=>(int)item["health"]>0&&(int)item["shield"]==0&&Vector2.Distance(Point(item),Point(own))<=2.5f&&Threat(snapshot,item,0.7f,0.8f));
            if(threatened)actions.Add("ability_secondary");
            return actions.ToArray();
        }
        public static bool Dodge(JObject snapshot,string playerID,Vector2? movement=null)
        {
            var own=snapshot["players"].First(item=>(string)item["player_id"]==playerID);
            return (long)own["dodge_remaining_ms"]==0&&Threat(snapshot,own,0.25f,0.5f,movement);
        }
        public static bool DefenseThreat(JObject snapshot,JToken actor)=>Threat(snapshot,actor,0.75f,0.85f);
        public static int Growth(JArray candidates)
        {
            foreach(var preferred in new[]{"ward","frost","ranger_fan","runesmith_twin","runesmith_heavy","ranger_tension","runesmith_chain","ranger_pierce","vitality","swift"})
                for(var index=0;index<candidates.Count;index++)if((string)candidates[index]==preferred)return index;
            return 0;
        }
    }
}
