using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 public sealed partial class DcP1World {
  public readonly bool V8;
  private bool V8Spinning(Player p)=>p.SpinRoot!=null&&now<p.SpinUntil&&p.Health>0&&!p.Left&&p.Room==p.SpinRoom&&!Terminal;
  private void V8Battle(Player p){if(Has(p,"battle_rune"))p.BattleUntil=now+Number(upgrades["battle_rune"],"duration_ms");}
  private int V8Damage(Player p,int basis,int factor=1000)=>Math.Max(1,(int)((long)basis*factor*(now<p.BattleUntil?Number(upgrades["battle_rune"],"damage_milli_factor"):1000)/1000000));
  private void V8StopSpin(Player p,string reason){if(p.SpinRoot==null)return;p.SpinUntil=Math.Min(p.SpinUntil,now);p.SpinEndReason=reason;p.Input.Attack=false;p.Input.Melee=false;p.Input.Charge=false;p.ReleaseRequired=true;}
  private bool V8Action(Player p,string kind,out bool accepted,bool skillStage=false){
   accepted=false;
   if(V8Spinning(p)&&(kind=="attack_press"||kind=="charge_begin"||kind=="charge_release")){LastActionReason="skill_active";return true;}
   if(!skillStage||p.Loadout!="runesmith"||kind!="ability_primary"&&kind!="ability_secondary")return false;
   if(kind=="ability_primary"){
    var d=Kit(p.Loadout)["primary"];var twin=Has(p,"runesmith_twin");
    if(now<p.AbilityAt){LastActionReason="cooldown";return true;}
    if(twin&&p.TurretCharges==0){p.TurretCharges=2;p.TurretGroup=null;}
    var point=FindTurretPoint(p,p.Position,turrets.Where(t=>t.Room==p.Room).Select(t=>t.Position).ToList());
    if(!point.HasValue){LastActionReason="blocked";return true;}
    if(turrets.Count>=Number(config["authority"],"max_turrets")){LastActionReason="budget";return true;}
    var first=!twin||p.TurretCharges==2;
    if(first){p.TurretGroup=NextID("turret_group");V8Battle(p);}
    turrets.Add(new Turret{Group=p.TurretGroup,ID=NextID("turret"),Owner=p.ID,SourceLife=p.LifeGeneration,Room=p.Room,Position=point.Value,Until=now+Number(d,"lifetime_ms"),AttackAt=now,BaseDamage=Number(d,twin?"twin_damage":"damage")});
    if(twin)p.TurretCharges--;if(!twin||p.TurretCharges==0)p.AbilityAt=now+Number(d,"cooldown_ms");
   }else{
    if(now<p.SecondaryAt){LastActionReason="cooldown";return true;}
    var d=Kit(p.Loadout)["secondary"];CancelBasic(p);p.Input.Attack=false;p.ReleaseRequired=true;
    V8Battle(p);p.SpinRoot=NextID("death_hammer");p.SpinRoom=p.Room;p.SpinStart=now;p.SpinUntil=now+Number(d,"duration_ms");p.SpinPulse=0;p.SpinDamage=V8Damage(p,Number(d,"damage"));p.SpinFrost=Has(p,"frost");p.SpinChain=Has(p,"runesmith_chain");p.SpinEndReason=null;
    p.SecondaryAt=now+Number(d,"cooldown_ms");
   }
   CancelReload(p);p.InteractionTarget=null;Effect("ability",p.ID,p.Room,Center(p));LastActionReason="accepted";accepted=true;return true;
  }
  private void TickV8Skills(){
   foreach(var p in players.Values){
    if(p.SpinRoot==null)continue;
    if(!V8Spinning(p)){if(p.SpinEndReason==null)V8StopSpin(p,p.Room!=p.SpinRoom?"room_change":p.Health<=0?"life_exhausted":"expired");continue;}
    var d=Kit(p.Loadout)["secondary"];var interval=Number(d,"pulse_interval_ms");var count=Number(d,"pulse_count");
    // A restored/technical-wait state skips missed pulses; it never deals catch-up damage.
    var due=(int)((now-p.SpinStart)/interval);if(due>=count)continue;
    if(p.SpinPulse<due)p.SpinPulse=due;
    if(p.SpinPulse!=due||now<p.SpinStart+p.SpinPulse*interval)continue;
    var pulse=p.SpinPulse++;var center=Center(p);var radius=Distance(d,"radius_milli_tiles");
    foreach(var e in enemies.Where(e=>e.Health>0&&e.Room==p.Room).OrderBy(e=>e.ID,StringComparer.Ordinal).ToArray()){
     if(DcV7Geometry.Distance(center-e.Position,Body(e).Vertices)>radius||!ClearLine(Layout(p.Room),center,Center(e)))continue;
     var before=e.Health;Hit(e,p.ID,p.SpinRoot+":"+pulse+":"+e.ID,p.SpinDamage,null,true,p.LifeGeneration,WorldTick,p.SpinRoot,p.SpinFrost,true);
     if(before!=e.Health&&p.SpinChain&&v5ChainRoots.Add(p.SpinRoot)){
      var next=enemies.Where(x=>x.Health>0&&x.Room==p.Room&&x.ID!=e.ID&&Vector2.Distance(Center(x),Center(e))<=3&&ClearLine(Layout(p.Room),Center(e),Center(x))).OrderBy(x=>Vector2.Distance(Center(x),Center(e))).ThenBy(x=>x.ID,StringComparer.Ordinal).FirstOrDefault();
      if(next!=null)Hit(next,p.ID,p.SpinRoot+"_chain",p.SpinDamage*700/1000,null,true,p.LifeGeneration,WorldTick,p.SpinRoot,p.SpinFrost,true);
     }
    }
    foreach(var c in covers.Where(c=>c.Health>0&&c.Room==p.Room).ToArray()){
     if(DcV7Geometry.Distance(center-c.Position,DcV7Geometry.Rect(-.4375f,-.4375f,.875f,.875f))<=radius&&V8CoverVisible(p.Room,center,c))DamageCover(c,p.SpinDamage);
    }
   }
  }
  private bool V8CoverVisible(int room,Vector2 from,Cover target){
   if(DcV6Geometry.Terrain(Layout(room),from,target.Position,0).HasValue)return false;
   foreach(var c in V6Covers(room,from,target.Position,0))if(c.ID!=target.ID&&CoverSolid(c)&&DcV6Geometry.Box(from,target.Position,c.Position.x-.4375f,c.Position.y-.4375f,.875f,.875f,0).HasValue)return false;return true;
  }
  private Vector2[] V8Head(MeleeV5 a){var direction=a.Direction.normalized;var side=new Vector2(-direction.y,direction.x);var charge=Kit("runesmith")["melee"]["charge"];var depth=Distance(charge,"head_depth_milli_tiles");var half=Distance(charge,"head_width_milli_tiles")/2;return ((JArray)charge["head_contact_vertices_milli"]).Select(v=>a.Impact+direction*((float)v[0]/1000)+side*((float)v[1]/1000)).ToArray();}
  private bool V8Direct(MeleeV5 a,Enemy e)=>DcV7Geometry.Overlap(V8Head(a),Vector2.zero,Body(e).Vertices,e.Position);
  private void V8Impact(Player p,MeleeV5 a){p.LastSlam=new JObject{["root"]=a.Root,["room_id"]=p.Room,["at_ms"]=now,["x"]=a.Impact.x,["y"]=a.Impact.y,["radius"]=a.Range,["aim_x"]=a.Direction.x,["aim_y"]=a.Direction.y,["head_profile_id"]="rune_hammer_head.v8"};}
  private void V8Snapshot(JObject value){
   Put(value,"v8_stage",true);Put(value,"schema_version",8);
   foreach(var item in ((JArray)value["turrets"]).Cast<JObject>()){var t=turrets.First(t=>t.ID==(string)item["id"]);Put(item,"deployment_group",t.Group);Put(item,"born_game_ms",t.Until-Number(Kit("runesmith")["primary"],"lifetime_ms"));Put(item,"expires_game_ms",t.Until);Put(item,"owner_life",t.SourceLife);}
   foreach(var item in ((JArray)value["players"]).Cast<JObject>()){
    var p=players[(string)item["player_id"]];var twin=Has(p,"runesmith_twin");
    Put(item,"turret_charges",twin?(now>=p.AbilityAt&&p.TurretCharges==0?2:p.TurretCharges):now>=p.AbilityAt?1:0);Put(item,"turret_group",p.TurretGroup);Put(item,"battle_until_ms",p.BattleUntil);
    item["death_hammer"]=p.SpinRoot==null?JValue.CreateNull():new JObject{["root"]=p.SpinRoot,["active"]=V8Spinning(p),["room_id"]=p.SpinRoom,["started_ms"]=p.SpinStart,["ends_ms"]=p.SpinUntil,["next_pulse"]=p.SpinPulse,["damage"]=p.SpinDamage,["frost"]=p.SpinFrost,["chain"]=p.SpinChain,["end_reason"]=p.SpinEndReason};
    item["last_slam"]=p.LastSlam?.DeepClone()??JValue.CreateNull();
   }
  }
 }
}
