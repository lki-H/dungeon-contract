using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 public sealed partial class DcP1World {
  private DcV7Geometry.Body v7MovingBody;
  private string v7ExecutingAction;
  private DcV7Geometry.Body Body(Player p){if(p.CachedBody==null||p.CachedBody.Kind!=p.Loadout||p.CachedBody.Facing!=p.BodyFacing)p.CachedBody=DcV7Geometry.Profile(parameters,p.Loadout,p.BodyFacing);return p.CachedBody;}
  private DcV7Geometry.Body Body(Enemy e){if(e.CachedBody==null||e.CachedBody.Kind!=e.Kind||e.CachedBody.Facing!=e.BodyFacing)e.CachedBody=DcV7Geometry.Profile(parameters,e.Kind,e.BodyFacing);return e.CachedBody;}
  private Vector2 Center(Player p)=>V7?p.Position+Body(p).Center:p.Position;
  private Vector2 Center(Enemy e)=>V7?e.Position+Body(e).Center:e.Position;
  private IEnumerable<Vector2> V7SolidCovers(int room)=>covers.Where(c=>c.Room==room&&CoverSolid(c)).Select(c=>c.Position);
  private bool V7Blocked(int room,DcV7Geometry.Body body,Vector2 at)=>DcV7Geometry.BodyTerrain(Layout(room),body,at,V6Covers(room,at,at,Math.Max(body.Max.magnitude,body.Min.magnitude)).Where(CoverSolid).Select(c=>c.Position));
  private bool V7ActorOverlap(DcV7Geometry.Body body,Vector2 at,Enemy other)=>DcV7Geometry.OverlapBodies(body,at,Body(other),other.Position);
  private void V7Facing(Player p){var next=DcV7Geometry.Facing(Aim(p.Input.Aim));if(next!=p.BodyFacing){var b=DcV7Geometry.Profile(parameters,p.Loadout,next);if(!V7Blocked(p.Room,b,p.Position)&&!enemies.Any(e=>e.Health>0&&e.Room==p.Room&&(e.Kind=="elite"||e.Kind=="boss")&&V7ActorOverlap(b,p.Position,e)))p.BodyFacing=next;}}
  private void V7Facing(Enemy e){var target=P21EnemyTarget(e);var committed=e.WarningUntil>0&&e.WarningUntil-combatNow<=200||e.ChargeUntil>combatNow||(e.V5State?.MeleeEndTick??0)>WorldTick;var aim=committed||target==null?e.Aim:Center(target)-Center(e);if(aim.sqrMagnitude<.0001f)return;var next=DcV7Geometry.Facing(aim);if(next==e.BodyFacing)return;var b=DcV7Geometry.Profile(parameters,e.Kind,next);if(!V7Blocked(e.Room,b,e.Position)&&!enemies.Any(o=>o.ID!=e.ID&&o.Health>0&&o.Room==e.Room&&DcV7Geometry.OverlapBodies(b,e.Position,Body(o),o.Position)))e.BodyFacing=next;}
  private DcV6Geometry.Contact? V7BodyContact(int room,DcV7Geometry.Body body,Vector2 from,Vector2 to,bool separating=true){
   DcV6Geometry.Contact? best=null;
   void Pick(DcV6Geometry.Contact? hit,string kind,string id=null){if(!hit.HasValue)return;var h=hit.Value;h.Kind=kind;h.ID=id;if(!best.HasValue||h.Time<best.Value.Time)best=h;}
   var layout=Layout(room);var width=(float)layout["width"];var height=(float)layout["height"];
   Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(-100,-100,101,height+200),default,separating),"wall");
   Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(width-1,-100,101,height+200),default,separating),"wall");
   Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(-100,-100,width+200,101),default,separating),"wall");
   Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(-100,height-1,width+200,101),default,separating),"wall");
   foreach(var r in (JArray)layout["obstacles"]){var x=(float)r[0];var y=(float)r[1];var w=(float)r[2];var h=(float)r[3];if(Math.Max(from.x,to.x)+body.Max.x<x||Math.Min(from.x,to.x)+body.Min.x>x+w||Math.Max(from.y,to.y)+body.Max.y<y||Math.Min(from.y,to.y)+body.Min.y>y+h)continue;Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(x,y,w,h),default,separating),"wall");}
   foreach(var cover in V6Covers(room,from,to,Math.Max(body.Min.magnitude,body.Max.magnitude)))if(CoverSolid(cover))Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(-.4375f,-.4375f,.875f,.875f),cover.Position,separating),"cover",cover.ID);
   return best;
  }
  private Vector2 MoveV7Body(int room,DcV7Geometry.Body body,Vector2 start,Vector2 delta,Func<Vector2,bool> extra=null,Action<Vector2,Vector2> trace=null){
   var at=start;
   for(var pass=0;pass<3&&delta.sqrMagnitude>.00000001f;pass++){
    var hit=V7BodyContact(room,body,at,at+delta);var t=hit?.Time??1;
    if(extra!=null){var steps=Math.Max(1,Mathf.CeilToInt(delta.magnitude/.05f));for(var i=1;i<=steps;i++)if(extra(at+delta*(i/(float)steps))){t=Math.Min(t,(i-1)/(float)steps);if(!hit.HasValue)hit=new DcV6Geometry.Contact{Time=t,Normal=-delta.normalized};break;}}
    var before=at;at+=delta*Math.Max(0,t-.0001f);trace?.Invoke(before,at);if(!hit.HasValue||t>=1)break;delta*=1-t;var normal=hit.Value.Normal;delta-=normal*Math.Min(0,Vector2.Dot(delta,normal));
   }
   return at;
  }
  private void MoveV7Player(Player p){
   if(p.DashSource!=null&&now>=p.DodgeUntil)EndV6Dash(p,p.DodgeUntil);
   V7Facing(p);var body=Body(p);var remaining=1f/30;var startMS=(double)now;var dashTime=Math.Max(0,Math.Min(remaining,(p.DodgeUntil-startMS)/1000));
   Action<Vector2,Vector2> trace=null;
   if(p.Room==4&&!p.Reached.Contains("inspect_zone"))trace=(from,to)=>{if(V7InspectSegment(from,to))p.V7InspectCrossed=true;};
   bool EnemyBlock(Vector2 at)=>enemies.Any(e=>e.Health>0&&e.Room==p.Room&&(e.Kind=="elite"||e.Kind=="boss")&&V7ActorOverlap(body,at,e)&&Vector2.Distance(at,e.Position)<Vector2.Distance(p.Position,e.Position));
   if(dashTime>0&&p.DashSource!=null){
    var before=p.Position;var end=p.Position+p.DodgeDirection*p.DodgeSpeed*(float)dashTime;var hit=V7BodyContact(p.Room,body,p.Position,end);
    foreach(var e in enemies.Where(e=>e.Health>0&&e.Room==p.Room&&(e.Kind=="elite"||e.Kind=="boss"))){var h=DcV7Geometry.Sweep(body.Vertices,p.Position,end,Body(e).Vertices,e.Position,true);if(h.HasValue&&(!hit.HasValue||h.Value.Time<hit.Value.Time))hit=h;}
    var door=V6DoorCrossing(p,p.Position,end);
    if(door.HasValue&&(!hit.HasValue||door.Value.time<=hit.Value.Time)){p.Position=Vector2.Lerp(p.Position,end,door.Value.time);trace?.Invoke(before,p.Position);EndV6Dash(p);TransferV6(p,door.Value.destination,door.Value.side);return;}
    if(hit.HasValue){p.Position=Vector2.Lerp(p.Position,end,Math.Max(0,hit.Value.Time-.0001f));remaining-=(float)dashTime*hit.Value.Time;EndV6Dash(p,startMS+dashTime*1000*hit.Value.Time);}
    else{p.Position=end;remaining-=(float)dashTime;if(startMS+dashTime*1000>=p.DodgeUntil-.001)EndV6Dash(p,p.DodgeUntil);}
    trace?.Invoke(before,p.Position);
   }
   if(remaining>0)p.Position=MoveV7Body(p.Room,body,p.Position,p.Input.Move*Distance(parameters["player"],"move_milli_tiles_per_second")*V5Movement(p)*remaining,EnemyBlock,trace);
  }
  private Vector2 V7MoveEnemy(Enemy e,Vector2 delta)=>MoveV7Body(e.Room,Body(e),e.Position,delta,p=>WeaponWindBlocks(e,p)||EnemyOverlap(e,p));
  private static readonly float[] v7WalkAngles={45f,-45f,90f,-90f};
  private Vector2 WalkV7Enemy(Enemy enemy,Vector2 delta){
   var start=enemy.Position;var direct=V7MoveEnemy(enemy,delta);var length=delta.magnitude;
   if(length<.0001f||Vector2.Distance(start,direct)>=length*.5f||!EnemyOverlap(enemy,start+delta))return direct;
   // Static paths do not include moving actors. Try bounded local headings only
   // when a neighbour blocks the step; every candidate uses the same hull sweep,
   // wind/actor blockers and movement length. Charges and forced pushes stay exact.
   var heading=delta/length;var best=direct;var bestScore=Vector2.Dot(direct-start,heading)+Vector2.Distance(start,direct)*.25f;
   foreach(var angle in v7WalkAngles){
    var candidate=V7MoveEnemy(enemy,Rotate(delta,angle));var travel=candidate-start;var score=Vector2.Dot(travel,heading)+travel.magnitude*.25f;
    if(score>bestScore+.00001f){best=candidate;bestScore=score;}
   }
   return best;
  }
  private bool V7SpawnLegal(int room,Vector2 point,string recovering,string reservationID,string kindOverride=null){
   if(IsConnector(room))return false;var kind=kindOverride??enemies.FirstOrDefault(e=>e.ID==recovering)?.Kind??spawnReservations.FirstOrDefault(s=>s.ID==reservationID)?.Kind??(rooms[room].Pending.Count>0?rooms[room].Pending.Peek():"guard");
   var body=DcV7Geometry.Profile(parameters,kind,"down");if(V7Blocked(room,body,point))return false;
   if(players.Values.Any(p=>!p.Left&&(p.Health>0||p.Life=="downed")&&p.Room==room&&Vector2.Distance(Center(p),point+body.Center)<2))return false;
   if(enemies.Any(e=>e.ID!=recovering&&e.Health>0&&e.Room==room&&DcV7Geometry.OverlapBodies(body,point,Body(e),e.Position)))return false;
   if(spawnReservations.Any(s=>s.ID!=reservationID&&s.Room==room&&DcV7Geometry.OverlapBodies(body,point,DcV7Geometry.Profile(parameters,s.Kind,"down"),s.Position)))return false;
   if(traps.Any(t=>t.Room==room&&DcV7Geometry.Overlap(body.Vertices,point,DcV7Geometry.Rect(-.875f,-.625f,1.75f,1.25f),t.Position)))return false;
   var objective=Layout(room)["objective"];if(objective is JArray&&Vector2.Distance(point+body.Center,RoomGeometry.Point(objective))<2)return false;
   foreach(var d in extension["doors"]){var forward=(int)d["from"]==room;if(!forward&&(int)d["to"]!=room)continue;if(Vector2.Distance(point+body.Center,RoomGeometry.DoorPoint(Layout(room),forward?(string)d["side"]:RoomGeometry.Opposite((string)d["side"])))<2)return false;}return true;
  }
  private Vector2? V7SpawnPoint(int room,string decision,string kind,string recovering=null){
   var candidates=Layout(room)["spawn_candidates"] as JArray;if(candidates==null)return null;var seed=Convert.ToUInt32(Rank(decision,"spawn_order").Substring(0,8),16);
   var legal=candidates.Select(c=>c is JArray?RoomGeometry.Point(c):new Vector2((float)c["x_milli"]/1000,(float)c["y_milli"]/1000)).Where(c=>V7SpawnLegal(room,c,recovering,null,kind)).OrderByDescending(c=>SharedVisibleEdge(room,c)).ThenByDescending(c=>SharedVisible(room,c)).ThenBy(c=>SpawnRank(seed,Mathf.RoundToInt(c.x*1000),Mathf.RoundToInt(c.y*1000))).ToArray();return legal.Length>0?legal[0]:(Vector2?)null;
  }
  private void QueueV7RecoveryPlacement(string id){var enemy=enemies.First(e=>e.ID==id);if(enemy.RecoveryPlacementUntil==0)enemy.RecoveryPlacementUntil=combatNow+Number(extension["encounter"],"room_spawn_block_timeout_ms",5000);enemy.RecoveryPlacementRetryAt=combatNow+250;enemy.ReadyAt=long.MaxValue;}
  private bool TickV7RecoveryPlacements(){foreach(var enemy in enemies.Where(e=>e.Health>0&&e.RecoveryPlacementUntil>0).ToArray()){if(combatNow<enemy.RecoveryPlacementRetryAt)continue;var point=V7SpawnPoint(enemy.Room,enemy.ID+":placement_retry",enemy.Kind,enemy.ID);if(point.HasValue){enemy.RecoveryPlacementUntil=0;enemy.RecoveryPlacementRetryAt=0;ReserveSpawn(enemy.Room,enemy.Kind,point.Value,enemy.Summon,enemy.ID);}else{if(combatNow>=enemy.RecoveryPlacementUntil){AbortTechnical();return false;}enemy.RecoveryPlacementRetryAt=combatNow+250;}}return true;}
  private float? V7MeleeContact(Player p,MeleeV5 a,Vector2 targetFrom,Vector2 targetTo,Vector2[] shape,bool parry=false){
   var blade=p.Loadout=="ranger";var inner=blade?.4f+(a.Range-.4f)*.35f:Math.Max(.4f,a.Range-.6f);
   var contact=DcV7Geometry.VisibleWeapon(p.TickStartCenter,Center(p),a.V7AngleStart,a.V7AngleEnd,inner,a.Range,blade?.08f:.3f,targetFrom,targetTo,shape);
   if(parry)return contact;var shaft=DcV7Geometry.Weapon(p.TickStartCenter,Center(p),a.V7AngleStart,a.V7AngleEnd,.4f,inner,.035f,targetFrom,targetTo,shape);
   return !contact.HasValue?shaft:!shaft.HasValue?contact:Math.Min(contact.Value,shaft.Value);
  }
  private float V7MeleeRange(Player p,bool heavy){
   if(heavy){var c=Kit(p.Loadout)["melee"]["charge"];var basis=V8?Number(c,"wave_radius_milli_tiles"):2875;var extra=V8?(double)Number(c,"wave_growth_exact_milli_numerator")/Number(c,"wave_growth_exact_denominator"):437.5;return (float)(Math.Round(basis+(Has(p,"runesmith_heavy")?extra:0),MidpointRounding.AwayFromZero)/1000);}
   if(p.Loadout=="ranger")return (float)(Math.Round((4000+(Has(p,"ranger_pierce")?700:0))/3d,MidpointRounding.AwayFromZero)/1000);
   return V8?Distance(Kit(p.Loadout)["melee"],"range_milli_tiles"):3.25f;
  }
  private Vector2 V7SlamImpact(Player p){var from=Center(p);var end=from+Aim(p.Input.Aim)*(V8?Distance(Kit(p.Loadout)["melee"]["charge"],"landing_distance_milli_tiles"):1.875f);var hit=V7Terrain(p.Room,from,end,.1f,true);return hit.HasValue?Vector2.Lerp(from,end,Math.Max(0,hit.Value.Time-.0001f)):end;}
  private void V7MeleeAngles(Player p,MeleeV5 a){
   if(a.V7AngleTick==WorldTick)return;var heading=Mathf.Atan2(a.Direction.y,a.Direction.x)*Mathf.Rad2Deg;
   if(a.Heavy){a.V7AngleStart=a.V7AngleEnd=heading;a.V7Aim=heading;a.V7AngleTick=WorldTick;return;}
   var step=a.Arc/Math.Max(1,a.RecoveryTick-a.ActiveTick);var first=a.V7AngleTick<a.ActiveTick;
   a.V7AngleStart=first?heading-a.Arc/2:a.V7AngleEnd;a.V7AngleEnd=a.V7AngleStart+(first?0:Mathf.DeltaAngle(a.V7Aim,heading))+step;a.V7Aim=heading;a.V7AngleTick=WorldTick;
  }
  private DcV6Geometry.Contact? V7Terrain(int room,Vector2 from,Vector2 to,float radius,bool separating){
   var best=DcV6Geometry.Terrain(Layout(room),from,to,radius,separating);
   foreach(var c in V6Covers(room,from,to,radius+.2f)){if(!CoverSolid(c))continue;var h=DcV6Geometry.Box(from,to,c.Position.x-.4375f,c.Position.y-.4375f,.875f,.875f,radius,separating);if(h.HasValue&&(!best.HasValue||h.Value.Time<best.Value.Time)){var item=h.Value;item.Kind="cover";item.ID=c.ID;best=item;}}
   return best;
  }
  private bool V7EnemyStrike(Enemy e,Player p,Vector2 origin,float range,float arc){var c=origin+Body(e).Center;var heading=Mathf.Atan2(e.Aim.y,e.Aim.x)*Mathf.Rad2Deg;return DcV7Geometry.Weapon(c,c,heading-arc/2,heading+arc/2,0,range,0,p.Position,p.Position,Body(p).Vertices).HasValue&&ClearLine(Layout(e.Room),c,Center(p));}
  private bool V7ChargeStrike(Enemy e,Player p)=>DcV7Geometry.Sweep(Body(e).Vertices,e.TickStart-p.TickStart,e.Position-p.Position,Body(p).Vertices).HasValue;
  private (float time,int destination,string side)? V7DoorCrossing(Player p,Vector2 from,Vector2 to){
   if(Practice||now<p.DoorAt||!p.Connected||now<p.ProtectedUntil)return null;var body=Body(p);var layout=Layout(p.Room);
   foreach(var door in (JArray)extension["doors"]){var forward=(int)door["from"]==p.Room;if(!forward&&(int)door["to"]!=p.Room)continue;var side=forward?(string)door["side"]:RoomGeometry.Opposite((string)door["side"]);var target=forward?(int)door["to"]:(int)door["from"];if(!CanTraverse(p.Room,target))continue;
    var direction=side=="east"?Vector2.right:side=="west"?Vector2.left:side=="north"?Vector2.up:Vector2.down;var tangent=new Vector2(-direction.y,direction.x);var center=RoomGeometry.DoorPoint(layout,side);var plane=center;
    if(side=="east")plane.x=(float)layout["width"]-1-body.Max.x-.001f;else if(side=="west")plane.x=1-body.Min.x+.001f;else if(side=="north")plane.y=(float)layout["height"]-1-body.Max.y-.001f;else plane.y=1-body.Min.y+.001f;
    var intent=p.DashSource!=null&&now<p.DodgeUntil?p.DodgeDirection:p.Input.Move;if(Vector2.Dot(intent,direction)<=.4f)continue;var denominator=Vector2.Dot(to-from,direction);var beyond=Vector2.Dot(from-plane,direction);float t;if(beyond>=-.001f&&beyond<.5f)t=0;else{if(denominator<=0)continue;t=Vector2.Dot(plane-from,direction)/denominator;if(t<0||t>1)continue;}
    var at=Vector2.Lerp(from,to,t);var min=body.Vertices.Min(v=>Vector2.Dot(at+v-center,tangent));var max=body.Vertices.Max(v=>Vector2.Dot(at+v-center,tangent));if(min>=-1&&max<=1)return(t,target,side);
   }return null;
  }
  private Vector2 V7EntryPoint(Player p,int destination,string fromSide){
   var side=RoomGeometry.Opposite(fromSide);var layout=Layout(destination);var body=Body(p);var center=RoomGeometry.DoorPoint(layout,side);var at=center-body.Center;
   if(side=="west")at.x=1-body.Min.x+.15f;else if(side=="east")at.x=(float)layout["width"]-1-body.Max.x-.15f;else if(side=="south")at.y=1-body.Min.y+.15f;else at.y=(float)layout["height"]-1-body.Max.y-.15f;return at;
  }
  private bool V7InspectSegment(Vector2 from,Vector2 to){var region=Layout(4)["regions"].First(r=>(string)r["id"]=="inspect_zone");var center=new Vector2((float)region["center_milli"][0],(float)region["center_milli"][1])/1000;return DcV6Geometry.Circle(from,to,center,(float)region["radius_milli"]/1000).HasValue;}
  private bool V7InInspectZone(Player p)=>p.V7InspectCrossed||V7InspectSegment(p.Position,p.Position);
  private void V7Snapshot(JObject value,bool cachedFlights=false){
   Put(value,"v7_stage",true);Put(value,"schema_version",7);
   foreach(var group in new[]{"players","enemies"})foreach(var item in ((JArray)value[group]).Cast<JObject>()){
    var player=group=="players";DcV7Geometry.Body b;Vector2 c;
    if(player){var p=players[(string)item["player_id"]];b=Body(p);c=Center(p);Put(item,"emission_index",p.EmissionIndex);Put(item,"aim_x",Aim(p.Input.Aim).x);Put(item,"aim_y",Aim(p.Input.Aim).y);}
    else{var e=enemies.First(e=>e.ID==(string)item["id"]);b=Body(e);c=Center(e);}
    Put(item,"body_profile_id",b.ID);Put(item,"body_facing",b.Facing);Put(item,"body_center_x",c.x);Put(item,"body_center_y",c.y);
    if(item["melee_action"] is JObject a&&player){var m=players[(string)item["player_id"]].MeleeAttack;Put(a,"angle_start",m.V7AngleStart);Put(a,"angle_end",m.V7AngleEnd);}
   }
   foreach(var f in ((JArray)value["fields"]).Cast<JObject>()){var p=players[(string)f["owner"]];var c=Center(p);Put(f,"x",c.x);Put(f,"y",c.y);}
   foreach(var t in ((JArray)value["traps"]).Cast<JObject>()){Put(t,"shape","rectangle");Put(t,"width",1.5f);Put(t,"height",1f);}
   foreach(var c in ((JArray)value["covers"]).Cast<JObject>()){Put(c,"shape","rectangle");Put(c,"width",.875f);Put(c,"height",.875f);}
   var index=0;foreach(var item in ((JArray)value["projectiles"]).Cast<JObject>()){var p=projectiles[index++];if(cachedFlights&&p.RuntimeSnapshotBound)continue;Put(item,"born_game_ms",p.BornGameMS);Put(item,"expires_game_ms",p.ExpiresGameMS);Put(item,"emission_index",p.EmissionIndex);Put(item,"attack_episode_id",p.Episode);Put(item,"unlimited_bounces",p.RuneBolt);}
  }
 }
}
