using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcP3Pilot
    {
        public static bool Rescue(JObject snapshot,JObject geometry,string playerID,ref DcP1Input input,bool trapExercise)
        {
            var own=snapshot["players"].First(item=>(string)item["player_id"]==playerID);var room=(int)own["room_id"];var position=Point(own);
            if((int)own["health"]<=0){input.Move=Vector2.zero;input.Attack=false;input.Interact=false;return true;}
            if(trapExercise&&room==2&&(string)snapshot["rooms"][2]["status"]=="cleared")
            {
                var trap=RoomGeometry.Point(geometry["rooms"][2]["trap_points"][0]);var destination=(int)own["slot"]==0?trap:trap+Vector2.right*1.35f;
                input.Move=Vector2.Distance(position,destination)<0.08f?Vector2.zero:RoomGeometry.PathDirection((JObject)geometry["rooms"][2],position,destination,0.3f);input.Attack=false;input.Melee=false;
                input.Interact=(int)own["slot"]!=0&&input.Move==Vector2.zero&&snapshot["players"].Any(item=>(int)item["slot"]==0&&(string)item["life_state"]=="downed");return true;
            }
            var target=snapshot["players"].Where(item=>(string)item["life_state"]=="downed"&&(int)item["room_id"]==room).OrderBy(item=>Vector2.Distance(position,Point(item))).FirstOrDefault();
            if(target==null)return false;
            var v8=(bool?)snapshot["v8_stage"]==true;
            if((bool?)snapshot["v7_stage"]==true&&Vector2.Distance(position,Point(target))>1.4f&&snapshot["enemies"].Any(e=>(int)e["room_id"]==room&&Vector2.Distance(position,Point(e))<4)&&(!v8||((string)own["rescue_target"]!=(string)target["player_id"]&&!DcV8CoopPilot.RescueSupportAvailable(snapshot,own,geometry))))return false;
            if((bool?)snapshot["v5_stage"]==true)
            {
                var helper=snapshot["players"].Where(item=>(int)item["health"]>0&&(int)item["room_id"]==room).OrderBy(item=>v8&&(string)item["rescue_target"]==(string)target["player_id"]?0:1).ThenBy(item=>(int)item["health"]<35?1:0).ThenBy(item=>v8&&(string)item["loadout_id"]=="ranger"&&DcV8CoopPilot.RescueSupportAvailable(snapshot,item,geometry)?0:1).ThenBy(item=>Vector2.Distance(Point(item),Point(target))).ThenBy(item=>(int)item["slot"]).First();
                if((string)helper["player_id"]!=playerID)return false;
            }
            var point=Point(target);var layout=(JObject)geometry["rooms"][room];
            if((bool?)snapshot["v5_stage"]==true)
            {
                layout=(JObject)layout.DeepClone();foreach(var cover in snapshot["covers"].Where(item=>(int)item["room_id"]==room&&(int)item["health"]>0))((JArray)layout["obstacles"]).Add(new JArray((float)cover["x"]-0.45f,(float)cover["y"]-0.45f,0.9f,0.9f));
                input.Interact=Vector2.Distance(position,point)<=1.4f&&RoomGeometry.ClearLine(layout,position,point);
                if(!input.Interact){var approach=Enumerable.Range(0,16).Select(index=>point+new Vector2(Mathf.Cos(index*Mathf.PI/8),Mathf.Sin(index*Mathf.PI/8))).Where(candidate=>!RoomGeometry.Blocked(layout,candidate,0.3f)&&RoomGeometry.ClearLine(layout,candidate,point)).OrderBy(candidate=>Vector2.Distance(position,candidate)).ToArray();if(approach.Length==0)return false;point=approach[0];}
            }
            else input.Interact=Vector2.Distance(position,point)<=1.4f;
            input.Attack=false;input.Melee=false;input.Move=input.Interact?Vector2.zero:RoomGeometry.PathDirection(layout,position,point,0.3f);return true;
        }
        private static Vector2 Point(JToken value)=>new Vector2((float)value["x"],(float)value["y"]);
    }
}
