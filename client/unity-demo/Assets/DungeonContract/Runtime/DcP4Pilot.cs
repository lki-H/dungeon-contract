using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcP4Pilot
    {
        public static void Route(JObject snapshot,JObject geometry,string playerID,ref DcP1Input input)
        {
            var own=snapshot["players"].First(item=>(string)item["player_id"]==playerID);var room=(int)own["room_id"];
            if((int)own["health"]<=0||(string)snapshot["rooms"][room]["status"]!="cleared")return;
            string side=null;
            if(room==2||room==4){var branch=room==2?6:7;if((bool)snapshot["rooms"][branch]["exists"]&&(string)snapshot["rooms"][branch]["status"]!="cleared")side="south";}
            if(room==6||room==7)
            {
                if(room==7&&!(bool)own["branch_supply_used"]&&(int)own["health"]<(int)own["max_health"])
                {
                    var position=new Vector2((float)own["x"],(float)own["y"]);var supply=(bool?)snapshot["v6_stage"]==true?DcV6Profile.SupplyPoint(geometry["rooms"][7]):new Vector2(15,6);
                    input.Attack=false;input.Interact=Vector2.Distance(position,supply)<1.2f;input.Move=input.Interact?Vector2.zero:RoomGeometry.PathDirection((JObject)geometry["rooms"][room],position,supply,0.3f);return;
                }
                side="north";
            }
            if(room==13||room==14){var branch=room==13?6:7;if((string)snapshot["rooms"][branch]["status"]=="cleared")side="north";}
            if(side==null)return;
            var layout=(JObject)geometry["rooms"][room];var point=RoomGeometry.DoorPoint(layout,side);var player=new Vector2((float)own["x"],(float)own["y"]);
            input.Attack=false;input.Interact=false;input.Move=Vector2.Distance(player,point)<0.6f?(side=="south"?Vector2.down:Vector2.up):RoomGeometry.PathDirection(layout,player,point,0.3f);
        }
    }
}
