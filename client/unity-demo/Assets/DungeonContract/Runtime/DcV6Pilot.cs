using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 // Test-only driver. The entry point is called exclusively behind the explicit autoplay flag.
 // It emits ordinary input/actions, never hits, task facts, health or world state.
 public static class DcV6Pilot {
  private sealed class Cache {public string Signature;public JObject Layout;}
  private sealed class Tactic{public Vector2 Goal;public long Until;}
  private static readonly Dictionary<string,Tactic> tactics=new Dictionary<string,Tactic>();
  private sealed class Motion{public Vector2 Point,Velocity;public long At;}
  private static readonly Dictionary<string,Motion> motion=new Dictionary<string,Motion>();
  private static readonly Dictionary<string,Cache> layouts=new Dictionary<string,Cache>();
  private static readonly Dictionary<string,long> switches=new Dictionary<string,long>();
  private static double nextEvidence;
  private static Vector2 Point(JToken t)=>new Vector2((float)t["x"],(float)t["y"]);
  private static float Segment(Vector2 p,Vector2 a,Vector2 b){var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Math.Max(0.0001f,d.sqrMagnitude)));}
  public static void Route(JObject snapshot,JObject geometry,string id,ref DcP1Input input,ref string[] actions,bool rescueApproach=false){
   if(!rescueApproach)RouteCore(snapshot,geometry,id,ref input,ref actions);if((bool?)snapshot["v7_stage"]!=true)Evidence(snapshot,geometry,id,input,actions);
  }
  private static bool Wield(JObject snapshot,JToken own,string id,bool melee,ref DcP1Input input,ref string[] actions){
   if((bool)own["melee_equipped"]==melee)return true;input.Attack=false;input.Melee=false;var key=(string)snapshot["run_id"]+":"+id;var game=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];if(switches.Count>16)switches.Clear();if(!switches.TryGetValue(key,out var next)||game>=next){actions=actions.Concat(new[]{"switch_weapon"}).Distinct().ToArray();switches[key]=game+500;}return false;
  }
  public static void Evidence(JObject snapshot,JObject geometry,string id,DcP1Input input,string[] actions){
   var path=DcBootstrap.Argument("-dc-v6-driver-evidence");if(string.IsNullOrEmpty(path)||Time.realtimeSinceStartupAsDouble<nextEvidence)return;nextEvidence=Time.realtimeSinceStartupAsDouble+5;path=System.IO.Path.GetFullPath(path);if(!path.StartsWith(System.IO.Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new System.IO.InvalidDataException("Driver evidence outside isolated cache");var own=snapshot["players"].First(p=>(string)p["player_id"]==id);var room=(int)own["room_id"];var value=new JObject{["scope"]="input_controller_own_room_diagnostics",["human"]=false,["input_only"]=true,["frame"]=snapshot["world_frame_id"],["game_ms"]=snapshot["elapsed_ms"],["own"]=new JObject{["room"]=room,["x"]=own["x"],["y"]=own["y"],["health"]=own["health"],["weapon"]=own["active_weapon_id"],["release_required"]=own["release_required"],["body_facing"]=own["body_facing"],["secondary_remaining_ms"]=own["secondary_remaining_ms"],["ability_remaining_ms"]=own["ability_remaining_ms"],["rescue_target"]=own["rescue_target"],["rescue_remaining_ms"]=own["rescue_remaining_ms"]},["input"]=new JObject{["move_x"]=input.Move.x,["move_y"]=input.Move.y,["aim"]=input.Aim,["attack"]=input.Attack,["melee"]=input.Melee,["interact"]=input.Interact,["actions"]=new JArray(actions)},["enemies"]=new JArray(snapshot["enemies"].Where(e=>(int)e["room_id"]==room).Take(24).Select(e=>e.DeepClone())),["covers"]=new JArray(snapshot["covers"].Where(e=>(int)e["room_id"]==room&&(int)e["health"]>0).Take(128).Select(e=>e.DeepClone())),["traps"]=new JArray(snapshot["traps"].Where(t=>(int)t["room_id"]==room).Take(64).Select(t=>t.DeepClone())),["layout"]=geometry["rooms"][room].DeepClone()};System.IO.File.WriteAllText(path,value.ToString(Newtonsoft.Json.Formatting.None));
  }
  private static void RouteCore(JObject snapshot,JObject geometry,string id,ref DcP1Input input,ref string[] actions){
   var own=snapshot["players"].First(p=>(string)p["player_id"]==id);if((int)own["health"]<=0||input.Interact)return;
   var room=(int)own["room_id"];var position=Point(own);var baseLayout=(JObject)geometry["rooms"][room];var covers=((JArray)snapshot["covers"]).Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0).ToArray();
   var signature=string.Join(",",covers.Select(c=>(string)c["id"]));var key=(string)snapshot["run_id"]+":"+room;
   if(layouts.Count>32)layouts.Clear();if(!layouts.TryGetValue(key,out var cache)||cache.Signature!=signature){var next=RoomGeometry.NavigationLayout(baseLayout);foreach(var c in covers)((JArray)next["obstacles"]).Add(new JArray((float)c["x"]-0.45f,(float)c["y"]-0.45f,0.9f,0.9f));layouts[key]=cache=new Cache{Signature=signature,Layout=next};}
   var foes=((JArray)snapshot["enemies"]).Where(e=>(int)e["room_id"]==room).ToArray();Vector2? goal=null;string side=null;
   if((string)snapshot["rooms"][room]["status"]=="cleared"){
    if(room==2||room==4){var branch=room==2?6:7;if((bool)snapshot["rooms"][branch]["exists"]&&(string)snapshot["rooms"][branch]["status"]!="cleared")side="south";}
    if(room==6||room==7)side="north";
    if(room==7&&!(bool)own["branch_supply_used"]&&(int)own["health"]<(int)own["max_health"]){goal=DcV6Profile.SupplyPoint(baseLayout);side=null;}
    if(room==13||room==14){var branch=room==13?6:7;side=(string)snapshot["rooms"][branch]["status"]=="cleared"?"north":"south";}
    if(side==null&&!goal.HasValue){var door=geometry["doors"].FirstOrDefault(d=>(int)d["from"]==room&&(int)d["to"]!=13&&(int)d["to"]!=14);if(door!=null)side=(string)door["side"];}
    if(side!=null)goal=RoomGeometry.DoorPoint(baseLayout,side);
   }else if(foes.Length<=2&&baseLayout["objective"] is JArray objective&&!(bool)snapshot["rooms"][room]["mechanism_complete"])goal=RoomGeometry.Point(objective);
   if(goal.HasValue){
    if(side!=null&&Vector2.Distance(position,goal.Value)<0.5f){input.Move=side=="east"?Vector2.right:side=="west"?Vector2.left:side=="north"?Vector2.up:Vector2.down;input.Attack=false;return;}
    if(side==null&&Vector2.Distance(position,goal.Value)<1.2f){input.Move=Vector2.zero;input.Attack=false;input.Interact=true;return;}
    var path=RoomGeometry.PathDirection(cache.Layout,position,goal.Value,0.24f);input.Move=path;input.Attack=false;input.Melee=false;input.Charge=false;
    if(path.sqrMagnitude>0.01f)return;
   }
   var v8=(bool?)snapshot["v8_stage"]==true;var kit=((JArray)geometry["parameters"]["loadouts"]).First(k=>(string)k["id"]==(string)own["loadout_id"]);var firingPosition=v8?position+DcV7Geometry.Profile((JObject)geometry["parameters"],(string)own["loadout_id"],(string)own["body_facing"]??"down").Center:position;var meleeRange=(float)kit["melee"]["range_milli_tiles"]/1000;var ranged=(string)own["loadout_id"]=="runesmith";var radius=(float)kit["ranged"]["projectile_radius_milli_tiles"]/1000;var game=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];
   Func<JToken,Vector2> lead=enemy=>{var key=(string)snapshot["run_id"]+":"+(string)enemy["id"];var point=Point(enemy);if(motion.Count>512)motion.Clear();if(!motion.TryGetValue(key,out var m))motion[key]=m=new Motion{Point=point,At=game};if(game>m.At){m.Velocity=Vector2.ClampMagnitude((point-m.Point)*1000/(game-m.At),12);m.At=game;m.Point=point;}var velocity=(long?)enemy["warning_remaining_ms"]>0?Vector2.zero:m.Velocity;return point+velocity*Math.Min(0.8f,Math.Max(0,Vector2.Distance(position,point)-0.9f)/(ranged?10:20));};
   Func<JToken,bool> canShoot=enemy=>{var point=Point(enemy);var hit=1f;if(v8){var body=DcV7Geometry.Profile((JObject)geometry["parameters"],(string)enemy["kind"],(string)enemy["body_facing"]??"down");var center=point+body.Center;hit=DcV7Geometry.CircleBody(firingPosition-point,center-point,body,radius)??1;point=center;}else hit=DcV5Collision.Circle(position,point,point,radius+((string)enemy["kind"]=="boss"?0.9f:(string)enemy["kind"]=="elite"?0.6f:0.42f))??1;var wall=DcV6Geometry.Terrain(baseLayout,firingPosition,point,radius,true);if(wall.HasValue&&wall.Value.Time<hit)return false;return !covers.Any(c=>v8?(DcV6Geometry.Box(firingPosition,point,(float)c["x"]-.4375f,(float)c["y"]-.4375f,.875f,.875f,radius,true)?.Time??2)<hit:(DcV5Collision.Circle(position,point,Point(c),radius+0.45f)??2)<hit);};
   var visible=foes.Where(canShoot).OrderBy(e=>(bool?)snapshot["v7_stage"]==true&&(string)e["kind"]=="boss"?-100:Vector2.Distance(position,Point(e))+((string)e["kind"]=="archer"?-3:0)).FirstOrDefault();
   if(visible!=null&&!goal.HasValue){var aimed=lead(visible)-position;input.Aim=(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(aimed.y,aimed.x),Mathf.PI*2)/(Mathf.PI*2)*65535);if(!Wield(snapshot,own,id,false,ref input,ref actions))return;input.Attack=true;input.Melee=false;if((bool)own["release_required"]){input.Attack=false;input.Melee=false;}return;}
   if(foes.Length>0&&visible==null&&!goal.HasValue){
    var tacticEnemy=foes.OrderBy(e=>Vector2.Distance(position,Point(e))).First();var tacticKey=(string)snapshot["run_id"]+":"+id;
    if(tactics.Count>16)tactics.Clear();if(!tactics.TryGetValue(tacticKey,out var tactic)||game>=tactic.Until||Vector2.Distance(position,tactic.Goal)<0.5f){
     var candidates=new List<Vector2>();foreach(var distance in new[]{4f,6f})for(var i=0;i<16;i++){var q=Point(tacticEnemy)+new Vector2(Mathf.Cos(i*Mathf.PI/8),Mathf.Sin(i*Mathf.PI/8))*distance;if(RoomGeometry.Blocked(cache.Layout,q,0.24f)||snapshot["traps"].Any(t=>(int)t["room_id"]==room&&Vector2.Distance(q,Point(t))<1.05f))continue;
      var hit=DcV5Collision.Circle(q,Point(tacticEnemy),Point(tacticEnemy),radius+0.42f)??1;var wall=DcV6Geometry.Terrain(baseLayout,q,Point(tacticEnemy),radius,true);if(wall.HasValue&&wall.Value.Time<hit||covers.Any(c=>(DcV5Collision.Circle(q,Point(tacticEnemy),Point(c),radius+0.45f)??2)<hit))continue;candidates.Add(q);}
     var selected=candidates.OrderBy(q=>Vector2.Distance(position,q)).FirstOrDefault(q=>RoomGeometry.PathDirection(cache.Layout,position,q,0.24f).sqrMagnitude>0.01f);if(selected!=Vector2.zero)tactics[tacticKey]=tactic=new Tactic{Goal=selected,Until=game+2000};
    }
    if(tactic!=null){var navigation=RoomGeometry.PathDirection(cache.Layout,position,tactic.Goal,0.24f);if(navigation.sqrMagnitude>0.01f){input.Move=navigation;}}
   }
   var nearBlocked=input.Move.sqrMagnitude>0.01f&&RoomGeometry.Blocked(cache.Layout,position+input.Move*0.35f,0.24f);
   var blockedShot=foes.Length>0&&visible==null;
   if(!goal.HasValue&&!nearBlocked&&!blockedShot)return;
   var target=foes.OrderBy(e=>Vector2.Distance(position,Point(e))).FirstOrDefault();
   var cover=covers.Where(c=>{var point=Point(c);if(!RoomGeometry.ClearLine(baseLayout,position,point))return false;var hit=DcV5Collision.Circle(position,point,point,radius+0.45f)??1;var terrain=DcV6Geometry.Terrain(baseLayout,position,point,radius,true);var shoot=(!terrain.HasValue||terrain.Value.Time>hit)&&!covers.Any(other=>other!=c&&(DcV5Collision.Circle(position,point,Point(other),radius+0.45f)??2)<hit);return shoot||Vector2.Distance(position,point)<(ranged?2.4f:1.8f);}).OrderBy(c=>goal.HasValue?Vector2.Distance(Point(c),goal.Value)+Vector2.Distance(Point(c),position):target!=null?Segment(Point(c),position,Point(target))*4+Vector2.Distance(Point(c),position):Vector2.Distance(Point(c),position)).FirstOrDefault();
   if(cover==null)return;var direction=Point(cover)-position;input.Aim=(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(direction.y,direction.x),Mathf.PI*2)/(Mathf.PI*2)*65535);input.Attack=!(bool)own["melee_equipped"];input.Melee=(bool)own["melee_equipped"];if((bool)own["release_required"]){input.Attack=false;input.Melee=false;}
   var meleeReach=v8?Vector2.Distance(firingPosition,Point(cover))-.4375f*(Math.Abs((Point(cover)-firingPosition).normalized.x)+Math.Abs((Point(cover)-firingPosition).normalized.y))<meleeRange-.1f:direction.magnitude<(ranged?2.4f:1.8f);
   if(meleeReach&&(!v8||foes.Length==0)){input.Move=Vector2.zero;if(!Wield(snapshot,own,id,true,ref input,ref actions))return;input.Attack=false;input.Melee=!(bool)own["release_required"];return;}
   if(!Wield(snapshot,own,id,false,ref input,ref actions))return;input.Attack=!(bool)own["release_required"];input.Melee=false;
   if(goal.HasValue){var approach=Point(cover)-direction.normalized*1.7f;input.Move=Vector2.Distance(position,Point(cover))<3?Vector2.zero:RoomGeometry.PathDirection(cache.Layout,position,approach,0.24f);}
  }
 }
}
