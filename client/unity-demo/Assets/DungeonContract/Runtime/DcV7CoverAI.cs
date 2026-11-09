using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public sealed partial class DcP1World {
  private JObject V7NavigationLayout(int room){
   var solid=covers.Where(c=>c.Room==room&&CoverSolid(c)).ToArray();var revision=string.Join("|",solid.Select(c=>c.ID+":"+c.Revision));
   if(!pathLayouts.TryGetValue(room,out var cached)||cached.revision!=revision){var map=RoomGeometry.NavigationLayout(Layout(room));foreach(var c in solid)((JArray)map["obstacles"]).Add(new JArray(c.Position.x-.4375f,c.Position.y-.4375f,.875f,.875f));cached=(revision,map);pathLayouts[room]=cached;}return cached.layout;
  }
  private Vector2? V7CoverStandPoint(Enemy enemy,Cover cover){
   var body=Body(enemy);var away=(Center(enemy)-cover.Position).normalized;if(away.sqrMagnitude<.01f)away=Vector2.right;
   foreach(var angle in v6CoverApproachAngles){var axis=Rotate(away,angle);var support=body.Vertices.Max(v=>Vector2.Dot(v-body.Center,-axis));var distance=support+.4375f*(Math.Abs(axis.x)+Math.Abs(axis.y))+.08f;var point=cover.Position+axis*distance-body.Center;if(!V7Blocked(enemy.Room,body,point)&&ClearV5CoverLine(enemy.Room,point+body.Center,cover))return point;}return null;
  }
  private bool TickV7CoverAI(Enemy enemy){
   if(enemy.Kind=="boss"||technicalWaiting||enemy.ReadyAt==long.MaxValue||enemy.Health<=0)return false;
   if(enemy.SuppressedUntil>now){enemy.CoverTarget=null;return false;}var target=P21EnemyTarget(enemy);if(target==null)return false;
   var body=Body(enemy);var center=Center(enemy);var targetCenter=Center(target);var selected=covers.FirstOrDefault(c=>c.ID==enemy.CoverTarget&&c.Health>0&&c.Room==enemy.Room);
   if(combatNow>=enemy.CoverProbeAt){
    var blocked=V7BodyContact(enemy.Room,body,enemy.Position,target.Position).HasValue;var slow=Vector2.Distance(enemy.Position,enemy.CoverProbePosition)<.1f;enemy.CoverProbePosition=enemy.Position;enemy.CoverProbeAt=combatNow+200;
    var half=(body.Max-body.Min)/2;var direct=Vector2.Distance(center,targetCenter);var detour=blocked?DcV7Navigation.Distance(V7NavigationLayout(enemy.Room),center,target.Position+body.Center,half):direct;
    enemy.CoverNoDetour=detour-direct>6;if(!blocked&&!slow||!enemy.CoverNoDetour)enemy.CoverBlockedAt=0;else if(enemy.CoverBlockedAt==0)enemy.CoverBlockedAt=combatNow;
   }
   if(selected==null&&enemy.CoverBlockedAt>0&&combatNow-enemy.CoverBlockedAt>=1000&&enemy.CoverNoDetour&&enemy.WarningUntil==0){
    selected=covers.Where(c=>c.Room==enemy.Room&&c.Health>0&&Vector2.Distance(c.Position,center)<6&&ClearV5CoverLine(enemy.Room,center,c)).OrderBy(c=>Vector2.Distance(c.Position,center)+Vector2.Distance(c.Position,targetCenter)).ThenBy(c=>c.ID,StringComparer.Ordinal).FirstOrDefault();
    if(selected!=null){enemy.CoverTarget=selected.ID;enemy.CoverTargetUntil=combatNow+500;}
   }
   if(selected==null){enemy.CoverTarget=null;return false;}
   if(enemy.Kind=="charger"&&enemy.ChargeUntil>combatNow){var definition=enemyDefinitions[enemy.Kind];var end=enemy.Position+enemy.Aim*Distance(definition,"charge_speed_milli_tiles_per_second",6000)/30;var hit=V7BodyContact(enemy.Room,body,enemy.Position,end);if(hit.HasValue){enemy.Position=Vector2.Lerp(enemy.Position,end,Math.Max(0,hit.Value.Time-.001f));if(hit.Value.Kind=="cover")DamageCover(covers.First(c=>c.ID==hit.Value.ID),Number(definition,"damage"));enemy.ChargeUntil=0;enemy.AttackAt=combatNow+Number(definition,"recovery_ms",500);}else enemy.Position=end;enemy.StuckAt=combatNow;return true;}
   if(combatNow>=enemy.CoverTargetUntil&&ClearLine(Layout(enemy.Room),center,targetCenter)){enemy.CoverTarget=null;return false;}
   var attack=enemyDefinitions[enemy.Kind];var range=enemy.Kind=="archer"?8:Distance(attack,"attack_range_milli_tiles",1200);var coverDistance=Vector2.Distance(center,selected.Position);
   if(enemy.WarningUntil>0){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;if(combatNow<enemy.WarningUntil)return true;enemy.PresentationAttackAt=combatNow;enemy.WarningUntil=0;enemy.Warning=null;enemy.AttackAt=Math.Max(enemy.AttackAt,combatNow+Number(attack,"recovery_ms",500));
    if(enemy.Kind=="charger"){enemy.ChargeUntil=combatNow+Number(attack,"charge_duration_ms",600);return true;}
    if(enemy.Kind=="archer"){for(var i=0;i<3;i++){enemy.ReservedShots=Math.Max(0,enemy.ReservedShots-1);EmitV5Enemy(enemy,enemy.AttackAction,enemy.Position,Rotate(enemy.Aim,(i-1)*12),Number(attack,"damage"),Distance(attack,"projectile_speed_milli_tiles_per_second"));}}
    else if(coverDistance<=range+.4375f&&ClearV5CoverLine(enemy.Room,center,selected))DamageCover(selected,Number(attack,"damage"));return true;
   }
   if(coverDistance>range+.35f||!ClearV5CoverLine(enemy.Room,center,selected)){var stand=V7CoverStandPoint(enemy,selected);if(!stand.HasValue){enemy.CoverTarget=null;enemy.CoverBlockedAt=0;enemy.PathAt=0;return false;}var direction=EnemyPath(enemy,stand.Value);enemy.Position=WalkV7Enemy(enemy,SeparateEnemy(enemy,direction)*Distance(attack,"move_milli_tiles_per_second")/30);if(Vector2.Distance(enemy.Position,enemy.StuckPosition)>.3f){enemy.StuckPosition=enemy.Position;enemy.StuckAt=combatNow;}else if(combatNow-enemy.StuckAt>=5000){enemy.CoverTarget=null;enemy.CoverBlockedAt=0;enemy.CoverProbeAt=combatNow+500;enemy.PathAt=0;}return true;}
   enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;if(combatNow<enemy.AttackAt||combatNow<enemy.ReadyAt)return true;if(enemy.Kind=="archer"&&ProjectileLoad+3>V5ProjectileCapacity)return true;
   enemy.AttackAt=combatNow+Number(attack,"attack_interval_ms",1000);enemy.ReservedShots=enemy.Kind=="archer"?3:0;enemy.Warning="cover_attack";enemy.WarningUntil=combatNow+Number(attack,"telegraph_ms",600);enemy.AttackAction=NextID("cover_attack");enemy.Origin=enemy.Position;enemy.Aim=(selected.Position-center).normalized;return true;
  }
 }
}
