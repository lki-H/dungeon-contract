using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public sealed partial class DcP1World {
  private sealed class LifeV6 {public Projectile Flight;}
  private readonly HashSet<string> v6LiveSet=new HashSet<string>();
  private readonly Dictionary<string,LifeV6> v6Flights=new Dictionary<string,LifeV6>();
  private readonly List<DcV6LifeRecord> v6Lifecycle=new List<DcV6LifeRecord>();
  private readonly System.IO.MemoryStream v6LifecycleBuffer=new System.IO.MemoryStream(2048);
  private System.IO.StreamWriter v6LifecycleText;
  private uint v6EventSequence;
  private bool v6ResolvingMelee;
  private bool v6ResolvingProjectiles;
  private double v6ContactTime=-1;
  public double V6LifecycleEncodingMS {get;private set;}
  public double V6ContactQueryMS {get;private set;}
  public int V6LifecyclePending=>v6Lifecycle.Count;
  private static void V6Write(Newtonsoft.Json.JsonWriter w,string key,string value){w.WritePropertyName(key);w.WriteValue(value);}
  private static void V6Write(Newtonsoft.Json.JsonWriter w,string key,float value){w.WritePropertyName(key);w.WriteValue(value);}
  private static void V6Write(Newtonsoft.Json.JsonWriter w,string key,long value){w.WritePropertyName(key);w.WriteValue(value);}
  private static void V6Write(Newtonsoft.Json.JsonWriter w,string key,bool value){w.WritePropertyName(key);w.WriteValue(value);}
  private static void V6WriteProjectile(Newtonsoft.Json.JsonWriter w,Projectile p){w.WriteStartObject();V6Write(w,"id",p.ID);V6Write(w,"room_id",p.Room);V6Write(w,"x",p.Position.x);V6Write(w,"y",p.Position.y);V6Write(w,"direction_x",p.Direction.x);V6Write(w,"direction_y",p.Direction.y);V6Write(w,"radius",p.CollisionRadius);V6Write(w,"speed",p.Speed);V6Write(w,"enemy",p.Enemy);V6Write(w,"rune_bolt",p.RuneBolt);V6Write(w,"wind_volley",p.Root?.StartsWith("volley_",StringComparison.Ordinal)==true);V6Write(w,"bounces",p.Bounces);V6Write(w,"penetrations",Math.Max(0,p.Penetration));V6Write(w,"root",p.Root??p.Action);V6Write(w,"owner",p.Owner);V6Write(w,"source_life",p.SourceLife);V6Write(w,"damage",p.Damage);V6Write(w,"frost",p.FrostEnabled);V6Write(w,"chain",p.ChainEnabled);V6Write(w,"trajectory_revision",p.TrajectoryRevision);V6Write(w,"network_group",p.NetworkGroup);V6Write(w,"unbounded",true);w.WritePropertyName("remaining_range");w.WriteNull();if(p.ProtocolVersion>=7){V6Write(w,"born_game_ms",p.BornGameMS);V6Write(w,"expires_game_ms",p.ExpiresGameMS);V6Write(w,"emission_index",(long)p.EmissionIndex);V6Write(w,"attack_episode_id",p.Episode);V6Write(w,"unlimited_bounces",p.RuneBolt);}w.WriteEndObject();}
  private void V6Life(string kind,Projectile p,string reason=null,float subtick=0){
   var clock=TickPhaseObserver==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
   if(v6Lifecycle.Count>=8192)throw new InvalidOperationException("V6 lifecycle world queue budget");
   if(v6LifecycleText==null)v6LifecycleText=new System.IO.StreamWriter(v6LifecycleBuffer,new System.Text.UTF8Encoding(false),1024,true);
   v6LifecycleBuffer.SetLength(0);v6LifecycleBuffer.Position=0;
   using(var writer=new Newtonsoft.Json.JsonTextWriter(v6LifecycleText){CloseOutput=false,Culture=System.Globalization.CultureInfo.InvariantCulture}){writer.WriteStartObject();V6Write(writer,"event_id",(string)manifest["run_id"]+":life:"+(++v6EventSequence));V6Write(writer,"kind",kind);V6Write(writer,"entity_id",p.ID);V6Write(writer,"room_id",p.Room);V6Write(writer,"frame",WorldTick);V6Write(writer,"game_ms",now);V6Write(writer,"subtick",DcV6Geometry.Quantize(subtick));V6Write(writer,"trajectory_revision",p.TrajectoryRevision);writer.WritePropertyName("state");V6WriteProjectile(writer,p);V6Write(writer,"reason",reason);writer.WriteEndObject();writer.Flush();}
   v6Lifecycle.Add(new DcV6LifeRecord(p.Room,p.ID,kind,v6LifecycleBuffer.ToArray()));if(TickPhaseObserver!=null)V6LifecycleEncodingMS+=(System.Diagnostics.Stopwatch.GetTimestamp()-clock)*1000d/System.Diagnostics.Stopwatch.Frequency;
  }
  private void RegisterV6Flight(Projectile p){if(!V6||v6Flights.ContainsKey(p.ID))return;V6Life("spawn",p);v6Flights[p.ID]=new LifeV6{Flight=p};}
  private void BeginV6LifecycleTick(){V6LifecycleEncodingMS=0;V6ContactQueryMS=0;foreach(var p in projectiles)RegisterV6Flight(p);}
  private void EndV6LifecycleTick(){
   foreach(var p in projectiles){RegisterV6Flight(p);v6Flights[p.ID].Flight=p;}
   v6LiveSet.Clear();foreach(var p in projectiles)v6LiveSet.Add(p.ID);foreach(var id in v6Flights.Keys.Where(id=>!v6LiveSet.Contains(id)).ToArray()){
    var p=v6Flights[id].Flight;var reason=Terminal?"terminal":players.TryGetValue(p.Owner,out var owner)&&owner.Left?"owner_permanent_exit":technicalWaiting?"technical_wait":"room_clear";V6Life("terminate",p,reason);v6Flights.Remove(id);
   }
  }

  public DcV6LifeRecord[] DrainV6LifecycleRecords(){if(!V6)return Array.Empty<DcV6LifeRecord>();EndV6LifecycleTick();var result=v6Lifecycle.ToArray();v6Lifecycle.Clear();return result;}
  public JObject[] DrainV6Lifecycle()=>DrainV6LifecycleRecords().Select(e=>e.Object()).ToArray();
  private void EndV6Flight(Projectile p,string reason,float at){if(p.TerminationReason!=null)return;RegisterV6Flight(p);V6Life("terminate",p,reason,at);v6Flights.Remove(p.ID);p.TerminationReason=reason;if(!v6ResolvingProjectiles)projectiles.Remove(p);}
  private sealed class ContactV6 {public DcV6Geometry.Contact? Terrain;public float Body,Clear;public Enemy Victim;public Player Player,Protector;public string Cast;public bool Melee;public float Time=>Math.Min(Terrain?.Time??2,Math.Min(Body,Clear));}
  private ContactV6 FindV6Contact(Projectile p,Vector2 start,Vector2 end,float consumed){
    if(V7)return V7FindContact(p,start,end,consumed);
    var clock=TickPhaseObserver==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
    var terrain=V6Terrain(p.Room,start,end,p.CollisionRadius,true);var tTerrain=terrain?.Time??2;
    var body=2f;Enemy victim=null;Player player=null;var clear=2f;Player protector=null;string cast=null;bool melee=false;
    foreach(var target in players.Values){if(!p.Enemy||target.Left||target.Health<=0||target.Room!=p.Room)continue;
     var old=Vector2.Lerp(target.TickStart,target.Position,consumed);var contact=DcV6Geometry.Circle(start-old,end-target.Position,Vector2.zero,PlayerHurtRadius+p.CollisionRadius);
     if(contact.HasValue&&(DcV6Geometry.Quantize(contact.Value)<DcV6Geometry.Quantize(body)||Math.Abs(contact.Value-body)<1e-6f&&(player==null||target.Slot<player.Slot))){body=contact.Value;player=target;}
     var a=target.MeleeAttack;if(a==null||a.Phase!="active")continue;
     var parry=a.Heavy?DcV6Geometry.Circle(start,end,a.Impact,0.5f+p.CollisionRadius):DcV6Geometry.Sweep(old,target.Position,a.Direction,Mathf.Lerp(a.SweepStart,a.SweepEnd,consumed),a.SweepEnd,a.Range,start,end,p.CollisionRadius);
     if(parry.HasValue&&parry.Value<clear&&ClearLine(Layout(target.Room),target.Position,Vector2.Lerp(start,end,parry.Value))){clear=parry.Value;protector=target;cast=a.Root;melee=true;}
    }
    if(p.Enemy)foreach(var field in fields.Where(f=>f.Kind=="wind_bind"&&f.Room==p.Room)){
     var owner=players[field.Owner];var old=Vector2.Lerp(owner.TickStart,owner.Position,consumed);var contact=DcV6Geometry.Circle(start-old,end-owner.Position,Vector2.zero,Distance(Kit(owner.Loadout)["secondary"],"radius_milli_tiles")+p.CollisionRadius);
     if(contact.HasValue&&contact.Value<clear){clear=contact.Value;protector=owner;cast=field.ID;melee=false;}
    }
    if(!p.Enemy)foreach(var enemy in enemies){if(enemy.Health<=0||enemy.Room!=p.Room||p.Hit.Contains(enemy.ID))continue;var contact=DcV6Geometry.Circle(start-Vector2.Lerp(enemy.TickStart,enemy.Position,consumed),end-enemy.Position,Vector2.zero,Radius(enemy)+p.CollisionRadius);
     if(contact.HasValue&&(contact.Value<body||Math.Abs(contact.Value-body)<1e-6f&&(victim==null||string.CompareOrdinal(enemy.ID,victim.ID)<0))){body=contact.Value;victim=enemy;}}
   if(TickPhaseObserver!=null)V6ContactQueryMS+=(System.Diagnostics.Stopwatch.GetTimestamp()-clock)*1000d/System.Diagnostics.Stopwatch.Frequency;return new ContactV6{Terrain=terrain,Body=body,Clear=clear,Victim=victim,Player=player,Protector=protector,Cast=cast,Melee=melee};
  }
  private void TickV6Projectiles(){v6ResolvingProjectiles=true;try{TickV6ProjectilesCore();}finally{v6ResolvingProjectiles=false;projectiles.RemoveAll(p=>p.TerminationReason!=null);}}
  private void TickV6ProjectilesCore(){
   foreach(var p in projectiles.Where(p=>!p.Enemy).ToArray()){RunV6Flight(p);if(Terminal||V7&&technicalWaiting)return;}
   var pending=projectiles.Where(p=>p.Enemy).Select(p=>(flight:p,time:FindV6Contact(p,p.Position,p.Position+p.Direction*p.Speed/30,0).Time)).ToList();
   Comparison<(Projectile flight,float time)> order=(a,b)=>{var t=DcV6Geometry.Quantize(a.time).CompareTo(DcV6Geometry.Quantize(b.time));return t!=0?t:string.CompareOrdinal(a.flight.ID,b.flight.ID);};pending.Sort(order);
   while(pending.Count>0){if(Terminal||V7&&technicalWaiting)return;var entry=pending[0];pending.RemoveAt(0);var fresh=FindV6Contact(entry.flight,entry.flight.Position,entry.flight.Position+entry.flight.Direction*entry.flight.Speed/30,0).Time;
    if(pending.Count>0&&order((entry.flight,fresh),pending[0])>0){pending.Add((entry.flight,fresh));pending.Sort(order);continue;}RunV6Flight(entry.flight);
   }
  }
  private void RunV6Flight(Projectile p){
   if(V7){RunV7Flight(p);return;}
   if(!Finite(p.Position.x)||!Finite(p.Position.y)||!Finite(p.Direction.x)||!Finite(p.Direction.y)||!Finite(p.Speed)||p.Speed<=0||Math.Abs(p.Direction.sqrMagnitude-1)>0.01f){AbortTechnical();return;}
   RegisterV6Flight(p);if(p.MuzzlePending){p.MuzzlePending=false;if(StepV6Flight(p,p.Position+p.Direction*0.9f,0))return;}StepV6Flight(p,p.Position+p.Direction*p.Speed/30f,1);
  }

  // One continuous segment; every bounce or pierce consumes the remaining segment distance.
  private bool StepV6Flight(Projectile p,Vector2 end,float frameFraction){
   var start=p.Position;float consumed=0,remainingFraction=frameFraction;
   for(var iteration=0;iteration<8;iteration++){
    var contact=FindV6Contact(p,start,end,consumed);var terrain=contact.Terrain;var tTerrain=terrain?.Time??2;var body=contact.Body;var clear=contact.Clear;var victim=contact.Victim;var player=contact.Player;var protector=contact.Protector;var cast=contact.Cast;var melee=contact.Melee;
    var earliest=Math.Min(tTerrain,Math.Min(body,clear));if(earliest>1){p.Position=end;return false;}
    p.Position=Vector2.Lerp(start,end,earliest);var at=consumed+remainingFraction*earliest;var distance=(end-start).magnitude*(1-earliest);
    if(DcV6Geometry.Quantize(tTerrain)<=Math.Min(DcV6Geometry.Quantize(body),DcV6Geometry.Quantize(clear))){
     if(terrain.Value.Kind=="cover")DamageCover(V6Cover(terrain.Value.ID),p.Damage);
     if(!p.RuneBolt||p.Bounces<=0){EndV6Flight(p,terrain.Value.Kind,at);return true;}
     var normal=terrain.Value.Normal;if(normal.sqrMagnitude<0.01f||Vector2.Dot(p.Direction,normal)>=0){EndV6Flight(p,terrain.Value.Kind,at);return true;}
     p.Direction=Vector2.Reflect(p.Direction,normal).normalized;p.Bounces--;p.TrajectoryRevision++;V6Life("bounce",p,null,at);Effect("ricochet",p.Owner,p.Room,p.Position);
     p.Position+=normal*0.001f;
     if(RoomGeometry.Blocked(Layout(p.Room),p.Position,p.CollisionRadius)||covers.Any(c=>c.Room==p.Room&&CoverSolid(c)&&Vector2.Distance(c.Position,p.Position)<p.CollisionRadius+0.45f-0.0001f)){EndV6Flight(p,terrain.Value.Kind,at);return true;}
    }else if(DcV6Geometry.Quantize(clear)<=DcV6Geometry.Quantize(body)){
     if(P3)P3ClearProjectile(protector,p,cast);Effect(melee?(protector.Loadout=="ranger"?"blade_block":"hammer_block"):"wind_clear",protector.ID,p.Room,p.Position);EndV6Flight(p,melee?"melee_block":"wind_clear",at);return true;
    }else if(p.Enemy){v6ContactTime=now+at*1000/30;Hurt(player,p.Damage,p.Action);v6ContactTime=-1;EndV6Flight(p,"target_hit",at);return true;}
    else{
     p.Hit.Add(victim.ID);Hit(victim,p.Owner,p.Action,p.Damage,p.Direction,true,p.SourceLife,p.EmissionFrame,p.Root,p.FrostEnabled);
     if(p.RuneBolt&&p.ChainEnabled&&!p.ChainTriggered&&v5ChainRoots.Add(p.Root??p.Action)){
      p.ChainTriggered=true;var next=enemies.Where(e=>e.Health>0&&e.ID!=victim.ID&&e.Room==p.Room&&Vector2.Distance(e.Position,victim.Position)<=3&&ClearLine(Layout(e.Room),victim.Position,e.Position)).OrderBy(e=>Vector2.Distance(e.Position,victim.Position)).ThenBy(e=>e.ID,StringComparer.Ordinal).FirstOrDefault();if(next!=null)Hit(next,p.Owner,p.Action+"_chain",p.Damage*700/1000,null,true,p.SourceLife,p.EmissionFrame,p.Root,p.FrostEnabled);
     }
     if(p.RuneBolt||p.Penetration--<=0){EndV6Flight(p,"target_hit",at);return true;}
     p.Position+=p.Direction*0.001f;
    }
    consumed=at;remainingFraction*=1-earliest;start=p.Position;end=start+p.Direction*distance;if(distance<=0.00001f)return false;
   }
   AbortTechnical();return true;
  }
  private void ResolveV6Melee(){v6ResolvingMelee=true;foreach(var p in players.Values.Where(p=>p.Health>0&&!p.Left))TickV5Melee(p);v6ResolvingMelee=false;}
  private void EnvironmentHit(Enemy enemy,int damage){var actual=Math.Min(enemy.Health,damage);enemy.Health-=actual;Effect("environment_hit",null,enemy.Room,enemy.Position,actual,enemy.ID);if(enemy.Health<=0)enemy.ReservedShots=0;}
 }
}
