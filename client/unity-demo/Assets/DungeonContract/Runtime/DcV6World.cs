using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public static class DcV6Geometry {
  private sealed class Grid {
   public int Width,Height;public Vector4[] Rects;public readonly Dictionary<Vector2Int,List<int>> Cells=new Dictionary<Vector2Int,List<int>>();public int[] Seen;public int Stamp;
   public Grid(JObject room){Width=(int)room["width"];Height=(int)room["height"];Rects=((JArray)room["obstacles"]).Select(r=>new Vector4((float)r[0],(float)r[1],(float)r[2],(float)r[3])).ToArray();Seen=new int[Rects.Length];for(var i=0;i<Rects.Length;i++){var r=Rects[i];for(var x=Mathf.FloorToInt(r.x/2);x<=Mathf.FloorToInt((r.x+r.z)/2);x++)for(var y=Mathf.FloorToInt(r.y/2);y<=Mathf.FloorToInt((r.y+r.w)/2);y++){var key=new Vector2Int(x,y);if(!Cells.TryGetValue(key,out var list))Cells[key]=list=new List<int>();list.Add(i);}}}
   public IEnumerable<Vector4> Query(Vector2 a,Vector2 b,float radius){if(Stamp==int.MaxValue){Array.Clear(Seen,0,Seen.Length);Stamp=0;}var stamp=++Stamp;for(var x=Mathf.FloorToInt((Math.Min(a.x,b.x)-radius)/2);x<=Mathf.FloorToInt((Math.Max(a.x,b.x)+radius)/2);x++)for(var y=Mathf.FloorToInt((Math.Min(a.y,b.y)-radius)/2);y<=Mathf.FloorToInt((Math.Max(a.y,b.y)+radius)/2);y++)if(Cells.TryGetValue(new Vector2Int(x,y),out var list))foreach(var index in list)if(Seen[index]!=stamp){Seen[index]=stamp;yield return Rects[index];}}
   public bool BlockedPoint(Vector2 point,float radius){for(var x=Mathf.FloorToInt((point.x-radius)/2);x<=Mathf.FloorToInt((point.x+radius)/2);x++)for(var y=Mathf.FloorToInt((point.y-radius)/2);y<=Mathf.FloorToInt((point.y+radius)/2);y++)if(Cells.TryGetValue(new Vector2Int(x,y),out var list))foreach(var index in list){var r=Rects[index];if((point-new Vector2(Mathf.Clamp(point.x,r.x,r.x+r.z),Mathf.Clamp(point.y,r.y,r.y+r.w))).sqrMagnitude<radius*radius)return true;}return false;}
  }
  private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Grid> grids=new System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Grid>();
  private static Grid GetGrid(JObject room)=>grids.GetValue(room,key=>new Grid(key));
  public static bool Blocked(JObject room,Vector2 point,float radius){var grid=GetGrid(room);if(point.x-radius<1||point.y-radius<1||point.x+radius>grid.Width-1||point.y+radius>grid.Height-1)return true;return grid.BlockedPoint(point,radius);}
  public struct Contact {public float Time; public Vector2 Normal; public string Kind;public string ID;}
  public static int Quantize(float t)=>t>1?int.MaxValue:Mathf.Clamp(Mathf.RoundToInt(t*65535),0,65535);
  public static float? Circle(Vector2 a,Vector2 b,Vector2 center,float radius,bool separating=false) {
   var offset=a-center;var delta=b-a;if(separating&&offset.sqrMagnitude<=radius*radius&&Vector2.Dot(offset,delta)>0)return null;
   return DcV5Collision.Circle(a,b,center,radius);
  }
  private static void PickContact(ref Contact? best,Contact? candidate){if(!candidate.HasValue)return;if(!best.HasValue||Quantize(candidate.Value.Time)<Quantize(best.Value.Time))best=candidate;else if(Quantize(candidate.Value.Time)==Quantize(best.Value.Time)){var v=best.Value;v.Normal=(v.Normal+candidate.Value.Normal).normalized;best=v;}}
  private static void PickBoxContact(ref Contact? best,float time,Vector2 normal){if(time>=0&&time<=1)PickContact(ref best,new Contact{Time=time,Normal=normal,Kind="wall"});}
  private static void PickCorner(ref Contact? best,Vector2 a,Vector2 b,Vector2 corner,float radius){var t=Circle(a,b,corner,radius);if(t.HasValue)PickBoxContact(ref best,t.Value,(Vector2.Lerp(a,b,t.Value)-corner).normalized);}
  public static Contact? Box(Vector2 a,Vector2 b,float x,float y,float w,float h,float radius,bool separating=false) {
   var margin=radius+0.00001f;if(Math.Max(a.x,b.x)+margin<x||Math.Min(a.x,b.x)-margin>x+w||Math.Max(a.y,b.y)+margin<y||Math.Min(a.y,b.y)-margin>y+h)return null;
   var closest=new Vector2(Mathf.Clamp(a.x,x,x+w),Mathf.Clamp(a.y,y,y+h));var offset=a-closest;var delta=b-a;
   if(offset.sqrMagnitude<=radius*radius+1e-7f){var normal=offset.normalized;if(normal.sqrMagnitude<0.01f){var gap=a.x-x;normal=Vector2.left;if(x+w-a.x<gap){gap=x+w-a.x;normal=Vector2.right;}if(a.y-y<gap){gap=a.y-y;normal=Vector2.down;}if(y+h-a.y<gap)normal=Vector2.up;}if(separating&&Vector2.Dot(delta,normal)>=-1e-7f)return null;return new Contact{Time=0,Normal=normal,Kind="wall"};}
   Contact? best=null;
   if(delta.x>1e-8f){var t=(x-radius-a.x)/delta.x;var at=a.y+delta.y*t;if(at>=y&&at<=y+h)PickBoxContact(ref best,t,Vector2.left);}else if(delta.x<-1e-8f){var t=(x+w+radius-a.x)/delta.x;var at=a.y+delta.y*t;if(at>=y&&at<=y+h)PickBoxContact(ref best,t,Vector2.right);}
   if(delta.y>1e-8f){var t=(y-radius-a.y)/delta.y;var at=a.x+delta.x*t;if(at>=x&&at<=x+w)PickBoxContact(ref best,t,Vector2.down);}else if(delta.y<-1e-8f){var t=(y+h+radius-a.y)/delta.y;var at=a.x+delta.x*t;if(at>=x&&at<=x+w)PickBoxContact(ref best,t,Vector2.up);}
   PickCorner(ref best,a,b,new Vector2(x,y),radius);PickCorner(ref best,a,b,new Vector2(x+w,y),radius);PickCorner(ref best,a,b,new Vector2(x,y+h),radius);PickCorner(ref best,a,b,new Vector2(x+w,y+h),radius);return best;
  }
  public static Contact? Terrain(JObject room,Vector2 a,Vector2 b,float radius,bool separating=false){
   Contact? best=null;var grid=GetGrid(room);var width=grid.Width;var height=grid.Height;
   PickContact(ref best,Box(a,b,-100,-100,101,height+200,radius,separating));PickContact(ref best,Box(a,b,width-1,-100,101,height+200,radius,separating));PickContact(ref best,Box(a,b,-100,-100,width+200,101,radius,separating));PickContact(ref best,Box(a,b,-100,height-1,width+200,101,radius,separating));
   foreach(var rect in grid.Query(a,b,radius))PickContact(ref best,Box(a,b,rect.x,rect.y,rect.z,rect.w,radius,separating));return best;
  }
  // Conservative advancement against a rotating radial weapon and a linearly moving target.
  // The derivative bound includes both translations and the full angular tip speed.
  public static float? Sweep(Vector2 oldCenter,Vector2 center,Vector2 direction,float from,float to,float range,Vector2 a,Vector2 b,float radius){
   var heading=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;var speed=(b-a-(center-oldCenter)).magnitude+Math.Abs(to-from)*Mathf.Deg2Rad*range;
   float t=0;
   for(var i=0;i<512&&t<=1;i++){
    var c=Vector2.Lerp(oldCenter,center,t);var angle=(heading+Mathf.Lerp(from,to,t))*Mathf.Deg2Rad;var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var point=Vector2.Lerp(a,b,t);var relative=point-c;
    var distance=(relative-axis*Mathf.Clamp(Vector2.Dot(relative,axis),0,range)).magnitude-radius;
    if(distance<=0.00002f)return t;
    if(speed<1e-7f)return null;var step=distance/speed*0.9f;if(t+step>1)return null;t+=Math.Max(0.000001f,step);
   }
   return null;
  }
 }
 public sealed partial class DcP1World {
  private readonly Dictionary<int,Dictionary<Vector2Int,List<Cover>>> v6CoverGrid=new Dictionary<int,Dictionary<Vector2Int,List<Cover>>>();
  private readonly Dictionary<string,Cover> v6CoverIDs=new Dictionary<string,Cover>();
  private Cover V6Cover(string id){if(v6CoverIDs.Count==0)foreach(var item in covers)v6CoverIDs[item.ID]=item;return v6CoverIDs[id];}
  private IEnumerable<Cover> V6Covers(int room,Vector2 a,Vector2 b,float radius){
   if(!v6CoverGrid.TryGetValue(room,out var grid)){grid=new Dictionary<Vector2Int,List<Cover>>();foreach(var cover in covers.Where(c=>c.Room==room)){var cell=Vector2Int.FloorToInt(cover.Position);if(!grid.TryGetValue(cell,out var list))grid[cell]=list=new List<Cover>();list.Add(cover);}v6CoverGrid[room]=grid;}
   radius+=0.45f;for(var x=Mathf.FloorToInt(Math.Min(a.x,b.x)-radius);x<=Mathf.FloorToInt(Math.Max(a.x,b.x)+radius);x++)for(var y=Mathf.FloorToInt(Math.Min(a.y,b.y)-radius);y<=Mathf.FloorToInt(Math.Max(a.y,b.y)+radius);y++)if(grid.TryGetValue(new Vector2Int(x,y),out var items))foreach(var c in items)yield return c;
  }
  private long v6TickEpoch;
  public long NextV6GameTick=>v6TickEpoch+(WorldTick+1)*1000/30;
  private float PlayerRadius=>V6?Distance(parameters["player"],"collision_radius_milli_tiles"):0.3f;
  private float PlayerHurtRadius=>V6?Distance(parameters["player"],"hurt_radius_milli_tiles"):0.3f;
  private void InitializeV6(){
   if(!V6)return;
   foreach(var trap in traps){} // Trap records are created after geometry initialization below.
  }
  private DcV6Geometry.Contact? V6Terrain(int room,Vector2 a,Vector2 b,float radius,bool separating=false){
   if(V7)return V7Terrain(room,a,b,radius,separating);
   var best=DcV6Geometry.Terrain(Layout(room),a,b,radius,separating);
   foreach(var cover in V6Covers(room,a,b,radius)){if(!CoverSolid(cover))continue;var hit=DcV6Geometry.Circle(a,b,cover.Position,0.45f+radius,separating);if(!hit.HasValue)continue;
    if(!best.HasValue||DcV6Geometry.Quantize(hit.Value)<DcV6Geometry.Quantize(best.Value.Time)){var point=Vector2.Lerp(a,b,hit.Value);best=new DcV6Geometry.Contact{Time=hit.Value,Normal=(point-cover.Position).normalized,Kind="cover",ID=cover.ID};}
   }return best;
  }
  private void EndV6Dash(Player p,double at=-1){var cutoff=at<0?now:(long)Math.Ceiling(at);if(p.DashSource=="space")p.SpaceUntil=Math.Min(p.SpaceUntil,cutoff);else if(p.DashSource=="gust")p.GustUntil=Math.Min(p.GustUntil,cutoff);p.DodgeUntil=now;p.DashSource=null;p.InvulnerableUntil=Math.Max(p.SpaceUntil,p.GustUntil);}
  private void MoveV6Player(Player p){
   var dt=1f/30;var dodging=now<p.DodgeUntil;
   if(!dodging){p.Position=Move(Layout(p.Room),p.Position,p.Input.Move*Distance(parameters["player"],"move_milli_tiles_per_second")*V5Movement(p)*dt,PlayerRadius,point=>enemies.Any(e=>e.Health>0&&e.Room==p.Room&&(e.Kind=="elite"||e.Kind=="boss")&&Vector2.Distance(point,e.Position)<Radius(e)+PlayerRadius&&Vector2.Distance(point,e.Position)<Vector2.Distance(p.Position,e.Position)));return;}
   var end=p.Position+p.DodgeDirection*p.DodgeSpeed*dt;var hit=V6Terrain(p.Room,p.Position,end,PlayerRadius,true);
   foreach(var enemy in enemies){if(enemy.Health<=0||enemy.Room!=p.Room||enemy.Kind!="elite"&&enemy.Kind!="boss")continue;var t=DcV6Geometry.Circle(p.Position,end,enemy.Position,Radius(enemy)+PlayerRadius,true);if(t.HasValue&&(!hit.HasValue||t<hit.Value.Time))hit=new DcV6Geometry.Contact{Time=t.Value,Normal=(p.Position-enemy.Position).normalized,Kind="actor"};}
   var door=V6DoorCrossing(p,p.Position,end);if(door.HasValue&&(!hit.HasValue||door.Value.time<=hit.Value.Time)){p.Position=Vector2.Lerp(p.Position,end,door.Value.time);EndV6Dash(p);TransferV6(p,door.Value.destination,door.Value.side);return;}
   if(!hit.HasValue){p.Position=end;return;}
   p.Position=Vector2.Lerp(p.Position,end,Math.Max(0,hit.Value.Time-0.0001f));EndV6Dash(p,now+hit.Value.Time*1000/30);
   var remaining=dt*(1-hit.Value.Time);p.Position=Move(Layout(p.Room),p.Position,p.Input.Move*Distance(parameters["player"],"move_milli_tiles_per_second")*V5Movement(p)*remaining,PlayerRadius,point=>enemies.Any(e=>e.Health>0&&e.Room==p.Room&&(e.Kind=="elite"||e.Kind=="boss")&&Vector2.Distance(point,e.Position)<Radius(e)+PlayerRadius));
  }
  private (float time,int destination,string side)? V6DoorCrossing(Player p,Vector2 a,Vector2 b){
   if(V7)return V7DoorCrossing(p,a,b);
   if(Practice||now<p.DoorAt||!p.Connected||now<p.ProtectedUntil)return null;
   foreach(var door in (JArray)extension["doors"]){var f=(int)door["from"]==p.Room;if(!f&&(int)door["to"]!=p.Room)continue;var side=f?(string)door["side"]:RoomGeometry.Opposite((string)door["side"]);var target=f?(int)door["to"]:(int)door["from"];if(!CanTraverse(p.Room,target))continue;
    var direction=side=="east"?Vector2.right:side=="west"?Vector2.left:side=="north"?Vector2.up:Vector2.down;var point=RoomGeometry.DoorPoint(Layout(p.Room),side);var denominator=Vector2.Dot(b-a,direction);var beyond=Vector2.Dot(a-point,direction);var intent=now<p.DodgeUntil?p.DodgeDirection:p.Input.Move;float time;if(beyond>=-0.0001f&&beyond<=0.5f&&Vector2.Dot(intent,direction)>0.4f)time=0;else{if(denominator<=0)continue;time=Vector2.Dot(point-a,direction)/denominator;if(time<0||time>1)continue;}var at=Vector2.Lerp(a,b,time);var tangent=new Vector2(-direction.y,direction.x);if(Math.Abs(Vector2.Dot(at-point,tangent))+(V7?Math.Max(Math.Abs(Vector2.Dot(Body(p).Min,tangent)),Math.Abs(Vector2.Dot(Body(p).Max,tangent))):PlayerRadius)<=1)return(time,target,side);
   }return null;
  }
  private void TryV6Door(Player p){
   if(p.Room<0)return;var crossing=V6DoorCrossing(p,p.TickStart,p.Position);
   if(crossing.HasValue){EndV6Dash(p);TransferV6(p,crossing.Value.destination,crossing.Value.side);}
  }
  private void TransferV6(Player p,int destination,string side){
   var direction=side=="east"?Vector2.right:side=="west"?Vector2.left:side=="north"?Vector2.up:Vector2.down;var point=V7?V7EntryPoint(p,destination,side):RoomGeometry.DoorPoint(Layout(destination),RoomGeometry.Opposite(side))+direction*0.7f;
   if(V7?V7Blocked(destination,Body(p),point):Blocked(Layout(destination),point,PlayerRadius))return;
   if(V8)V8StopSpin(p,"room_change");fields.RemoveAll(f=>f.Owner==p.ID);p.Room=destination;p.Explored.Add(destination);p.Position=point;p.TickStart=point;p.DoorAt=now+500;p.InteractionTarget=null;CancelBasic(p);p.Input.Attack=false;p.Input.Melee=false;p.Input.AttackPressed=false;p.ReleaseRequired=true;EndV6Dash(p);Effect("door",p.ID,destination,point);
  }
  private float? V6SweepContact(Player p,MeleeV5 attack,Vector2 a,Vector2 b,float radius)=>DcV6Geometry.Sweep(p.TickStart,p.Position,attack.Direction,attack.SweepStart,attack.SweepEnd,attack.Range,a,b,radius);
  private Vector2? V6SpawnPoint(int room,string decision){
   if(V7)return V7SpawnPoint(room,decision,rooms[room].Pending.Count>0?rooms[room].Pending.Peek():"guard");
   var candidates=Layout(room)["spawn_candidates"] as JArray;if(candidates==null)return null;var seed=Convert.ToUInt32(Rank(decision,"spawn_order").Substring(0,8),16);
   var legal=candidates.Select(c=>c is JArray?RoomGeometry.Point(c):new Vector2((float)c["x_milli"]/1000,(float)c["y_milli"]/1000)).Where(c=>SpawnLegal(room,c)).OrderByDescending(c=>SharedVisibleEdge(room,c)).ThenByDescending(c=>SharedVisible(room,c)).ThenBy(c=>SpawnRank(seed,Mathf.RoundToInt(c.x*1000),Mathf.RoundToInt(c.y*1000))).ToArray();return legal.Length>0?legal[0]:(Vector2?)null;
  }
  private void TickV6Traps(){
   foreach(var trap in traps){
    if(trap.Epoch<0){if(!players.Values.Any(p=>!p.Left&&p.Room==trap.Room&&p.Connected))continue;trap.Epoch=combatNow;trap.ActivationGeneration=1;var items=(JArray)Layout(trap.Room)["traps"];var local=traps.Where(t=>t.Room==trap.Room).ToList().IndexOf(trap);trap.Group=(string)items[local]["group"];}
    var offset=trap.Group=="B"?1000:trap.Group=="C"?2000:0;var elapsed=combatNow-trap.Epoch-offset;
    if(elapsed<0){trap.Phase="idle";trap.Until=trap.Epoch+offset;continue;}
    var cycle=elapsed/3000;var phase=elapsed%3000;if(trap.Cycle!=cycle){trap.Cycle=cycle;trap.Hit.Clear();}
    var previous=trap.Phase;trap.Phase=phase<600?"warning":phase<800?"active":"cooldown";trap.Until=combatNow+(phase<600?600-phase:phase<800?800-phase:3000-phase);trap.Trigger=trap.Epoch+offset+cycle*3000;
    if(trap.Phase=="warning"&&previous!="warning")Effect("trap_warning",null,trap.Room,trap.Position);
    if(technicalWaiting||trap.Phase!="active")continue;
    foreach(var p in players.Values.Where(p=>!p.Left&&p.Health>0&&p.Room==trap.Room)){var key=p.ID+":"+p.LifeGeneration;if((V7?DcV7Geometry.Overlap(Body(p).Vertices,p.Position,DcV7Geometry.Rect(-.75f,-.5f,1.5f,1),trap.Position):Vector2.Distance(p.Position,trap.Position)<=0.7f+PlayerHurtRadius)&&trap.Hit.Add(key))Hurt(p,12,trap.ID+":"+trap.ActivationGeneration+":"+cycle);}
    foreach(var e in enemies.Where(e=>e.Health>0&&e.Room==trap.Room&&e.Kind!="elite"&&e.Kind!="boss").ToArray())if((V7?DcV7Geometry.Overlap(Body(e).Vertices,e.Position,DcV7Geometry.Rect(-.75f,-.5f,1.5f,1),trap.Position):Vector2.Distance(e.Position,trap.Position)<=0.7f+Radius(e))&&trap.Hit.Add(e.ID))EnvironmentHit(e,25);
   }
  }
  private void ResetV6Traps(long at){foreach(var trap in traps.Where(t=>t.Epoch>=0)){trap.Epoch=at;trap.ActivationGeneration++;trap.Cycle=-1;trap.Hit.Clear();trap.Phase="idle";}}
  private float V6DetourLength(Enemy enemy,Vector2 goal){
   var layout=Layout(enemy.Room);var origin=Vector2Int.FloorToInt(enemy.Position);var end=Vector2Int.FloorToInt(goal);var queue=new Queue<(Vector2Int cell,int steps)>();var seen=new HashSet<Vector2Int>{origin};queue.Enqueue((origin,0));var dirs=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
   var goalBlocked=Blocked(layout,(Vector2)end+Vector2.one*0.5f,Radius(enemy));
   while(queue.Count>0&&seen.Count<=1024){var node=queue.Dequeue();if(node.cell==end||goalBlocked&&Vector2.Distance((Vector2)node.cell+Vector2.one*0.5f,goal)<=1.5f)return node.steps;foreach(var dir in dirs){var next=node.cell+dir;if(seen.Contains(next)||Blocked(layout,(Vector2)next+Vector2.one*0.5f,Radius(enemy)))continue;seen.Add(next);queue.Enqueue((next,node.steps+1));}}
   return 100000;
  }
  private static readonly float[] v6CoverApproachAngles={0,45,-45,90,-90,135,-135,180};
  private Vector2? V6CoverStandPoint(Enemy enemy,Cover cover){var away=(enemy.Position-cover.Position).normalized;if(away.sqrMagnitude<0.01f)away=Vector2.right;var distance=Radius(enemy)+0.55f;foreach(var angle in v6CoverApproachAngles){var point=cover.Position+Rotate(away,angle)*distance;if(!Blocked(Layout(enemy.Room),point,Radius(enemy))&&ClearV5CoverLine(enemy.Room,point,cover))return point;}return null;}
  private bool TickV6CoverAI(Enemy enemy){
   if(V7)return TickV7CoverAI(enemy);
   if(enemy.Kind=="boss"||technicalWaiting||enemy.ReadyAt==long.MaxValue||enemy.Health<=0)return false;
   if(enemy.SuppressedUntil>now){enemy.CoverTarget=null;return false;}
   var target=P21EnemyTarget(enemy);if(target==null)return false;
   var selected=covers.FirstOrDefault(c=>c.ID==enemy.CoverTarget&&c.Health>0&&c.Room==enemy.Room);
   if(combatNow>=enemy.CoverProbeAt){
    var blocked=V6Terrain(enemy.Room,enemy.Position,target.Position,Radius(enemy),true).HasValue;
    var slow=Vector2.Distance(enemy.Position,enemy.CoverProbePosition)<0.1f;
    enemy.CoverProbePosition=enemy.Position;enemy.CoverProbeAt=combatNow+200;
    var detour=blocked?V6DetourLength(enemy,target.Position):Vector2.Distance(enemy.Position,target.Position);
    enemy.CoverNoDetour=detour-Vector2.Distance(enemy.Position,target.Position)>6;
    if(!blocked&&!slow||!enemy.CoverNoDetour)enemy.CoverBlockedAt=0;else if(enemy.CoverBlockedAt==0)enemy.CoverBlockedAt=combatNow;
   }
   if(selected==null&&enemy.CoverBlockedAt>0&&combatNow-enemy.CoverBlockedAt>=1000&&enemy.CoverNoDetour&&enemy.WarningUntil==0){
    selected=covers.Where(c=>c.Room==enemy.Room&&c.Health>0&&Vector2.Distance(c.Position,enemy.Position)<6&&ClearV5CoverLine(enemy.Room,enemy.Position,c)).OrderBy(c=>Vector2.Distance(c.Position,enemy.Position)+Vector2.Distance(c.Position,target.Position)).ThenBy(c=>c.ID,StringComparer.Ordinal).FirstOrDefault();
    if(selected!=null){enemy.CoverTarget=selected.ID;enemy.CoverTargetUntil=combatNow+500;}
   }
   if(selected==null){enemy.CoverTarget=null;return false;}
   if(enemy.Kind=="charger"&&enemy.ChargeUntil>combatNow){var dashDefinition=enemyDefinitions[enemy.Kind];var end=enemy.Position+enemy.Aim*Distance(dashDefinition,"charge_speed_milli_tiles_per_second",6000)/30;var hit=V6Terrain(enemy.Room,enemy.Position,end,Radius(enemy),true);if(hit.HasValue){enemy.Position=Vector2.Lerp(enemy.Position,end,Math.Max(0,hit.Value.Time-0.001f));if(hit.Value.Kind=="cover")DamageCover(covers.First(c=>c.ID==hit.Value.ID),Number(dashDefinition,"damage"));enemy.ChargeUntil=0;enemy.AttackAt=combatNow+Number(dashDefinition,"recovery_ms",500);}else enemy.Position=end;enemy.StuckAt=combatNow;return true;}

   if(combatNow>=enemy.CoverTargetUntil&&ClearLine(Layout(enemy.Room),enemy.Position,target.Position)){enemy.CoverTarget=null;return false;}
   var definition=enemyDefinitions[enemy.Kind];var range=enemy.Kind=="archer"?8:Distance(definition,"attack_range_milli_tiles",1200);
   if(enemy.WarningUntil>0){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;if(combatNow<enemy.WarningUntil)return true;enemy.PresentationAttackAt=combatNow;enemy.StuckAt=combatNow;enemy.WarningUntil=0;enemy.Warning=null;enemy.AttackAt=Math.Max(enemy.AttackAt,combatNow+Number(definition,"recovery_ms",500));
    if(enemy.Kind=="charger"){enemy.ChargeUntil=combatNow+Number(definition,"charge_duration_ms",600);return true;}
    if(enemy.Kind=="archer"){for(var i=0;i<3;i++){enemy.ReservedShots=Math.Max(0,enemy.ReservedShots-1);EmitV5Enemy(enemy,enemy.AttackAction,enemy.Position,Rotate(enemy.Aim,(i-1)*12),Number(definition,"damage"),Distance(definition,"projectile_speed_milli_tiles_per_second"));}}
    else if(Vector2.Distance(enemy.Position,selected.Position)<=range+0.45f&&ClearV5CoverLine(enemy.Room,enemy.Position,selected))DamageCover(selected,Number(definition,"damage"));return true;}
   if(Vector2.Distance(enemy.Position,selected.Position)>range+0.35f||!ClearV5CoverLine(enemy.Room,enemy.Position,selected)){var stand=V6CoverStandPoint(enemy,selected);if(!stand.HasValue){enemy.CoverTarget=null;enemy.CoverBlockedAt=0;enemy.PathAt=0;return false;}var direction=EnemyPath(enemy,stand.Value);enemy.Position=Move(Layout(enemy.Room),enemy.Position,SeparateEnemy(enemy,direction)*Distance(definition,"move_milli_tiles_per_second")/30,Radius(enemy),p=>WeaponWindBlocks(enemy,p)||EnemyOverlap(enemy,p));if(Vector2.Distance(enemy.Position,enemy.StuckPosition)>0.3f){enemy.StuckPosition=enemy.Position;enemy.StuckAt=combatNow;}else if(combatNow-enemy.StuckAt>=5000){enemy.CoverTarget=null;enemy.CoverBlockedAt=0;enemy.CoverProbeAt=combatNow+500;enemy.PathAt=0;}return true;}
   enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;
   if(combatNow<enemy.AttackAt||combatNow<enemy.ReadyAt)return true;
   if(enemy.Kind=="archer"&&ProjectileLoad+3>V5ProjectileCapacity)return true;
   enemy.AttackAt=combatNow+Number(definition,"attack_interval_ms",1000);enemy.ReservedShots=enemy.Kind=="archer"?3:0;enemy.Warning="cover_attack";enemy.WarningUntil=combatNow+Number(definition,"telegraph_ms",600);enemy.AttackAction=NextID("cover_attack");enemy.Origin=enemy.Position;enemy.Aim=(selected.Position-enemy.Position).normalized;return true;
  }
 }
}
