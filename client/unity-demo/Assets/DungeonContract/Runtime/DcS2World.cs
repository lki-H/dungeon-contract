using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private string WeaponID(Player player)=>(string)Kit(player.Loadout)[player.MeleeEquipped?"melee":"ranged"]["weapon"];
        public string ActiveWeapon(string id)=>WeaponID(players[id]);
        public string OtherWeapon(string id)=>(string)Kit(players[id].Loadout)[players[id].MeleeEquipped?"ranged":"melee"]["weapon"];
        public bool ValidSwitchPair(string id,string current,string requested){var kit=Kit(players[id].Loadout);var ranged=(string)kit["ranged"]["weapon"];var melee=(string)kit["melee"]["weapon"];return current==ranged&&requested==melee||current==melee&&requested==ranged;}
        public uint WeaponRevision(string id)=>players[id].WeaponRevision;
        public uint WeaponActionSequence(string id)=>players[id].LastActionSequence;
        public bool MeleeEquipped(string id)=>players[id].MeleeEquipped;
        public bool RequiresRelease(string id)=>players[id].ReleaseRequired;
        public void RejectWeaponAction(string id,DcP1Input intent,string actionID,string reason)
        {
            if(actionResults.ContainsKey(actionID))return;
            if(players.TryGetValue(id,out var player)&&intent.ActionSequence==player.LastActionSequence+1)player.LastActionSequence=intent.ActionSequence;
            actionResults[actionID]=reason;actionOrder.Enqueue(actionID);while(actionOrder.Count>512)actionResults.Remove(actionOrder.Dequeue());
        }
        private bool ApplyWeaponAction(string id,string kind,DcP1Input intent,string actionID)
        {
            LastActionReason="life_state";
            if(!players.TryGetValue(id,out var player))return false;
            if(intent.ActionSequence>0&&intent.ActionSequence!=player.LastActionSequence+1){LastActionReason="stale_revision";return false;}
            if(intent.ActionSequence>0)player.LastActionSequence=intent.ActionSequence;
            if(kind=="growth_choice")return ChooseGrowth(player,intent,actionID);
            if(player.Left||!player.Connected||player.Health<=0||Terminal)return false;
            if(kind=="cancel_input"){CancelBasic(player);player.Input=default;player.ReleaseRequired=true;LastActionReason="accepted";return true;}
            if(intent.WeaponRevision!=player.WeaponRevision||intent.WeaponID!=WeaponID(player)){LastActionReason="stale_weapon_revision";return false;}
            if(P2&&IsConnector(player.Room)&&(kind=="attack_press"||kind=="attack_release"||kind=="ability_primary"||kind=="ability_secondary")){LastActionReason="invalid_phase";return false;}
            player.Input.Aim=intent.Aim;player.Input.Move=intent.Move;
            if(V8&&V8Action(player,kind,out var v8Accepted))return v8Accepted;
            if(V5&&V5WeaponAction(player,kind,intent,actionID,out var v5Accepted))return v5Accepted;
            if(kind=="switch_weapon")
            {
                if(now<player.SwitchAt){LastActionReason="cooldown";return false;}
                CancelBasic(player);player.Input.Attack=false;player.Input.Melee=false;player.Input.Interact=false;player.ChargeAction=null;player.ReleaseRequired=true;
                player.MeleeEquipped=!player.MeleeEquipped;player.WeaponRevision++;player.SwitchAt=now+Number(parameters["input"],"switch_cooldown_ms");player.AttackAt=player.MeleeEquipped?player.MeleeReady:player.RangedReady;
                Effect("weapon_switch",id,player.Room,player.Position);LastActionReason="accepted";return true;
            }
            if(kind=="attack_release")
            {
                player.ReleaseRequired=false;
                if(player.MeleeEquipped)
                {
                    if(!player.Charging||intent.PressActionID!=player.ChargeAction){LastActionReason="invalid_phase";return false;}
                    player.AttackAt=player.MeleeReady;
                    var success=ApplyAction(id,"melee_release",intent);player.ChargeAction=null;return success;
                }
                player.Input.Attack=false;LastActionReason="accepted";return true;
            }
            if(kind=="attack_press")
            {
                if(player.ReleaseRequired){LastActionReason="invalid_phase";return false;}
                player.AttackAt=player.MeleeEquipped?player.MeleeReady:player.RangedReady;
                if(player.MeleeEquipped){var accepted=ApplyAction(id,"melee_begin",intent);if(accepted)player.ChargeAction=actionID;return accepted;}
                if(now<player.RangedReady||player.ReloadUntil>now){LastActionReason="cooldown";return false;}
                if(MagazineSize(player)>0&&player.Magazine==0){BeginReload(player);LastActionReason="accepted";return true;}
                if(!FirePlayer(player)){if(!V5)LastActionReason="budget";return false;}
                player.RangedReady=now+Number(Kit(player.Loadout)["ranged"],"interval_ms");player.AttackAt=player.RangedReady;player.InteractionTarget=null;LastActionReason="accepted";return true;
            }
            if(kind=="attack_cancel"){player.Charging=false;player.ChargeAction=null;player.Input.Attack=false;player.Input.Melee=false;LastActionReason="accepted";return true;}
            if(kind=="reload"&&player.MeleeEquipped){LastActionReason="not_reloadable";return false;}
            if(kind=="dodge")
            {
                if(now<player.DodgeUntil){LastActionReason="invalid_phase";return false;}
                player.DodgeSpeed=8;
            }
            if(kind=="interact_begin")player.ChargeAction=null;
            return ApplyAction(id,kind,intent);
        }
        private void FlushWeaponHeld()
        {
            foreach(var pending in waitingHeld.ToArray())
            {
                if(!players.TryGetValue(pending.Key,out var player)){waitingHeld.Remove(pending.Key);continue;}
                if(pending.Value.ActionWatermark>player.LastActionSequence)continue;
                waitingHeld.Remove(pending.Key);Input(pending.Key,pending.Value);
            }
        }
        private void CancelReload(Player player)
        {
            if(player.ReloadUntil>0)Effect("reload_cancel",player.ID,player.Room,player.Position);
            player.ReloadUntil=0;
        }
        private void CancelWeaponBuffer(Player player,string reason="canceled")
        {
            if(Weapons&&player.BufferedID!=null)actionResults[player.BufferedID]=reason;
            player.BufferedID=null;player.BufferedAction=null;
        }
        private bool CastWeaponSkill(Player player,JObject kit,bool primary)
        {
            var definition=kit[primary?"primary":"secondary"];var kind=(string)definition["id"];
            if(kind=="gust_volley")
            {
                if(V5)return CastV5Volley(player,definition);
                if(P2&&IsConnector(player.Room)){LastActionReason="invalid_phase";return false;}
                if(ProjectileLoad+3>128){LastActionReason="budget";return false;}
                var action=NextID("volley");
                foreach(var angle in (JArray)definition["angles_deg"])SpawnProjectile(player.Room,player.Position,Rotate(Aim(player.Input.Aim),(float)angle),player.ID,action,Number(definition,"damage"),Distance(definition,"projectile_speed_milli_tiles_per_second"),Distance(definition,"range_milli_tiles"),false,0);
                if(now>=player.DodgeUntil)
                {
                    player.DodgeDirection=player.Input.Move.sqrMagnitude>0.01f?player.Input.Move.normalized:player.Direction.sqrMagnitude>0.01f?player.Direction:Aim(player.Input.Aim);
                    player.DodgeUntil=now+Number(definition,"dash_duration_ms");player.DodgeSpeed=Distance(definition,"dash_distance_milli_tiles")*1000/Number(definition,"dash_duration_ms");player.InvulnerableUntil=Math.Max(player.InvulnerableUntil,now+Number(definition,"dash_invulnerable_ms"));
                    Effect("gust_dash",player.ID,player.Room,player.Position);
                }
            }
            else if(kind=="rune_turret")
            {
                if(P2&&IsConnector(player.Room)){LastActionReason="invalid_phase";return false;}
                var twin=Has(player,"runesmith_twin");var perpendicular=new Vector2(-Aim(player.Input.Aim).y,Aim(player.Input.Aim).x)*Distance(definition,"twin_offset_milli_tiles");
                var desired=twin?new[]{player.Position-perpendicular,player.Position+perpendicular}:new[]{player.Position};var selected=new List<Vector2>();
                foreach(var point in desired){var found=FindTurretPoint(player,point,selected);if(!found.HasValue){LastActionReason="blocked";return false;}selected.Add(found.Value);}
                if(turrets.Count(item=>item.Owner!=player.ID)+selected.Count>8){LastActionReason="budget";return false;}
                turrets.RemoveAll(item=>item.Owner==player.ID);
                foreach(var point in selected)turrets.Add(new Turret{ID=NextID("turret"),Owner=player.ID,SourceLife=player.LifeGeneration,Room=player.Room,Position=point,Until=now+Number(definition,"lifetime_ms"),AttackAt=now,BaseDamage=Number(definition,twin?"twin_damage":"damage")});
            }
            else
            {
                if(fields.Count(item=>item.Kind==kind)>=4){LastActionReason="budget";return false;}
                if(V8)V8Battle(player);
                fields.Add(new Field{ID=NextID("field"),Owner=player.ID,Room=player.Room,Kind=kind,Position=player.Position,Until=now+Number(definition,kind=="wind_bind"?"duration_ms":"field_duration_ms")});
            }
            if(Has(player,"ward"))GrantWeaponShield(player,15,3000);
            Effect("ability",player.ID,player.Room,player.Position);return true;
        }
        private Vector2? FindTurretPoint(Player owner,Vector2 desired,List<Vector2> selected)
        {
            var definition=Kit(owner.Loadout)["primary"];
            var candidates=new List<Vector2>{desired};var step=Distance(definition,"placement_search_step_milli_tiles");var limit=Distance(definition,"placement_search_max_milli_tiles");
            for(var radius=V8?Math.Max(step,limit/3):step;radius<=limit+0.001f&&candidates.Count<Number(definition,"placement_search_max_candidates");radius+=V8?Math.Max(step,limit/3):step)
                for(var index=0;index<8&&candidates.Count<Number(definition,"placement_search_max_candidates");index++)candidates.Add(desired+Rotate(Vector2.right,index*45)*radius);
            foreach(var point in candidates)if((V7?!V7Terrain(owner.Room,point,point,.3f,false).HasValue:!Blocked(Layout(owner.Room),point,0.3f))&&ClearLine(Layout(owner.Room),Center(owner),point)&&selected.All(other=>Vector2.Distance(other,point)>=0.6f)&&!enemies.Any(enemy=>enemy.Health>0&&enemy.Room==owner.Room&&(V7?DcV7Geometry.Distance(point-enemy.Position,Body(enemy).Vertices)<.3f:Vector2.Distance(point,enemy.Position)<Radius(enemy)+0.3f)))return point;
            return null;
        }
        private void GrantWeaponShield(Player player,int shield,int duration,string owner=null,string cast=null)
        {
            if(player.Shield>shield&&now<player.ShieldUntil)return;
            player.Shield=shield;player.ShieldUntil=now+duration;player.ShieldOwner=owner;player.ShieldCast=cast;Effect("shield",player.ID,player.Room,player.Position);
        }
        private void TickWeaponFields()
        {
            fields.RemoveAll(field=>now>=field.Until||!players.TryGetValue(field.Owner,out var owner)||owner.Health<=0||owner.Left||owner.Room!=field.Room);
            var pushed=new HashSet<string>();
            foreach(var field in fields)
            {
                var owner=players[field.Owner];field.Position=Center(owner);var definition=Kit(owner.Loadout)["secondary"];var radius=Distance(definition,"radius_milli_tiles");
                if(field.Kind=="wind_bind")
                {
                    foreach(var enemy in enemies.Where(enemy=>enemy.Health>0&&enemy.Room==field.Room&&enemy.Kind!="elite"&&enemy.Kind!="boss"&&(V7?DcV7Geometry.Distance(field.Position-enemy.Position,Body(enemy).Vertices)<radius:Vector2.Distance(enemy.Position,field.Position)<radius+Radius(enemy))).OrderBy(enemy=>enemy.ID,StringComparer.Ordinal))
                    {
                        var interrupted=enemy.WarningUntil>combatNow||enemy.ChargeUntil>combatNow;
                        enemy.Warning=null;enemy.WarningUntil=0;enemy.ChargeUntil=0;enemy.AttackAction=null;enemy.ReservedShots=0;enemy.SuppressedUntil=now+50;enemy.AttackAt=Math.Max(enemy.AttackAt,combatNow+50);
                        if(!pushed.Add(enemy.ID))continue;
                        var direction=(Center(enemy)-field.Position).normalized;if(direction.sqrMagnitude<0.01f)direction=Aim(owner.Input.Aim);
                        var gap=V7?radius+Distance(definition,"edge_margin_milli_tiles")-DcV7Geometry.Distance(field.Position-enemy.Position,Body(enemy).Vertices):radius+Radius(enemy)+Distance(definition,"edge_margin_milli_tiles")-Vector2.Distance(enemy.Position,field.Position);var amount=Math.Min(Distance(definition,"push_speed_milli_tiles_per_second")/30,gap);
                        var before=enemy.Position;enemy.Position=V7?V7MoveEnemy(enemy,direction*Math.Max(0,amount)):Move(Layout(enemy.Room),enemy.Position,direction*Math.Max(0,amount),Radius(enemy));enemy.StuckAt=combatNow;
                        if(P3)P3Control(owner,enemy,field.ID,interrupted||Vector2.Distance(before,enemy.Position)>0.001f);
                        Effect("wind_push",owner.ID,enemy.Room,enemy.Position,0,enemy.ID);
                    }
                }
                else foreach(var member in players.Values.Where(member=>!member.Left&&member.Health>0&&member.Room==field.Room&&Vector2.Distance(Center(member),field.Position)<=radius))
                    if(field.Granted.Add(member.ID))GrantWeaponShield(member,Number(definition,"shield"),Number(definition,"shield_duration_ms"),owner.ID,field.ID);
            }
        }
        private bool WeaponWindBlocks(Enemy enemy,Vector2 point)=>Weapons&&enemy.Kind!="elite"&&enemy.Kind!="boss"&&fields.Any(field=>field.Kind=="wind_bind"&&field.Room==enemy.Room&&(V7?DcV7Geometry.Distance(field.Position-point,Body(enemy).Vertices)<Distance(Kit(players[field.Owner].Loadout)["secondary"],"radius_milli_tiles")&&DcV7Geometry.Distance(field.Position-point,Body(enemy).Vertices)<DcV7Geometry.Distance(field.Position-enemy.Position,Body(enemy).Vertices):Vector2.Distance(point,field.Position)<Distance(Kit(players[field.Owner].Loadout)["secondary"],"radius_milli_tiles")+Radius(enemy)&&Vector2.Distance(point,field.Position)<Vector2.Distance(enemy.Position,field.Position)));
        private bool WeaponErase(Projectile projectile)
        {
            if(!Weapons||!projectile.Enemy)return false;
            var field=fields.FirstOrDefault(field=>field.Kind=="wind_bind"&&field.Room==projectile.Room&&Vector2.Distance(field.Position,projectile.Position)<=Distance(Kit(players[field.Owner].Loadout)["secondary"],"radius_milli_tiles")+projectile.CollisionRadius);
            if(field==null)return false;if(P3)P3ClearProjectile(players[field.Owner],projectile,field.ID);Effect("projectile_erased",field.Owner,projectile.Room,projectile.Position);return true;
        }
    }
}
