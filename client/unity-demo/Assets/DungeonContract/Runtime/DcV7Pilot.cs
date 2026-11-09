using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 // Explicit autoplay diagnostics only. Uses the same legal input/actions as a human client.
 public static class DcV7Pilot {
  private sealed class Motion {public Vector2 Position,Velocity;public long At;}
  private sealed class Navigation {public string Signature,ApproachTarget;public JObject Layout,ShotLayout;public Vector4[] Rects;public long SwitchAt,DodgeAt,ApproachAt;public bool ExerciseSearched;public Vector2? ExerciseFoot,Approach;public Vector2 ApproachEnemy;public readonly Dictionary<string,Motion> Targets=new Dictionary<string,Motion>();}
  private static readonly Dictionary<string,Navigation> navigation=new Dictionary<string,Navigation>();
  public static void InteractionGuard(DcP1Input input,ref string[] actions){if(input.Interact)actions=Array.Empty<string>();}
  public static Vector2 EastDoorMove(JObject layout,JToken own){var center=(float)own["body_center_y"];var door=RoomGeometry.DoorPoint(layout,"east");return new Vector2(1,Mathf.Clamp((door.y-center)*.35f,-1,1)).normalized;}
  private static Vector2 Point(JToken p)=>new Vector2((float)p["x"],(float)p["y"]);
  private static Vector2 Center(JObject geometry,JToken p){var kind=(string)(p["loadout_id"]??p["kind"]);return Point(p)+DcV7Geometry.Profile((JObject)geometry["parameters"],kind,(string)p["body_facing"]??"down").Center;}
  private static void ResumeCombat(JToken own,ref DcP1Input input){var ready=!(bool)own["release_required"];var melee=(bool)own["melee_equipped"];input.Interact=false;input.Charge=false;input.Melee=ready&&melee;input.Attack=ready&&!melee;}
  public static bool TrapInMoveHorizon(JToken trap){
   // Relative authoritative phases also remain correct across growth safe time,
   // where the combat clock is frozen while the overall Run clock continues.
   var phase=(string)trap["phase"];var remaining=(long?)trap["remaining_ms"];
   return (phase!="cooldown"&&phase!="idle")||!remaining.HasValue||remaining<=400;
  }
  private static Vector2 FiringApproach(Navigation nav,DcV7Geometry.Body body,Vector2 center,Vector2 target,string targetID,long game,float shotRadius){
   var half=(body.Max-body.Min)/2;
   if(!nav.Approach.HasValue&&nav.ApproachTarget==targetID&&game<nav.ApproachAt&&Vector2.Distance(target,nav.ApproachEnemy)<1)return Vector2.zero;
   if(nav.Approach.HasValue&&nav.ApproachTarget==targetID&&game<nav.ApproachAt&&Vector2.Distance(target,nav.ApproachEnemy)<1){var known=DcV7Navigation.Direction(nav.Layout,center,nav.Approach.Value,half);if(known.sqrMagnitude>.01f)return known;}
   var candidates=new List<Vector2>();
   foreach(var range in new[]{3f,4f,6f,8f,10f})for(var index=0;index<32;index++){
    var point=target+new Vector2(Mathf.Cos(index*Mathf.PI/16),Mathf.Sin(index*Mathf.PI/16))*range;
    if(DcV7Geometry.BodyTerrain(nav.Layout,body,point-body.Center,Array.Empty<Vector2>())||DcV6Geometry.Terrain(nav.ShotLayout,point,target,shotRadius,true).HasValue)continue;
    candidates.Add(point);
   }
   nav.Approach=null;nav.ApproachTarget=targetID;nav.ApproachEnemy=target;nav.ApproachAt=game+1000;
   foreach(var point in candidates.OrderBy(point=>Vector2.Distance(center,point)).Take(24)){
    var direction=DcV7Navigation.Direction(nav.Layout,center,point,half);if(direction.sqrMagnitude<=.01f)continue;
    nav.Approach=point;return direction;
   }
   return Vector2.zero;
  }
  private static IEnumerable<Vector2> RescuePoints(Navigation nav,DcV7Geometry.Body body,Vector2 from,Vector2 target){
   return new[]{1.35f,1.15f,.9f}.SelectMany(radius=>Enumerable.Range(0,16).Select(i=>target+new Vector2(Mathf.Cos(i*Mathf.PI/8),Mathf.Sin(i*Mathf.PI/8))*radius)).Where(p=>!DcV7Geometry.BodyTerrain(nav.Layout,body,p,Array.Empty<Vector2>())&&RoomGeometry.ClearLine(nav.ShotLayout,p,target)).OrderBy(p=>Vector2.Distance(from,p));
  }
  private static Vector2 RescueMove(Navigation nav,DcV7Geometry.Body body,Vector2 from,Vector2 target){
   foreach(var point in RescuePoints(nav,body,from,target)){if(Vector2.Distance(from,point)<.08f)return Vector2.zero;var direction=DcV7Navigation.Direction(nav.Layout,from+body.Center,point+body.Center,(body.Max-body.Min)/2);if(direction.sqrMagnitude>.01f)return direction;}return Vector2.zero;
  }
  private static Vector2? ExercisePoint(Navigation nav,JToken layout,JObject parameters,JToken victim){
   if(nav.ExerciseSearched)return nav.ExerciseFoot;nav.ExerciseSearched=true;
   var body=DcV7Geometry.Profile(parameters,(string)victim["loadout_id"],"right");
   var offsets=Enumerable.Range(-2,5).SelectMany(x=>Enumerable.Range(-2,5).Select(y=>new Vector2(x*.1f,y*.1f))).OrderBy(v=>v.sqrMagnitude).ThenBy(v=>v.x).ThenBy(v=>v.y).ToArray();
   foreach(var point in layout["trap_points"]){var trap=RoomGeometry.Point(point);foreach(var offset in offsets){var foot=trap-body.Center+offset;if(!DcV7Geometry.BodyTerrain(nav.ShotLayout,body,foot,Array.Empty<Vector2>())&&DcV7Geometry.Overlap(body.Vertices,foot,DcV7Geometry.Rect(-.75f,-.5f,1.5f,1),trap)){nav.ExerciseFoot=foot;return foot;}}}return null;
  }
  private static JToken MechanismGate(JObject snapshot,JToken layout,int room,Vector2 center,out JToken group){
   foreach(var candidate in layout["mandatory_cover_groups"]??new JArray()){
    var authored=layout["covers"].Where(c=>candidate["cover_ids"].Any(k=>(string)k==(string)c["id"])).ToArray();
    var gate=snapshot["covers"].Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0&&authored.Any(a=>Vector2.Distance(Point(c),new Vector2((float)a["x_milli"]/1000,(float)a["y_milli"]/1000))<.01f)).OrderBy(c=>Vector2.Distance(center,Point(c))).FirstOrDefault();
    if(gate!=null){group=candidate;return gate;}
   }group=null;return null;
  }
  private static Vector2 CombatMove(JObject snapshot,Navigation nav,JToken own,DcV7Geometry.Body body,Vector2 ground,Vector2 target,Vector2 previous,JObject geometry){
   var center=ground+body.Center;var room=(int)own["room_id"];var shots=snapshot["projectiles"].Where(p=>(int)p["room_id"]==room&&(bool)p["enemy"]).Select(p=>(point:Point(p),velocity:new Vector2((float)p["direction_x"],(float)p["direction_y"])*(float)p["speed"],radius:(float)p["radius"])).ToArray();var enemies=snapshot["enemies"].Where(e=>(int)e["room_id"]==room).ToArray();var foes=enemies.Select(e=>Center(geometry,e)).ToArray();var preferred=(string)own["loadout_id"]=="ranger"?8.5f:7;var orbit=new Vector2(-(target-center).y,(target-center).x).normalized*((int)own["slot"]%2==0?1:-1);var best=float.MinValue;var choice=Vector2.zero;
   for(var index=0;index<49;index++){var move=index==48?Vector2.zero:new Vector2(Mathf.Cos(index%16*Mathf.PI/8),Mathf.Sin(index%16*Mathf.PI/8));var travel=index<16?2.45f:index<32?1.0f:.35f;var horizon=index==48?.35f:travel/7;var delta=move*travel;var end=ground+delta;if(DcV7Geometry.BodyTerrain(nav.Layout,body,end,Array.Empty<Vector2>()))continue;var blocked=false;foreach(var r in nav.Rects){if(Math.Max(ground.x,end.x)+body.Max.x<r.x||Math.Min(ground.x,end.x)+body.Min.x>r.x+r.z||Math.Max(ground.y,end.y)+body.Max.y<r.y||Math.Min(ground.y,end.y)+body.Min.y>r.y+r.w)continue;if(DcV7Geometry.Sweep(body.Vertices,ground,end,DcV7Geometry.Rect(r.x,r.y,r.z,r.w),default,true).HasValue){blocked=true;break;}}if(blocked)continue;
    var endpoint=end+body.Center;var nearest=foes.Min(p=>Vector2.Distance(endpoint,p));var score=Math.Min(4,nearest)*2-Math.Abs(Vector2.Distance(endpoint,target)-preferred)*1.5f+Vector2.Dot(move,orbit)*.6f+Vector2.Dot(move,previous)*.1f;
    foreach(var shot in shots){var relative=shot.point-ground;var hit=DcV7Geometry.Circle(relative,relative+shot.velocity*horizon-delta,body.Vertices,shot.radius+.08f);if(hit.HasValue)score-=150-50*hit.Value;}
    foreach(var foe in enemies){var kind=(string)foe["warning"];var at=Center(geometry,foe);if(kind=="melee"&&Vector2.Distance(endpoint,at)<((string)foe["kind"]=="boss"?4.2f:2.6f))score-=80;if(kind=="charge"&&DcV7Geometry.Circle(at-ground,at+new Vector2((float)foe["aim_x"],(float)foe["aim_y"])*4-ground-delta,body.Vertices,.8f).HasValue)score-=80;}
    if(score>best){best=score;choice=move;}
   }return choice;
  }
  public static void Adapt(JObject snapshot,JObject geometry,string id,ref DcP1Input input,ref string[] actions,bool rescue){
   if((bool?)snapshot["v7_stage"]!=true)return;var own=snapshot["players"].First(p=>(string)p["player_id"]==id);if((int)own["health"]<=0)return;var room=(int)own["room_id"];var ground=Point(own);var center=Center(geometry,own);var layout=(JObject)geometry["rooms"][room];var body=DcV7Geometry.Profile((JObject)geometry["parameters"],(string)own["loadout_id"],(string)own["body_facing"]??"down");
   var foes=snapshot["enemies"].Where(e=>(int)e["room_id"]==room).ToArray();
   var cleared=(string)snapshot["rooms"][room]["status"]=="cleared";var unfinished=!cleared&&layout["objective"] is JArray&&!(bool)snapshot["rooms"][room]["mechanism_complete"];
   // Earlier drivers may already be holding the mechanism. Stop that ordinary
   // input while enemies remain; actual damage still interrupts it on the server.
   if(input.Interact&&!rescue){if(!unfinished||foes.Length==0)return;ResumeCombat(own,ref input);}
   JToken gate=null,group=null;if(unfinished&&foes.Length<=2)gate=MechanismGate(snapshot,layout,room,center,out group);
   var objective=unfinished&&(foes.Length==0||gate!=null);
   var dangerTraps=snapshot["traps"].Where(t=>(int)t["room_id"]==room&&TrapInMoveHorizon(t)).ToArray();
   var signature=string.Join(",",snapshot["covers"].Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0).Select(c=>(string)c["id"]))+"|"+string.Join(",",dangerTraps.Select(t=>(string)t["id"]));var key=(string)snapshot["run_id"]+":"+room;
   // Cooldown traps are timed crossings, not permanent walls. Rebuild only
   // when the set threatening the next move changes; warnings remain avoided.
   if(navigation.Count>32)navigation.Clear();if(!navigation.TryGetValue(key,out var nav)||nav.Signature!=signature){var map=RoomGeometry.NavigationLayout(layout);foreach(var c in snapshot["covers"].Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0))((JArray)map["obstacles"]).Add(new JArray((float)c["x"]-.4375f,(float)c["y"]-.4375f,.875f,.875f));var shots=(JObject)map.DeepClone();foreach(var t in dangerTraps)((JArray)map["obstacles"]).Add(new JArray((float)t["x"]-.75f,(float)t["y"]-.5f,1.5f,1));navigation[key]=nav=new Navigation{Signature=signature,Layout=map,ShotLayout=shots,Rects=((JArray)map["obstacles"]).Select(r=>new Vector4((float)r[0],(float)r[1],(float)r[2],(float)r[3])).ToArray()};}
   if(rescue){
    input.Attack=input.Melee=input.Charge=false;input.Interact=false;
    var target=snapshot["players"].Where(p=>(int)p["room_id"]==room&&(string)p["life_state"]=="downed").OrderBy(p=>Vector2.Distance(ground,Point(p))).FirstOrDefault();
    if(target!=null){var point=Point(target);var range=(float)geometry["parameters"]["player"]["rescue_range_milli_tiles"]/1000-.05f;input.Interact=Vector2.Distance(ground,point)<=range&&RoomGeometry.ClearLine(nav.ShotLayout,ground,point)&&!DcV7Geometry.BodyTerrain(nav.Layout,body,ground,Array.Empty<Vector2>());input.Move=input.Interact?Vector2.zero:RescueMove(nav,body,ground,point);}
    else if(room==2&&cleared){var victim=snapshot["players"].First(p=>(int)p["slot"]==0);var foot=ExercisePoint(nav,layout,(JObject)geometry["parameters"],victim);input.Aim=0;input.Move=Vector2.zero;if(foot.HasValue)input.Move=(int)own["slot"]==0?(Vector2.Distance(ground,foot.Value)<.08f?Vector2.zero:DcV7Navigation.Direction(nav.ShotLayout,center,foot.Value+body.Center,(body.Max-body.Min)/2)):RescueMove(nav,body,ground,foot.Value);}
    return;
   }
   if(cleared||((string)layout["kind"]=="corridor")||objective){
    string desired=null;Vector2? supply=null;
    if(objective){
     var target=RoomGeometry.Point(layout["objective"]);
     input.Attack=input.Melee=input.Charge=false;input.Interact=false;
     if(gate!=null){
      var stand=RoomGeometry.Point(group["outside_approach"]);input.Move=Vector2.Distance(ground,stand)>.3f?DcV7Navigation.Direction(nav.Layout,center,stand+body.Center,(body.Max-body.Min)/2):Vector2.zero;
      var direction=Point(gate)-center;input.Aim=(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(direction.y,direction.x),Mathf.PI*2)/(Mathf.PI*2)*65535);
      if(Vector2.Distance(ground,stand)<.6f){var game=(long)snapshot["elapsed_ms"];if((bool)own["melee_equipped"]){if(game>=nav.SwitchAt){actions=actions.Concat(new[]{"switch_weapon"}).Distinct().ToArray();nav.SwitchAt=game+500;}}else input.Attack=!(bool)own["release_required"];}
     }else if(Vector2.Distance(ground,target)<1.2f){input.Move=Vector2.zero;input.Interact=true;}
     else input.Move=DcV7Navigation.Direction(nav.Layout,center,target+body.Center,(body.Max-body.Min)/2);
     return;
    }
    if(room==2||room==4){var branch=room==2?6:7;if((bool)snapshot["rooms"][branch]["exists"]&&(string)snapshot["rooms"][branch]["status"]!="cleared")desired="south";}
    if(room==6||room==7)desired="north";
    if(room==7&&!(bool)own["branch_supply_used"]&&(int)own["health"]<(int)own["max_health"]){desired=null;supply=DcV6Profile.SupplyPoint(layout);}
    if(room==13||room==14){var branch=room==13?6:7;desired=(string)snapshot["rooms"][branch]["status"]=="cleared"?"north":"south";}
    if(desired==null&&!supply.HasValue){var door=geometry["doors"].FirstOrDefault(d=>(int)d["from"]==room&&(int)d["to"]!=13&&(int)d["to"]!=14);if(door!=null)desired=(string)door["side"];}
    if(desired!=null||supply.HasValue){
     var outward=desired=="east"?Vector2.right:desired=="west"?Vector2.left:desired=="north"?Vector2.up:Vector2.down;var target=supply??RoomGeometry.DoorPoint(layout,desired)-outward*.8f;
     var envelope=((JObject)geometry["parameters"]["body_profiles"]).Properties().Where(p=>(string)p.Value["kind"]==(string)own["loadout_id"]).Select(p=>new Vector2(((float)p.Value["bounds_milli"][2]-(float)p.Value["bounds_milli"][0])/2000,((float)p.Value["bounds_milli"][3]-(float)p.Value["bounds_milli"][1])/2000)).Aggregate(Vector2.zero,Vector2.Max);
     if(supply.HasValue&&Vector2.Distance(ground,target)<1.2f){input.Move=Vector2.zero;input.Interact=true;}
     else if(desired!=null&&Vector2.Distance(center,target)<.5f)input.Move=outward;
     else input.Move=DcV7Navigation.Direction(nav.Layout,center,target,envelope);
     input.Attack=false;input.Melee=false;input.Charge=false;
    }
    var choices=((JArray)geometry["doors"]).Where(d=>(int)d["from"]==room||(int)d["to"]==room).Select(d=>{var side=(int)d["from"]==room?(string)d["side"]:RoomGeometry.Opposite((string)d["side"]);var outward=side=="east"?Vector2.right:side=="west"?Vector2.left:side=="north"?Vector2.up:Vector2.down;var point=RoomGeometry.DoorPoint(layout,side)-body.Center;return(side,outward,point);}).OrderBy(x=>Vector2.Distance(ground,x.point)).ToArray();
    foreach(var door in choices)if(Vector2.Distance(center,RoomGeometry.DoorPoint(layout,door.side))<2.4f&&Vector2.Dot(input.Move,door.outward)>-.2f){var tangent=new Vector2(-door.outward.y,door.outward.x);var lateral=Vector2.Dot(center-RoomGeometry.DoorPoint(layout,door.side),tangent);input.Move=Math.Abs(lateral)>.12f?-tangent*Math.Sign(lateral):door.outward;break;}
   }
   else if(foes.Length>0){
    var target=foes.OrderBy(e=>(string)e["kind"]=="boss"?-100:Vector2.Distance(center,Center(geometry,e))).First();var goal=Center(geometry,target);
    if(unfinished){var aim=goal-center;ResumeCombat(own,ref input);input.Aim=(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(aim.y,aim.x),Mathf.PI*2)/(Mathf.PI*2)*65535);}
    if(!RoomGeometry.ClearLine(nav.ShotLayout,center,goal)){
     input.Move=DcV7Navigation.Direction(nav.Layout,center,goal,(body.Max-body.Min)/2);
     // The enemy's footprint can be unreachable although a legal firing point
     // exists outside its pocket. Do not keep shooting from a blocked corner.
     if(input.Move.sqrMagnitude<=.01f){var game=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];var ranged=((JArray)geometry["parameters"]["loadouts"]).First(kit=>(string)kit["id"]==(string)own["loadout_id"])["ranged"];input.Move=FiringApproach(nav,body,center,goal,(string)target["id"],game,(float)ranged["projectile_radius_milli_tiles"]/1000);}
    }
    else input.Move=CombatMove(snapshot,nav,own,body,ground,goal,input.Move,geometry);
    if((bool?)snapshot["v8_stage"]==true){
     // Keep explicit QA squad members within one another's real defensive range.
     // Follow by legal navigation; ordinary players and rescue holds are untouched.
     var leader=snapshot["players"].Where(p=>(int)p["room_id"]==room&&(int)p["health"]>0&&(bool?)p["connected"]!=false).OrderBy(p=>(int)p["slot"]).First();var rendezvous=Center(geometry,leader);
     if((string)leader["player_id"]!=id&&Vector2.Distance(center,rendezvous)>1.6f){var follow=DcV7Navigation.Direction(nav.Layout,center,rendezvous,(body.Max-body.Min)/2);if(follow.sqrMagnitude>.01f)input.Move=follow;}
    }
   }
   var avoidanceMove=input.Move;var observedGame=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];
   if(foes.Length>0&&observedGame>=nav.DodgeAt&&avoidanceMove.sqrMagnitude>.01f&&(long)own["dodge_remaining_ms"]==0&&snapshot["projectiles"].Any(p=>(int)p["room_id"]==room&&(bool)p["enemy"]&&DcV7Geometry.Circle(Point(p)-ground,Point(p)+new Vector2((float)p["direction_x"],(float)p["direction_y"])*(float)p["speed"]*.3f-ground-avoidanceMove*2.1f,body.Vertices,(float)p["radius"]).HasValue)){actions=actions.Concat(new[]{"dodge"}).Distinct().ToArray();nav.DodgeAt=observedGame+500;}
   if(input.Attack||input.Melee){
    var oldDir=DcP1World.Aim(input.Aim);var target=foes.OrderBy(e=>Math.Abs(Vector2.SignedAngle(oldDir,Point(e)-ground))).FirstOrDefault();var cover=snapshot["covers"].Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0).OrderBy(c=>Math.Abs(Vector2.SignedAngle(oldDir,Point(c)-ground))).FirstOrDefault();
    var oldIsEnemy=target!=null&&Math.Abs(Vector2.SignedAngle(oldDir,Point(target)-ground))<15;var aim=oldIsEnemy?Center(geometry,target)-center:cover!=null&&Math.Abs(Vector2.SignedAngle(oldDir,Point(cover)-ground))<15?Point(cover)-center:oldDir;
    if(oldIsEnemy&&!input.Melee){var targetID=(string)target["id"];var at=(long?)snapshot["dynamic_elapsed_ms"]??(long)snapshot["elapsed_ms"];var targetCenter=Center(geometry,target);if(nav.Targets.Count>24)nav.Targets.Clear();if(!nav.Targets.TryGetValue(targetID,out var previous))nav.Targets[targetID]=previous=new Motion{Position=targetCenter,At=at};if(at>previous.At){previous.Velocity=Vector2.ClampMagnitude((targetCenter-previous.Position)*1000/(at-previous.At),12);previous.Position=targetCenter;previous.At=at;}if((long?)target["warning_remaining_ms"]<=0)aim+=previous.Velocity*Math.Min(.7f,Math.Max(0,aim.magnitude-.9f)/((string)own["loadout_id"]=="ranger"?20:10));}
    input.Aim=(ushort)Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(aim.y,aim.x),Mathf.PI*2)/(Mathf.PI*2)*65535);
   }
  }
 }
}
