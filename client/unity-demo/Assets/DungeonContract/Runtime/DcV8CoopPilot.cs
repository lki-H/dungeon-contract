using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 // Explicit automated-input policy only. Never changes the world or resources.
 public static class DcV8CoopPilot {
  private static readonly Dictionary<string,long> requested=new Dictionary<string,long>();
  private static Vector2 Point(JToken actor)=>new Vector2((float?)actor["body_center_x"]??(float)actor["x"],(float?)actor["body_center_y"]??(float)actor["y"]);
  private static bool Alive(JToken actor)=>(int)actor["health"]>0&&(bool?)actor["left"]!=true&&(bool?)actor["connected"]!=false;
  private static bool Ready(JToken actor)=>Alive(actor)&&(long?)actor["secondary_remaining_ms"]==0;
  private static bool Threat(JObject state,JToken actor,JObject geometry){
   if(geometry==null)return DcV5Pilot.DefenseThreat(state,actor);
   var room=(int)actor["room_id"];var foot=new Vector2((float)actor["x"],(float)actor["y"]);var body=DcV7Geometry.Profile((JObject)geometry["parameters"],(string)actor["loadout_id"],(string)actor["body_facing"]??"down");
   foreach(var shot in state["projectiles"].Where(p=>(int)p["room_id"]==room&&(bool)p["enemy"])){
    var from=new Vector2((float)shot["x"],(float)shot["y"]);var to=from+new Vector2((float)shot["direction_x"],(float)shot["direction_y"])*(float)shot["speed"]*.75f;
    if(DcV7Geometry.Circle(from-foot,to-foot,body.Vertices,(float)shot["radius"]+.15f).HasValue)return true;
   }
   return state["enemies"].Any(e=>(int)e["room_id"]==room&&((string)e["warning"]=="melee"||(string)e["warning"]=="charge")&&Vector2.Distance(Point(actor),Point(e))<((string)e["kind"]=="boss"?4.5f:3));
  }
  public static void FilterCombatActions(JObject state,JObject geometry,string id,DcP1Input input,ref string[] actions){
   var own=state["players"].First(p=>(string)p["player_id"]==id);if((string)own["loadout_id"]!="ranger"||!actions.Contains("ability_primary"))return;
   if((long?)own["dodge_duration_remaining_ms"]>0)return;
   var room=(int)own["room_id"];var body=DcV7Geometry.Profile((JObject)geometry["parameters"],"ranger",(string)own["body_facing"]??"down");var start=new Vector2((float)own["x"],(float)own["y"]);var distance=(float)((JArray)geometry["parameters"]["loadouts"]).First(k=>(string)k["id"]=="ranger")["primary"]["dash_distance_milli_tiles"]/1000;var end=start+input.Move.normalized*distance;
   var layout=(JObject)geometry["rooms"][room];var safe=input.Move.sqrMagnitude>.01f&&!actions.Contains("dodge")&&!DcV7Geometry.BodyTerrain(layout,body,end,Array.Empty<Vector2>());
   if(safe)foreach(var rect in layout["obstacles"])if(DcV7Geometry.Sweep(body.Vertices,start,end,DcV7Geometry.Rect((float)rect[0],(float)rect[1],(float)rect[2],(float)rect[3]),default,true).HasValue){safe=false;break;}
   if(safe)foreach(var cover in state["covers"].Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0))if(DcV7Geometry.Sweep(body.Vertices,start,end,DcV7Geometry.Rect(-.4375f,-.4375f,.875f,.875f),new Vector2((float)cover["x"],(float)cover["y"]),true).HasValue){safe=false;break;}
   var center=end+body.Center;
   if(safe&&state["enemies"].Any(e=>(int)e["room_id"]==room&&Vector2.Distance(center,Point(e))<((string)e["kind"]=="boss"?5:2.8f)))safe=false;
   if(safe&&state["traps"].Any(t=>(int)t["room_id"]==room&&DcV7Pilot.TrapInMoveHorizon(t)&&DcV7Geometry.Overlap(body.Vertices,end,DcV7Geometry.Rect(-.75f,-.5f,1.5f,1),new Vector2((float)t["x"],(float)t["y"]))))safe=false;
   if(!safe)actions=actions.Where(a=>a!="ability_primary").ToArray();
  }
  private static int Rank(JToken actor)=>((string)actor["loadout_id"]=="ranger"?0:4)+(int)actor["slot"];
  private static bool Clear(JObject state,JObject geometry,int room,Vector2 from,Vector2 to){
   if(geometry!=null&&DcV6Geometry.Terrain((JObject)geometry["rooms"][room],from,to,.01f,true).HasValue)return false;
   return !state["covers"].Any(cover=>(int)cover["room_id"]==room&&(int)cover["health"]>0&&DcV6Geometry.Box(from,to,(float)cover["x"]-.4375f,(float)cover["y"]-.4375f,.875f,.875f,.01f,true).HasValue);
  }
  private static bool Covered(JObject state,JToken target,JObject geometry){
   var room=(int)target["room_id"];var at=Point(target);
   if(state["fields"].Any(field=>(int)field["room_id"]==room&&(string)field["kind"]=="wind_bind"&&Vector2.Distance(Point(field),at)<=2.2f&&Clear(state,geometry,room,Point(field),at)))return true;
   return state["players"].Any(actor=>Alive(actor)&&(int)actor["room_id"]==room&&(bool?)(actor["death_hammer"] as JObject)?["active"]==true&&Vector2.Distance(Point(actor),at)<=2.2f&&Clear(state,geometry,room,Point(actor),at));
  }
  public static bool RescueSupportAvailable(JObject state,JToken actor,JObject geometry)=>Ready(actor)||Covered(state,actor,geometry);
  public static bool ShouldUseDefense(JObject state,string id,JObject geometry=null){
   var own=state["players"].First(actor=>(string)actor["player_id"]==id);if(!Ready(own))return false;var room=(int)own["room_id"];
   var friends=state["players"].Where(actor=>Alive(actor)&&(int)actor["room_id"]==room).ToArray();
   foreach(var target in friends.Where(actor=>Vector2.Distance(Point(own),Point(actor))<=2.2f&&Clear(state,geometry,room,Point(own),Point(actor))&&!Covered(state,actor,geometry)&&Threat(state,actor,geometry))){
    var leader=friends.Where(actor=>Ready(actor)&&Vector2.Distance(Point(actor),Point(target))<=2.2f&&Clear(state,geometry,room,Point(actor),Point(target))).OrderBy(Rank).First();
    if((string)leader["player_id"]==id)return true;
   }
   return false;
  }
  public static void PrepareRescue(JObject state,string id,ref DcP1Input input,ref string[] actions,JObject geometry=null){
   if(!ShouldUseDefense(state,id,geometry))return;
   input.Interact=false;var key=(string)state["run_id"]+":"+id;var game=(long?)state["dynamic_elapsed_ms"]??(long)state["elapsed_ms"];
   if(requested.TryGetValue(key,out var at)&&game<at){actions=Array.Empty<string>();return;}
   if(requested.Count>=16&&!requested.ContainsKey(key))requested.Clear();requested[key]=game+500;actions=new[]{"ability_secondary"};
  }
 }
}
