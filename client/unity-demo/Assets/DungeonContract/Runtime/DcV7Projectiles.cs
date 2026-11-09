using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonContract {
 public sealed partial class DcP1World {
  public int V7ContactBudgetParks {get;private set;}
  private long v7PhysicsWaitUntil;
  private void V7UpdateInside(Projectile p,float fraction){
   foreach(var id in p.Inside.ToArray()){
    var target=enemies.FirstOrDefault(e=>e.ID==id&&e.Health>0&&e.Room==p.Room);
    if(target==null||DcV7Geometry.Distance(p.Position-Vector2.Lerp(target.TickStart,target.Position,fraction),Body(target).Vertices)>p.CollisionRadius+.001f)p.Inside.Remove(id);
   }
  }
  private float? V7Parry(Player owner,MeleeV5 a,Vector2 from,Vector2 to,float radius,float consumed,float through){
   if(a.Heavy)return V8?DcV7Geometry.Circle(from,to,V8Head(a),radius):DcV6Geometry.Circle(from,to,a.Impact,.625f+radius);
   var old=Vector2.Lerp(owner.TickStartCenter,Center(owner),consumed);var center=Vector2.Lerp(owner.TickStartCenter,Center(owner),through);var inner=owner.Loadout=="ranger"?.4f+(a.Range-.4f)*.35f:Math.Max(.4f,a.Range-.6f);
   return DcV7Geometry.VisibleWeaponCircle(old,center,Mathf.Lerp(a.V7AngleStart,a.V7AngleEnd,consumed),Mathf.Lerp(a.V7AngleStart,a.V7AngleEnd,through),inner,a.Range,owner.Loadout=="ranger"?.08f:.3f,from,to,radius);
  }
  private ContactV6 V7FindContact(Projectile p,Vector2 start,Vector2 end,float consumed,float through=1){
   var clock=TickPhaseObserver==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
   var terrain=V7Terrain(p.Room,start,end,p.CollisionRadius,true);float body=2,clear=2;Enemy victim=null;Player player=null,protector=null;string cast=null;var melee=false;
   if(p.Enemy){
    foreach(var target in players.Values){
     if(target.Left||target.Health<=0||target.Room!=p.Room)continue;var old=Vector2.Lerp(target.TickStart,target.Position,consumed);
     var hit=DcV7Geometry.CircleBody(start-old,end-Vector2.Lerp(target.TickStart,target.Position,through),Body(target),p.CollisionRadius);
     if(hit.HasValue&&(hit.Value<body||Math.Abs(hit.Value-body)<1e-6f&&(player==null||target.Slot<player.Slot))){body=hit.Value;player=target;}
     var attack=target.MeleeAttack;if(attack==null||attack.Phase!="active")continue;var parry=V7Parry(target,attack,start,end,p.CollisionRadius,consumed,through);
     if(parry.HasValue&&parry.Value<clear&&ClearLine(Layout(target.Room),Vector2.Lerp(target.TickStartCenter,Center(target),Mathf.Lerp(consumed,through,parry.Value)),Vector2.Lerp(start,end,parry.Value))){clear=parry.Value;protector=target;cast=attack.Root;melee=true;}
    }
    if(V8)foreach(var owner in players.Values.Where(o=>o.Room==p.Room&&V8Spinning(o))){
     var old=Vector2.Lerp(owner.TickStartCenter,Center(owner),consumed);var finish=Vector2.Lerp(owner.TickStartCenter,Center(owner),through);var hit=DcV6Geometry.Circle(start-old,end-finish,Vector2.zero,Distance(Kit(owner.Loadout)["secondary"],"radius_milli_tiles")+p.CollisionRadius);
     if(hit.HasValue&&hit.Value<clear&&ClearLine(Layout(p.Room),Vector2.Lerp(old,finish,hit.Value),Vector2.Lerp(start,end,hit.Value))){clear=hit.Value;protector=owner;cast=owner.SpinRoot;melee=false;}
    }
    foreach(var field in fields.Where(f=>f.Kind=="wind_bind"&&f.Room==p.Room)){
     var owner=players[field.Owner];var old=Vector2.Lerp(owner.TickStartCenter,Center(owner),consumed);var hit=DcV6Geometry.Circle(start-old,end-Vector2.Lerp(owner.TickStartCenter,Center(owner),through),Vector2.zero,Distance(Kit(owner.Loadout)["secondary"],"radius_milli_tiles")+p.CollisionRadius);
     if(hit.HasValue&&hit.Value<clear){clear=hit.Value;protector=owner;cast=field.ID;melee=false;}
    }
   }else foreach(var target in enemies){
    if(target.Health<=0||target.Room!=p.Room||(p.RuneBolt?p.Inside.Contains(target.ID):p.Hit.Contains(target.ID)))continue;
    var hit=DcV7Geometry.CircleBody(start-Vector2.Lerp(target.TickStart,target.Position,consumed),end-Vector2.Lerp(target.TickStart,target.Position,through),Body(target),p.CollisionRadius);
    if(hit.HasValue&&(hit.Value<body||Math.Abs(hit.Value-body)<1e-6f&&(victim==null||string.CompareOrdinal(target.ID,victim.ID)<0))){body=hit.Value;victim=target;}
   }
   if(TickPhaseObserver!=null)V6ContactQueryMS+=(System.Diagnostics.Stopwatch.GetTimestamp()-clock)*1000d/System.Diagnostics.Stopwatch.Frequency;return new ContactV6{Terrain=terrain,Body=body,Clear=clear,Victim=victim,Player=player,Protector=protector,Cast=cast,Melee=melee};
  }
  private void RunV7Flight(Projectile p){
   if(!Finite(p.Position.x)||!Finite(p.Position.y)||!Finite(p.Direction.x)||!Finite(p.Direction.y)||!Finite(p.Speed)||p.Speed<=0||Math.Abs(p.Direction.sqrMagnitude-1)>.01f){AbortTechnical();return;}
   if(p.ExpiresGameMS>0&&now>=p.ExpiresGameMS){EndV6Flight(p,"expired",0);return;}
   RegisterV6Flight(p);V7UpdateInside(p,0);var contactBudget=32;
   if(p.MuzzlePending){p.MuzzlePending=false;if(StepV7Flight(p,p.Position+p.Direction*.9f,0,ref contactBudget)||contactBudget==0)return;}
   var duration=Math.Min(1000d/30,p.ExpiresGameMS>0?p.ExpiresGameMS-now:1000d/30);
   if(StepV7Flight(p,p.Position+p.Direction*p.Speed*(float)(duration/1000),(float)(duration/(1000d/30)),ref contactBudget))return;
   if(p.ExpiresGameMS>0&&now+duration>=p.ExpiresGameMS)EndV6Flight(p,"expired",(float)(duration/(1000d/30)));
  }
  private bool StepV7Flight(Projectile p,Vector2 end,float frameFraction,ref int contactBudget){
   var start=p.Position;float consumed=0,remainingFraction=frameFraction;
   while(contactBudget>0){contactBudget--;
    // Zero denotes the existing muzzle-offset query, which retains full-frame CCD.
    // Timed flight segments instead share their lifetime-clipped interval.
    var poseStart=consumed;var poseEnd=frameFraction==0?1:frameFraction;
    var contact=V7FindContact(p,start,end,poseStart,poseEnd);var terrain=contact.Terrain;var tTerrain=terrain?.Time??2;var earliest=Math.Min(tTerrain,Math.Min(contact.Body,contact.Clear));
    if(earliest>1){p.Position=end;V7UpdateInside(p,poseEnd);p.ContactStalls=0;return false;}
    p.Position=Vector2.Lerp(start,end,earliest);var at=consumed+remainingFraction*earliest;var distance=(end-start).magnitude*(1-earliest);V7UpdateInside(p,at);
    if(DcV6Geometry.Quantize(tTerrain)<=Math.Min(DcV6Geometry.Quantize(contact.Body),DcV6Geometry.Quantize(contact.Clear))){
     if(terrain.Value.Kind=="cover")DamageCover(V6Cover(terrain.Value.ID),p.Damage);
     if(!p.RuneBolt){EndV6Flight(p,terrain.Value.Kind,at);return true;}
     var normal=terrain.Value.Normal;if(normal.sqrMagnitude<.01f||Vector2.Dot(p.Direction,normal)>=-.000001f){V7Park(p);return false;}
     p.Direction=Vector2.Reflect(p.Direction,normal).normalized;p.TrajectoryRevision++;p.Position+=normal*.001f;V6Life("bounce",p,null,at);Effect("ricochet",p.Owner,p.Room,p.Position);
    }else if(DcV6Geometry.Quantize(contact.Clear)<=DcV6Geometry.Quantize(contact.Body)){
     if(P3)P3ClearProjectile(contact.Protector,p,contact.Cast);Effect(contact.Melee?(contact.Protector.Loadout=="ranger"?"blade_block":"hammer_block"):"wind_clear",contact.Protector.ID,p.Room,p.Position);EndV6Flight(p,contact.Melee?"melee_block":"wind_clear",at);return true;
    }else if(p.Enemy){v6ContactTime=now+at*1000/30;Hurt(contact.Player,p.Damage,p.Action);v6ContactTime=-1;EndV6Flight(p,"target_hit",at);return true;}
    else{
     var victim=contact.Victim;var before=victim.Health;var instance=p.Action;
     if(p.RuneBolt){p.Inside.Add(victim.ID);p.Entries.TryGetValue(victim.ID,out var number);p.Entries[victim.ID]=++number;instance=p.ID+":"+victim.ID+":"+number;}
     else p.Hit.Add(victim.ID);
     Hit(victim,p.Owner,instance,p.Damage,p.Direction,true,p.SourceLife,p.EmissionFrame,p.Root,p.FrostEnabled);
     if(before!=victim.Health&&p.RuneBolt&&p.ChainEnabled&&!p.ChainTriggered&&v5ChainRoots.Add(p.Root??p.Action)){
      p.ChainTriggered=true;var next=enemies.Where(e=>e.Health>0&&e.ID!=victim.ID&&e.Room==p.Room&&Vector2.Distance(Center(e),Center(victim))<=3&&ClearLine(Layout(e.Room),Center(victim),Center(e))).OrderBy(e=>Vector2.Distance(Center(e),Center(victim))).ThenBy(e=>e.ID,StringComparer.Ordinal).FirstOrDefault();
      if(next!=null)Hit(next,p.Owner,p.Action+"_chain",p.Damage*700/1000,null,true,p.SourceLife,p.EmissionFrame,p.Root,p.FrostEnabled);
     }
     if(!p.RuneBolt&&p.Penetration--<=0){EndV6Flight(p,"target_hit",at);return true;}p.Position+=p.Direction*.001f;
    }
    consumed=at;remainingFraction*=1-earliest;start=p.Position;end=start+p.Direction*distance;if(distance<=.00001f){p.ContactStalls=0;return false;}
   }
   V7Park(p);return false;
  }
  private void V7Park(Projectile p){
   V7ContactBudgetParks++;p.ContactStalls++;
   if(p.ContactStalls>=30){v7PhysicsWaitUntil=now+500;TechnicalWait(true,now);Effect("technical_wait",null,p.Room,p.Position);}
  }
 }
}
