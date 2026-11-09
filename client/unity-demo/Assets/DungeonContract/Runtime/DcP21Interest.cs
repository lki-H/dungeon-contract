using System.Linq;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public static class DcP21Interest
    {
        public static JObject ManifestForPlayer(JObject manifest,string playerID)
        {
            var visible=(JObject)manifest.DeepClone();var requirements=new JObject();
            if(manifest["task_requirements"] is JObject source&&source[playerID] is JObject own)requirements[playerID]=own.DeepClone();
            visible["task_requirements"]=requirements;return visible;
        }
        public static JObject ForPlayer(JObject snapshot,string playerID,bool omitProjectiles=false)
        {
            var value=new JObject();var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==playerID);var room=(int)own["room_id"];
            var entityGroups=new System.Collections.Generic.HashSet<string>(DcS1Replica.Groups.Concat(new[]{"spawn_reservations","effects","rooms"}));
            foreach(var property in snapshot.Properties())if(!entityGroups.Contains(property.Name))value[property.Name]=property.Value.DeepClone();
            foreach(var group in DcS1Replica.Groups.Concat(new[]{"spawn_reservations"}))value[group]=group=="projectiles"&&omitProjectiles?new JArray():new JArray(((JArray)snapshot[group]??new JArray()).Where(item=>(int)item["room_id"]==room).Select(item=>item.DeepClone()));
            value["rooms"]=snapshot["rooms"].DeepClone();
            value["team_rooms"]=new JArray(((JArray)snapshot["players"]).Where(item=>(string)item["player_id"]!=playerID).Select(item=>new JObject{["player_id"]=item["player_id"],["room_id"]=item["room_id"],["life_state"]=item["life_state"]}));
            value["effects"]=new JArray(((JArray)snapshot["effects"]).Where(item=>(int)item["room"]==room||(string)item["actor"]==playerID).Select(source=>
            {
                var item=(JObject)source.DeepClone();
                if((int)item["room"]!=room){item["target"]=null;item["amount"]=0;item["x"]=0;item["y"]=0;}return item;
            }));
            foreach(var teammate in value["players"].Where(item=>(string)item["player_id"]!=playerID)){teammate["candidates"]=new JArray();teammate["growth"]=new JArray();teammate["choice_hash"]=null;teammate["choice_node"]=-1;teammate["choice_revision"]=0;teammate["choice_remaining_ms"]=0;}
            foreach(var state in value["rooms"])if((int)state["index"]!=room){state["enemies_remaining"]=0;state["pending"]=0;state["warning_remaining_ms"]=0;}
            return value;
        }
    }
}
