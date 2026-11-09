using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public static class DcP1Definition
    {
        public static JObject NormalizeGeometry(JObject geometry)
        {
            var normalized=(JObject)geometry.DeepClone();
            foreach(var room in (JArray)normalized["rooms"])if(room["obstacles_milli"] is JArray rectangles)room["obstacles"]=new JArray(rectangles.Select(rect=>new JArray(rect.Select(value=>(float)value/1000f))));
            if(((string)normalized["definition_id"])?.StartsWith("dc_rooms.v8",StringComparison.Ordinal)==true||((string)normalized["definition_id"])?.StartsWith("dc_rooms.v6",StringComparison.Ordinal)==true||((string)normalized["definition_id"])?.StartsWith("dc_rooms.v7",StringComparison.Ordinal)==true)foreach(var room in ((JArray)normalized["rooms"]).Cast<JObject>())room["v6_geometry"]=true;
            foreach(var room in ((JArray)normalized["rooms"]).Cast<JObject>())foreach(var key in new[]{"spawn","objective","cover_points","trap_points","spawn_candidates"})if(room[key+"_milli"] is JArray points){room[key]=key=="spawn"||key=="objective"?new JArray(points.Select(v=>(float)v/1000)):new JArray(points.Cast<JArray>().Select(row=>new JArray(row.Select(v=>(float)v/1000))));}
            return normalized;
        }
        public static JObject Simulation(JObject parameters)
        {
            var result=(JObject)parameters.DeepClone();
            result["operation"]=new JObject { ["reinforcements"]=0,["hard_deadline_ms"]=parameters["clocks"]["active_limit_ms"] };
            result["authority"]=new JObject { ["max_alive_enemies"]=parameters["budgets"]["global_alive_enemies"],["max_projectiles"]=parameters["budgets"]["projectiles"],["max_turrets"]=parameters["budgets"]["turrets"],["input_timeout_ms"]=parameters["network"]["input_timeout_wall_ms"] };
            result["world"]=new JObject { ["generator_version"]="dc_rooms.v3.p1" };
            result["trial"]=new JObject();
            result["player"]["interaction_radius_milli_tiles"]=parameters["player"]["interaction_range_milli_tiles"];
            result["player"]["defense_radius_milli_tiles"]=parameters["player"]["defense_range_milli_tiles"];
            result["upgrades"]=parameters["growth"].DeepClone();
            foreach(var kit in (JArray)result["loadouts"])
            {
                foreach(var pair in new[]{("turret_damage","damage"),("turret_range_milli_tiles","range_milli_tiles"),("turret_attack_interval_ms","interval_ms")}) kit[pair.Item1]=kit["primary"][pair.Item2]??0;
                kit["projectile_speed_milli_tiles_per_second"]=kit["ranged"]["projectile_speed_milli_tiles_per_second"];
            }
            foreach(var room in (JArray)result["rooms"])
            {
                var weights=(string)room["id"]=="sanctum"?new[]{5,3,2}:new[]{6,3,1};
                room["waves"]=new JArray(((JArray)room["waves"]).Select(count=>new JObject { ["count"]=count,["mix"]=new JArray("guard","archer","charger"),["weights"]=new JArray(weights) }));
            }
            return result;
        }
    }
}
