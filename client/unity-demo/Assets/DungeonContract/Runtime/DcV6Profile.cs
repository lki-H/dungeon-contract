using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
namespace DungeonContract {
 public static class DcV6Profile {
  public static ushort TestTransportPort(string role,string host,string value,bool autoplay){if(value=="12777")return 12777;if(!Active||role!="client"||host!="127.0.0.1"||!autoplay||!ushort.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var port)||port<12970||port>12989)throw new System.IO.InvalidDataException("Test relay port requires explicit local v6 autoplay");return port;}
  public static Vector2 SupplyPoint(JToken room){var item=(room["shared_objects"] as JArray)?.FirstOrDefault(v=>(string)v["id"]=="branch_supply");return item==null?new Vector2(15,6):new Vector2((float)item["position_milli"][0]/1000,(float)item["position_milli"][1]/1000);}
  public static bool Active=>DcBootstrap.Argument("-dc-runtime","v2")=="v6"||DcV7Profile.Active;
  public static bool Modern=>Active||DcBootstrap.Argument("-dc-runtime","v2")=="v5";
  public static bool P5(string id)=>id=="dc_ruins.v8.p5.1"||id=="dc_ruins.v7.p5.2"||id=="dc_ruins.v7.p5.1"||id=="dc_ruins.v5.p5.1"||id=="dc_ruins.v6.p5.1"||id=="dc_ruins.v6.p5.2";
  public static JObject Compact(JObject source) {
   var result=(JObject)source.DeepClone(); if((int?)source["config"]?["protocol_version"]!=6&&(int?)source["config"]?["protocol_version"]!=7&&(int?)source["config"]?["protocol_version"]!=8)return result;
   var world=(JObject)source["p1_world"];
   result["p1_world"]=new JObject{[(int?)source["config"]?["protocol_version"]==8?"bundled_v8":(int?)source["config"]?["protocol_version"]==7?"bundled_v7":"bundled_v6"]=true,["selected"]=new JArray(((JArray)world["rooms"]).Select(room=>new JObject{["id"]=room["id"],["variant_id"]=room["variant_id"],["exists"]=room["exists"]}))};
   return result;
  }
  public static void Expand(JObject manifest,string modelContentFolder=null) {
   var v8=(bool?)manifest["p1_world"]?["bundled_v8"]==true;var v7=v8||(bool?)manifest["p1_world"]?["bundled_v7"]==true;if(!v7&&(bool?)manifest["p1_world"]?["bundled_v6"]!=true)return;
   var folder=modelContentFolder??Path.Combine(UnityEngine.Application.dataPath,"..","Content");
   var world=JObject.Parse(File.ReadAllText(Path.Combine(folder,v8?"world-v8-p5.json":v7?"world-v7-p5.json":"world-v6-p5.json")));
   world["parameters"]=JObject.Parse(File.ReadAllText(Path.Combine(folder,v8?"parameters-v8-p5.json":v7?"parameters-v7-p5.json":"parameters-v6-p5.json")));
   foreach(var key in new[]{"p2_stage","p3_stage","p4_stage","v5_stage","v6_stage"})world[key]=true;
   if(v7)world["v7_stage"]=true;if(v8)world["v8_stage"]=true;
   var selected=(JArray)manifest["p1_world"]["selected"];if(selected.Count!=15)throw new InvalidDataException("v6 variant directory");
   foreach(var room in ((JArray)world["rooms"]).Cast<JObject>()){
    var choice=selected.Single(item=>(string)item["id"]==(string)room["id"]);
    var variant=((JArray)room["variants"]).Single(item=>(string)item["id"]==(string)choice["variant_id"]);
    room["variant_id"]=variant["id"].DeepClone();room["exists"]=choice["exists"].DeepClone();
    foreach(var key in new[]{"obstacles_milli","cover_points_milli","trap_points_milli","covers","traps","spawn_candidates_milli","mandatory_cover_groups"})room[key]=variant[key].DeepClone();room.Remove("variants");
   }
   var rooms=(JArray)world["rooms"];world["doors"]=new JArray(((JArray)world["doors"]).Where(door=>(bool)rooms[(int)door["from"]]["exists"]&&(bool)rooms[(int)door["to"]]["exists"]).Select(v=>v.DeepClone()));
   manifest["p1_world"]=world;
  }
 }
}
