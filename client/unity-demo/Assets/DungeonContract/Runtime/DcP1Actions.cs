using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public struct DcP1Input
    {
        public int ChoiceNode;
        public int ChoiceRevision;
        public string ChoiceHash;
        public string ChoiceOption;
        public Vector2 Move;
        public ushort Aim;
        public bool Attack;
        public bool AttackPressed;
        public bool Melee;
        public bool Charge;
        public bool Interact;
        public uint Sequence;
        public Vector2 Placement;
        public bool HasPlacement;
        public string WeaponID;
        public uint WeaponRevision;
        public uint ActionSequence;
        public uint ActionWatermark;
        public string PressActionID;
        public string AttackEpisode;
        public string ChargeEpisode;
    }

    public sealed partial class DcP1World
    {
        private JObject Kit(string loadout)=>(JObject)parameters["loadouts"].First(item=>(string)item["id"]==loadout);
        public void QueueAction(string player,string kind,DcP1Input intent,string actionID)
        {
            if(P2&&technicalWaiting){RejectWeaponAction(player,intent,actionID,"technical_wait");return;}
            if(queued.Count>=32) {LastActionReason="budget";if(V6&&players.TryGetValue(player,out var overloaded)){overloaded.RejectedActionThrough=Math.Max(overloaded.RejectedActionThrough,intent.ActionSequence);actionResults[actionID]="budget";actionOrder.Enqueue(actionID);while(actionOrder.Count>512)actionResults.Remove(actionOrder.Dequeue());}return;}
            queued.Enqueue(new QueuedAction {Player=player,Kind=kind,Intent=intent,ID=actionID});
        }
        public bool Action(string id,string kind,DcP1Input? intent=null) => ExecuteAction(id,kind,intent??(players.TryGetValue(id,out var player)?player.Input:default),NextID("action"));
        public string Receipt(string actionID)=>actionResults.TryGetValue(actionID,out var reason)?reason:"pending";

        private bool ExecuteAction(string id,string kind,DcP1Input intent,string actionID)
        {
            if(actionResults.TryGetValue(actionID,out var prior)) {LastActionReason=prior;return prior=="accepted"||prior=="buffered";}
            bool accepted;v7ExecutingAction=V7?actionID:null;try{accepted=Weapons?ApplyWeaponAction(id,kind,intent,actionID):ApplyAction(id,kind,intent);}finally{v7ExecutingAction=null;}
            if(P3&&accepted&&kind!="growth_choice"&&kind!="attack_cancel"&&kind!="interact_begin"&&kind!="interact_cancel"&&players.TryGetValue(id,out var acting)){acting.RescueTarget=null;if(kind=="attack_press"||kind=="attack_release"||kind=="dodge"||kind=="ability_primary"||kind=="ability_secondary")acting.LastCombatAt=now;}
            if(Weapons&&LastActionReason=="buffered")players[id].BufferedID=actionID;
            actionResults[actionID]=LastActionReason;actionOrder.Enqueue(actionID);
            while(actionOrder.Count>512)actionResults.Remove(actionOrder.Dequeue());
            return accepted;
        }
        private bool ApplyAction(string id,string kind,DcP1Input intent)
        {
            LastActionReason="life_state";
            if(!players.TryGetValue(id,out var player)||player.Left||!player.Connected||player.Health<=0||Terminal)return false;
            if(kind=="cancel_input"||kind=="melee_cancel"||kind=="interact_cancel") {CancelBasic(player);player.Input=default;LastActionReason="accepted";return true;}
            var kit=Kit(player.Loadout);
            player.Input.Aim=intent.Aim;player.Input.Placement=intent.Placement;player.Input.HasPlacement=intent.HasPlacement;
            if(kind=="ranged_press")
            {
                if(player.Charging||player.Input.Melee){LastActionReason="invalid_phase";return false;}
                player.Input.AttackPressed=true;LastActionReason="accepted";return true;
            }
            if(kind=="melee_begin")
            {
                if(player.Charging||now<player.AttackAt){LastActionReason="cooldown";return false;}
                player.Charging=true;player.ChargeAt=now;player.Input.Melee=true;player.Input.Attack=false;player.Input.AttackPressed=false;player.ReloadUntil=0;player.InteractionTarget=null;
                Effect("charge",id,player.Room,player.Position);LastActionReason="accepted";return true;
            }
            if(kind=="melee_release")
            {
                if(!player.Charging){LastActionReason="invalid_phase";return false;}
                Melee(player,kit,intent.Aim);player.Charging=false;player.Input.Melee=false;player.InteractionTarget=null;LastActionReason="accepted";return true;
            }
            if(kind=="reload")
            {
                LastActionReason=Number(kit["ranged"],"magazine_size")==0?"not_reloadable":"invalid_phase";
                if(Number(kit["ranged"],"magazine_size")==0||player.Charging||player.Input.Melee||player.ReloadUntil>now||player.Magazine>=MagazineSize(player))return false;
                BeginReload(player);LastActionReason="accepted";return true;
            }
            if(kind=="interact_begin") {CancelBasic(player);player.Input.Interact=true;LastActionReason="accepted";return true;}
            if(kind!="dodge"&&kind!="ability_primary"&&kind!="ability_secondary") {LastActionReason="invalid_phase";return false;}
            var ready=kind=="dodge"?player.DodgeAt:kind=="ability_primary"?player.AbilityAt:player.SecondaryAt;
            if(now<ready)
            {
                LastActionReason="cooldown";
                if(ready-now<=Number(parameters["player"],"input_buffer_ms")&&player.BufferedAction==null){player.BufferedAction=kind;player.BufferUntil=now+100;player.BufferedIntent=intent;LastActionReason="buffered";return true;}
                return false;
            }
            if(V8&&V8Action(player,kind,out var v8Skill,true))return v8Skill;
            if(kind=="dodge")
            {
                if(V5){player.DodgeSpeed=V6?Distance(parameters["player"],"dodge_distance_milli_tiles")*1000/Number(parameters["player"],"dodge_duration_ms"):8;player.MeleeAttack=null;}
                player.DodgeDirection=intent.Move.sqrMagnitude>0.01f?intent.Move.normalized:player.Direction.sqrMagnitude>0.01f?player.Direction:Aim(intent.Aim);
                player.DodgeAt=now+Math.Max(1800,Number(parameters["player"],"dodge_cooldown_ms")-(Has(player,"swift")?600:0));player.DodgeUntil=now+Number(parameters["player"],"dodge_duration_ms");player.InvulnerableUntil=now+Number(parameters["player"],"dodge_invulnerable_ms");if(V6){player.DashSource="space";player.SpaceUntil=player.InvulnerableUntil;}Effect("dodge",id,player.Room,player.Position);
            }
            else
            {
                if(!Cast(player,kit,kind=="ability_primary")){return false;}
                if(kind=="ability_primary")player.AbilityAt=now+Number(kit["primary"],"cooldown_ms");else player.SecondaryAt=now+Number(kit["secondary"],"cooldown_ms");
            }
            if(Weapons)CancelReload(player);else player.ReloadUntil=0;player.InteractionTarget=null;LastActionReason="accepted";return true;
        }

        private void CancelBasic(Player player) {player.Charging=false;player.ChargeRequested=false;player.MeleeAttack=null;if(V5){player.AttackEpisode=null;player.ChargeAction=null;}if(Weapons)CancelReload(player);else player.ReloadUntil=0;player.InteractionTarget=null;CancelWeaponBuffer(player);player.Input.AttackPressed=false;player.Input.Melee=false;player.Input.Charge=false;}
        private int MagazineSize(Player player)=>Number(Kit(player.Loadout)["ranged"],"magazine_size")+(Has(player,"ranger_tension")?2:0);
        private void BeginReload(Player player) {player.ReloadDuration=Math.Max(1,Number(Kit(player.Loadout)["ranged"],"reload_ms")-(Has(player,"ranger_tension")?200:0));player.ReloadStarted=now;player.ReloadUntil=now+player.ReloadDuration;Effect("reload",player.ID,player.Room,player.Position);}
        private void TickPlayer(Player player,bool safe)
        {
            if(player.Left||player.Health<=0)return;
            TriggerP4Shield(player);
            if(now-player.InputAt>250) {player.Input=default;CancelBasic(player);}
            if(player.ShieldUntil<=now)player.Shield=0;
            if(player.ReloadUntil>0&&now>=player.ReloadUntil){player.Magazine=MagazineSize(player);player.ReloadUntil=0;Effect("reload_done",player.ID,player.Room,player.Position);}
            if(player.BufferedAction!=null)
            {
                var action=player.BufferedAction;var ready=action=="dodge"?player.DodgeAt:action=="ability_primary"?player.AbilityAt:player.SecondaryAt;
                if(now>player.BufferUntil)CancelWeaponBuffer(player,"expired");
                else if(now>=ready)
                {
                    var intent=player.BufferedIntent;var bufferedID=player.BufferedID;player.BufferedAction=null;player.BufferedID=null;
                    if(Weapons&&action=="dodge"&&now<player.DodgeUntil)LastActionReason="invalid_phase";
                    else{if(Weapons&&action=="dodge")player.DodgeSpeed=V6?Distance(parameters["player"],"dodge_distance_milli_tiles")*1000/Number(parameters["player"],"dodge_duration_ms"):8;ApplyAction(player.ID,action,intent);}
                    if(Weapons&&bufferedID!=null)actionResults[bufferedID]=LastActionReason;
                }
            }
            if(Weapons)player.AttackAt=player.MeleeEquipped?player.MeleeReady:player.RangedReady;
            if(player.Input.Move.sqrMagnitude>0.01f)player.Direction=player.Input.Move.normalized;
            var dodging=now<player.DodgeUntil;
            if(V7)MoveV7Player(player);else if(V6)MoveV6Player(player);else{
            var velocity=dodging?player.DodgeDirection*(Weapons?player.DodgeSpeed:8):player.Input.Move*5*(V5?V5Movement(player):player.Charging?Number(parameters["player"],"charge_move_milli_factor")/1000f:1);
            var before=player.Position;
            player.Position=Move(Layout(player.Room),before,velocity/30,0.3f,point=>enemies.Any(enemy=>enemy.Health>0&&enemy.Room==player.Room&&(P2?enemy.Kind=="elite"||enemy.Kind=="boss":!dodging||enemy.Kind=="elite"||enemy.Kind=="boss")&&Vector2.Distance(point,enemy.Position)<Radius(enemy)+0.3f&&Vector2.Distance(point,enemy.Position)<Vector2.Distance(before,enemy.Position)));
            }
            TryDoor(player);
            if(V5)TickV5Melee(player);
            if((!V6||!IsConnector(player.Room))&&(!Weapons||!player.MeleeEquipped&&!player.ReleaseRequired)&&(player.Input.Attack||player.Input.AttackPressed)&&(!V8||!V8Spinning(player))&&!player.Charging&&!player.Input.Melee&&now>=player.AttackAt&&player.ReloadUntil==0)
            {
                if(MagazineSize(player)>0&&player.Magazine==0)BeginReload(player);
                else if(FirePlayer(player)){player.InteractionTarget=null;if(P3){player.RescueTarget=null;player.LastCombatAt=now;}player.AttackAt=now+Number(Kit(player.Loadout)["ranged"],"interval_ms");if(Weapons)player.RangedReady=player.AttackAt;}
            }
            player.Input.AttackPressed=false;
            if(!P3&&player.Room==2&&rooms[2].Status=="cleared"&&!player.Reached.Contains("inspect_zone")&&Vector2.Distance(player.Position,new Vector2(15,5))<1.5f){player.Reached.Add("inspect_zone");player.Reaches++;}
        }
        private bool FirePlayer(Player player)
        {
            if(V5)return FireV5(player);
            var ranged=Kit(player.Loadout)["ranged"];var fan=Has(player,"ranger_fan");var angles=fan?new[]{-12f,0f,12f}:new[]{0f};
            if(ProjectileLoad+angles.Length>128)return false;
            var damage=Number(ranged,"damage")*(fan?650:1000)/1000;var action=NextID("shot");
            foreach(var angle in angles){SpawnProjectile(player.Room,player.Position,Rotate(Aim(player.Input.Aim),angle),player.ID,action,Math.Max(1,damage),Weapons?Distance(ranged,"projectile_speed_milli_tiles_per_second"):16,Weapons&&P2?0:Weapons?Distance(ranged,"range_milli_tiles"):10,false,(Weapons?Number(ranged,"initial_extra_targets"):0)+(Has(player,"ranger_pierce")?1:0));if(Weapons){var projectile=projectiles.Last();projectile.CollisionRadius=Distance(ranged,"projectile_radius_milli_tiles");projectile.RuneBolt=player.Loadout=="runesmith";projectile.Bounces=projectile.RuneBolt?Number(ranged,"bounce_count"):0;projectile.Unbounded=P2;}}
            if(MagazineSize(player)>0)player.Magazine--;
            Effect("shot",player.ID,player.Room,player.Position);return true;
        }
        private void Melee(Player player,JObject kit,ushort aim)
        {
            var melee=kit["melee"];var held=Math.Min(now-player.ChargeAt,Number(parameters["player"],"charge_full_ms")-(Has(player,"ranger_tension")?200:0));var heavy=held>=Number(parameters["player"],"melee_tap_boundary_ms");
            var fraction=Mathf.Clamp01((held-250f)/Math.Max(1,Number(parameters["player"],"charge_full_ms")-(Has(player,"ranger_tension")?200:0)-250));
            var damage=heavy?Mathf.FloorToInt(Mathf.Lerp(Number(melee,"heavy_min_damage"),Number(melee,"heavy_full_damage"),fraction)):Number(melee,"light_damage");
            if(Has(player,"ranger_fan"))damage=Math.Max(1,damage*800/1000);
            var range=Distance(melee,heavy?"heavy_range_milli_tiles":"range_milli_tiles")+(Has(player,"ranger_pierce")?0.35f:0)+(heavy&&Has(player,"runesmith_heavy")?0.35f:0);
            var arc=Number(melee,heavy?"heavy_arc_deg":"arc_deg")+(Has(player,"ranger_fan")?30:0);var direction=Aim(aim);var action=NextID("melee");
            var struck=new List<Enemy>();
            foreach(var enemy in enemies.Where(item=>item.Health>0&&item.Room==player.Room).ToArray())
                if(InArc(player.Position,enemy.Position,direction,range+Radius(enemy),arc)&&ClearLine(Layout(player.Room),player.Position,enemy.Position)){Hit(enemy,player.ID,action,damage,direction);struck.Add(enemy);}
            if(Weapons&&player.Loadout=="runesmith"&&Has(player,"runesmith_chain")&&struck.Count>0)
            {
                var source=struck.OrderBy(item=>Vector2.Distance(item.Position,player.Position)).First();
                var chain= enemies.Where(item=>item.Health>0&&item.Room==player.Room&&!struck.Contains(item)&&Vector2.Distance(item.Position,source.Position)<=3f&&ClearLine(Layout(item.Room),source.Position,item.Position)).OrderBy(item=>Vector2.Distance(item.Position,source.Position)).ThenBy(item=>item.ID,StringComparer.Ordinal).FirstOrDefault();
                if(chain!=null)Hit(chain,player.ID,action,damage*700/1000,direction);
            }
            foreach(var cover in covers.Where(item=>frameCovers.Contains(item.ID)&&item.Room==player.Room).ToArray())if(InArc(player.Position,cover.Position,direction,range+0.45f,arc))DamageCover(cover,damage);
            if(heavy)player.HeavyCount++;else player.LightCount++;
            player.AttackAt=now+(heavy?Number(melee,"heavy_recovery_ms"):Number(melee,"light_interval_ms"))*(Has(player,"runesmith_twin")?900:1000)/1000;
            if(Weapons)player.MeleeReady=player.AttackAt;
            Effect(heavy?"heavy":"light",player.ID,player.Room,player.Position,damage);player.ReloadUntil=0;
        }
        public static bool InArc(Vector2 origin,Vector2 target,Vector2 direction,float radius,float arc) => Vector2.Distance(origin,target)<=radius&&(target==origin||Vector2.Dot((target-origin).normalized,direction)>=Mathf.Cos(arc*0.5f*Mathf.Deg2Rad));
        private bool Cast(Player player,JObject kit,bool primary)
        {
            if(Weapons)return CastWeaponSkill(player,kit,primary);
            var definition=kit[primary?"primary":"secondary"];var kind=(string)definition["id"];
            if(kind=="gust_volley")
            {
                if(ProjectileLoad+3>128){LastActionReason="budget";return false;}
                var action=NextID("volley");foreach(var angle in (JArray)definition["angles_deg"])SpawnProjectile(player.Room,player.Position,Rotate(Aim(player.Input.Aim),(float)angle),player.ID,action,Number(definition,"damage"),16,10,false,0);
            }
            else
            {
                var point=player.Input.Placement;
                LastActionReason="out_of_range";
                if(!player.Input.HasPlacement||Vector2.Distance(player.Position,point)>Distance(definition,"placement_range_milli_tiles"))return false;
                var twin=kind=="rune_turret"&&Has(player,"runesmith_twin");var perpendicular=new Vector2(-Aim(player.Input.Aim).y,Aim(player.Input.Aim).x)*0.4f;
                var points=twin?new[]{point-perpendicular,point+perpendicular}:new[]{point};LastActionReason="blocked";
                if(points.Any(value=>Blocked(Layout(player.Room),value,0.3f)||!ClearLine(Layout(player.Room),player.Position,value)||enemies.Any(enemy=>enemy.Health>0&&enemy.Room==player.Room&&Vector2.Distance(value,enemy.Position)<Radius(enemy)+0.3f)))return false;
                if(kind=="rune_turret")
                {
                    if(turrets.Count(item=>item.Owner!=player.ID)+points.Length>8){LastActionReason="budget";return false;}
                    turrets.RemoveAll(item=>item.Owner==player.ID);
                    foreach(var value in points)turrets.Add(new Turret{ID=NextID("turret"),Owner=player.ID,Room=player.Room,Position=value,Until=now+Number(definition,"lifetime_ms"),AttackAt=now});
                }
                else
                {
                    if(fields.Count(item=>item.Kind==kind)>=4){LastActionReason="budget";return false;}
                    fields.Add(new Field{ID=NextID("field"),Owner=player.ID,Room=player.Room,Kind=kind,Position=point,Until=now+Number(definition,kind=="wind_bind"?"duration_ms":"field_duration_ms")});
                }
            }
            if(Has(player,"ward")){player.Shield=Math.Max(player.Shield,15);player.ShieldUntil=now+3000;}
            Effect("ability",player.ID,player.Room,player.Position);return true;
        }
        private void TickFields()
        {
            if(Weapons){TickWeaponFields();return;}
            fields.RemoveAll(item=>now>=item.Until||players[item.Owner].Health<=0||players[item.Owner].Left);
            foreach(var field in fields)
            {
                var definition=Kit(players[field.Owner].Loadout)["secondary"];
                if(field.Kind=="wind_bind")foreach(var enemy in enemies.Where(item=>item.Health>0&&item.Room==field.Room&&Vector2.Distance(item.Position,field.Position)<=Distance(definition,"radius_milli_tiles"))){ApplySlow(enemy,enemy.Kind=="boss"?0.95f:0.75f,70);}
                else foreach(var player in players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==field.Room&&Vector2.Distance(item.Position,field.Position)<=Distance(definition,"radius_milli_tiles")))if(field.Granted.Add(player.ID)){player.Shield=Math.Max(player.Shield,Number(definition,"shield"));player.ShieldUntil=now+Number(definition,"shield_duration_ms");Effect("shield",player.ID,player.Room,player.Position);}
            }
        }
        private void ApplySlow(Enemy enemy,float factor,int duration)
        {
            if(enemy.SlowUntil<=now||factor<enemy.SlowFactor){enemy.SlowFactor=factor;enemy.SlowUntil=now+duration;}
            else if(Math.Abs(factor-enemy.SlowFactor)<0.001f)enemy.SlowUntil=Math.Max(enemy.SlowUntil,now+duration);
        }
    }
}
