using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private sealed class MeleeV5
        {
            public float V7AngleStart,V7AngleEnd,V7Aim;public long V7AngleTick=-1;
            public string Root;
            public string Weapon;
            public string Phase="windup";
            public long StartedTick;
            public long ActiveTick;
            public long RecoveryTick;
            public long EndTick;
            public long PreviousReady;
            public bool Heavy;
            public bool Frozen;
            public bool Frost;
            public bool Chain;
            public int Damage;
            public float Range;
            public float Arc;
            public Vector2 Direction;
            public Vector2 Impact; public bool ImpactValid; public Vector2 PreviewImpact; public bool PreviewValid; public Vector2 PreviousCenter;
            public float SweepStart;
            public float SweepEnd;
            public HashSet<string> HitTargets=new HashSet<string>();
            public HashSet<string> HitCovers=new HashSet<string>();
        }

        private readonly HashSet<string> v5ChainRoots=new HashSet<string>();
        private readonly int[] v5NetworkCounts=new int[43];
        private int ChooseV5NetworkGroup()
        {
            Array.Clear(v5NetworkCounts,0,v5NetworkCounts.Length);
            foreach(var projectile in projectiles)v5NetworkCounts[projectile.NetworkGroup]++;
            var selected=0;for(var index=1;index<v5NetworkCounts.Length;index++)if(v5NetworkCounts[index]<v5NetworkCounts[selected])selected=index;
            return selected;
        }
        private int V5ProjectileCapacity=>Number(config["authority"],"max_projectiles");
        private static int V5Milliseconds(int ticks)=>(ticks*1000+29)/30;

        private bool V5WeaponAction(Player player,string kind,DcP1Input intent,string actionID,out bool accepted)
        {
            accepted=false;
            if(kind=="attack_press"&&!player.ReleaseRequired){player.AttackEpisode=actionID;if(V7)player.LastAttackEpisode=actionID;}
            if(kind=="attack_release")
            {
                if(player.AttackEpisode==null||intent.PressActionID!=player.AttackEpisode){LastActionReason="stale_episode";return true;}
                player.AttackEpisode=null;
                player.Input.Attack=false;player.Input.Melee=false;player.ReleaseRequired=false;LastActionReason="accepted";accepted=true;return true;
            }
            if(kind=="attack_press"&&player.MeleeEquipped)
            {
                if(player.ReleaseRequired||player.Charging){LastActionReason="invalid_phase";return true;}
                player.Input.Melee=true;accepted=BeginV5Melee(player,false);return true;
            }
            if(kind=="charge_begin")
            {
                if(IsConnector(player.Room)){LastActionReason="invalid_phase";return true;}
                if(!player.MeleeEquipped||player.Loadout!="runesmith"){LastActionReason="charge_unsupported";return true;}
                if(player.Charging){LastActionReason="invalid_phase";return true;}
                player.ChargeAction=actionID;player.ChargeRequested=true;player.Input.Charge=true;
                if(player.MeleeAttack?.Phase=="windup")
                {
                    player.MeleeReady=player.MeleeAttack.PreviousReady;player.MeleeAttack=null;
                }
                if(player.MeleeAttack==null&&now>=player.MeleeReady)StartV5Charge(player);
                LastActionReason="accepted";accepted=true;return true;
            }
            if(kind=="charge_release"||kind=="charge_cancel")
            {
                if(player.ChargeAction==null||intent.PressActionID!=player.ChargeAction){LastActionReason="stale_episode";return true;}
                var held=now-player.ChargeAt;
                player.Input.Charge=false;player.ChargeRequested=false;player.ChargeAction=null;
                if(!player.Charging||kind=="charge_cancel"){player.Charging=false;LastActionReason="accepted";accepted=true;return true;}
                player.Charging=false;
                if(held<Number(Kit(player.Loadout)["melee"]["charge"],"minimum_ms")){LastActionReason="accepted";accepted=true;return true;}
                accepted=BeginV5Melee(player,true,held);return true;
            }
            if(kind=="dodge")
            {
                if(now<player.DodgeUntil){LastActionReason="invalid_phase";return true;}
                accepted=ApplyAction(player.ID,kind,intent);
                return true;
            }
            return false;
        }

        private void StartV5Charge(Player player)
        {
            player.Charging=true;player.ChargeAt=now;player.ChargeRequested=false;
            player.Input.Attack=false;player.Input.Melee=false;CancelReload(player);player.InteractionTarget=null;
            Effect("charge",player.ID,player.Room,player.Position);
        }

        private bool BeginV5Melee(Player player,bool heavy,long held=0)
        {
            if(player.MeleeAttack!=null||now<player.MeleeReady){LastActionReason="cooldown";return false;}
            var definition=Kit(player.Loadout)["melee"];
            var phases=heavy?new[]{Number(definition["charge"],"release_windup_ticks"),1,Number(definition["charge"],"recovery_ticks")}:V5Phases(player,definition);
            var attack=new MeleeV5{Root=NextID(heavy?"slam":"swing"),Weapon=WeaponID(player),StartedTick=WorldTick,ActiveTick=WorldTick+phases[0],RecoveryTick=WorldTick+phases[0]+phases[1],EndTick=WorldTick+phases.Sum(),PreviousReady=player.MeleeReady,Heavy=heavy};
            if(V7){attack.Arc=Number(definition,"arc_deg")+(Has(player,"ranger_fan")?30:0);attack.Range=V7MeleeRange(player,heavy);attack.Direction=Aim(player.Input.Aim);}
            if(heavy)
            {
                var charge=definition["charge"];
                var minimum=Number(charge,"minimum_ms");var duration=Number(charge,"full_ms")-minimum;
                attack.Damage=Number(charge,"minimum_wave_damage")+(int)((long)(Number(charge,"full_wave_damage")-Number(charge,"minimum_wave_damage"))*Math.Max(0,Math.Min(duration,held-minimum))/Math.Max(1,duration));
            }
            if(V6){attack.PreviousCenter=player.Position;attack.PreviewValid=heavy;attack.PreviewImpact=player.Position+Aim(player.Input.Aim)*Distance(definition["charge"],"landing_distance_milli_tiles");}
            player.MeleeAttack=attack;player.MeleeReady=now+V5Milliseconds(phases.Sum());
            player.InteractionTarget=null;player.RescueTarget=null;player.LastCombatAt=now;CancelReload(player);
            Effect(heavy?"slam_windup":"melee_windup",player.ID,player.Room,player.Position);
            LastActionReason="accepted";return true;
        }

        private int[] V5Phases(Player player,JToken definition)
        {
            return MeleePhaseTicks(definition,Has(player,"ranger_tension"),Has(player,"runesmith_twin"));
        }

        public static int[] MeleePhaseTicks(JToken definition,bool rangerTension,bool runesmithTwin)
        {
            var phases=new[]{Number(definition,"windup_ticks"),Number(definition,"active_ticks"),Number(definition,"recovery_ticks")};
            var factor=rangerTension?850:runesmithTwin?900:1000;
            var original=phases.Sum();var total=(original*factor+999)/1000;
            var scaled=phases.Select(value=>Math.Max(1,value*total/original)).ToArray();
            foreach(var index in Enumerable.Range(0,3).OrderByDescending(index=>phases[index]*total%original).ThenBy(index=>index))
                if(scaled.Sum()<total)scaled[index]++;
            return scaled;
        }

        private float V5Movement(Player player)=>player.Charging?0.7f:player.MeleeAttack!=null?Number(Kit(player.Loadout)["melee"],"move_milli_factor",1000)/1000f:1;

        private void TickV5Melee(Player player)
        {
            var attack=player.MeleeAttack;
            if(attack!=null&&WorldTick>=attack.EndTick){player.MeleeAttack=null;attack=null;}
            if(V8&&V8Spinning(player))return;
            if(attack==null&&player.ChargeRequested&&player.Input.Charge&&now>=player.MeleeReady)StartV5Charge(player);
            if(attack==null&&!player.Charging&&player.MeleeEquipped&&!player.ReleaseRequired&&player.Input.Melee&&now>=player.MeleeReady)
            {
                BeginV5Melee(player,false);attack=player.MeleeAttack;
            }
            if(attack==null)return;
            if(V6&&attack.Heavy&&!attack.Frozen){attack.PreviewValid=true;attack.PreviewImpact=V7?V7SlamImpact(player):Move(Layout(player.Room),player.Position,Aim(player.Input.Aim)*Distance(Kit(player.Loadout)["melee"]["charge"],"landing_distance_milli_tiles"),0.1f);}
            if(V7)attack.Direction=Aim(player.Input.Aim);
            if(WorldTick<attack.ActiveTick)return;
            if(WorldTick>=attack.RecoveryTick){attack.Phase="recover";return;}
            if(!attack.Frozen)
            {
                attack.Frozen=true;attack.Direction=Aim(player.Input.Aim);attack.Frost=Has(player,"frost");attack.Chain=Has(player,"runesmith_chain");
                var definition=Kit(player.Loadout)["melee"];
                attack.Arc=Number(definition,"arc_deg")+(Has(player,"ranger_fan")?30:0);
                attack.Range=V7?V7MeleeRange(player,false):Distance(definition,"range_milli_tiles")+(Has(player,"ranger_pierce")?0.35f:0);
                if(!attack.Heavy)attack.Damage=Math.Max(1,Number(definition,"damage")*(Has(player,"ranger_fan")?800:1000)/1000);
                if(attack.Heavy)
                {
                    attack.Range=V7?V7MeleeRange(player,true):Distance(definition["charge"],"wave_radius_milli_tiles")+(Has(player,"runesmith_heavy")?0.35f:0);
                    attack.Impact=V7?V7SlamImpact(player):Move(Layout(player.Room),player.Position,attack.Direction*Distance(definition["charge"],"landing_distance_milli_tiles"),0.1f);attack.ImpactValid=true;
                    player.HeavyCount++;
                }
                else player.LightCount++;
                if(V8){attack.Damage=V8Damage(player,attack.Damage);if(attack.Heavy)V8Impact(player,attack);}
                Effect(attack.Heavy?"heavy":"light",player.ID,player.Room,attack.Heavy?attack.Impact:player.Position,attack.Damage);
                var presentation=effects.Last();presentation["visual_radius"]=attack.Range;presentation["visual_aim_x"]=attack.Direction.x;presentation["visual_aim_y"]=attack.Direction.y;
            }
            attack.Phase="active";
            if(V7)V7MeleeAngles(player,attack);
            var count=Math.Max(1,attack.RecoveryTick-attack.ActiveTick);
            attack.SweepStart=-attack.Arc/2+attack.Arc*(WorldTick-attack.ActiveTick)/count;
            attack.SweepEnd=-attack.Arc/2+attack.Arc*(WorldTick-attack.ActiveTick+1)/count;
            if(V6&&!v6ResolvingMelee)return;
            foreach(var enemy in enemies.Where(item=>item.Health>0&&item.Room==player.Room).ToArray())
            {
                var hit=V7?(attack.Heavy?DcV7Geometry.Distance(attack.Impact-enemy.Position,Body(enemy).Vertices)<=attack.Range&&ClearLine(Layout(enemy.Room),attack.Impact,Center(enemy)):V7MeleeContact(player,attack,enemy.TickStart,enemy.Position,Body(enemy).Vertices).HasValue&&ClearLine(Layout(enemy.Room),Center(player),Center(enemy))):attack.Heavy?Vector2.Distance(enemy.Position,attack.Impact)<=attack.Range+Radius(enemy)&&ClearLine(Layout(enemy.Room),attack.Impact,enemy.Position):V6?V6SweepContact(player,attack,enemy.TickStart,enemy.Position,Radius(enemy)).HasValue&&ClearLine(Layout(enemy.Room),player.Position,enemy.Position):V5SweepContains(player,attack,enemy.Position,Radius(enemy));
                if(!hit||!attack.HitTargets.Add(enemy.ID))continue;
                var damage=attack.Damage*(attack.Heavy&&(V8?V8Direct(attack,enemy):V7?DcV7Geometry.Distance(attack.Impact-enemy.Position,Body(enemy).Vertices)<=.625f:Vector2.Distance(enemy.Position,attack.Impact)<=0.5f+Radius(enemy))?2:1);
                var before=enemy.Health;
                Hit(enemy,player.ID,attack.Root,damage,attack.Direction,false,player.LifeGeneration,WorldTick,attack.Root,attack.Frost);
                if(V6&&before!=enemy.Health)Effect(player.Loadout=="ranger"?"blade_hit":"hammer_hit",player.ID,enemy.Room,enemy.Position);
                if(before!=enemy.Health&&attack.Chain&&v5ChainRoots.Add(attack.Root))
                {
                    var next=enemies.Where(item=>item.Health>0&&item.Room==enemy.Room&&!attack.HitTargets.Contains(item.ID)&&!V5PrimaryContains(player,attack,item)&&Vector2.Distance(item.Position,enemy.Position)<=3&&ClearLine(Layout(item.Room),enemy.Position,item.Position)).OrderBy(item=>Vector2.Distance(item.Position,enemy.Position)).ThenBy(item=>item.ID,StringComparer.Ordinal).FirstOrDefault();
                    if(next!=null){attack.HitTargets.Add(next.ID);Hit(next,player.ID,attack.Root,damage*700/1000,attack.Direction,false,player.LifeGeneration,WorldTick,attack.Root,attack.Frost);}
                }
            }
            foreach(var cover in covers.Where(item=>item.Room==player.Room&&CoverSolid(item)).ToArray())
                if((attack.Heavy?Vector2.Distance(cover.Position,attack.Impact)<=attack.Range&&ClearV5CoverLine(player.Room,attack.Impact,cover):V7?V7MeleeContact(player,attack,cover.Position,cover.Position,DcV7Geometry.Rect(-.4375f,-.4375f,.875f,.875f)).HasValue&&ClearV5CoverLine(player.Room,Center(player),cover):V6?V6SweepContact(player,attack,cover.Position,cover.Position,0.45f).HasValue:InArc(player.Position,cover.Position,Rotate(attack.Direction,(attack.SweepStart+attack.SweepEnd)/2),attack.Range+0.45f,attack.SweepEnd-attack.SweepStart+12)&&ClearV5CoverLine(player.Room,player.Position,cover))&&attack.HitCovers.Add(cover.ID))DamageCover(cover,attack.Damage);
        }

        private bool ClearV5CoverLine(int room,Vector2 start,Cover target)
        {
            if(V7){if(DcV6Geometry.Terrain(Layout(room),start,target.Position,.035f,true).HasValue)return false;return !V6Covers(room,start,target.Position,.035f).Any(c=>c.ID!=target.ID&&CoverSolid(c)&&DcV6Geometry.Box(start,target.Position,c.Position.x-.4375f,c.Position.y-.4375f,.875f,.875f,.035f,true).HasValue);}
            if(!RoomGeometry.ClearLine(Layout(room),start,target.Position))return false;
            var steps=Math.Max(1,Mathf.CeilToInt(Vector2.Distance(start,target.Position)/0.1f));
            for(var index=0;index<=steps;index++){var point=Vector2.Lerp(start,target.Position,(float)index/steps);if(covers.Any(item=>item.Room==room&&item.ID!=target.ID&&CoverSolid(item)&&Vector2.Distance(point,item.Position)<0.5f))return false;}
            return true;
        }

        private bool V5SweepContains(Player player,MeleeV5 attack,Vector2 point,float radius)
        {
            if(V6)return V6SweepContact(player,attack,point,point,radius).HasValue&&ClearLine(Layout(player.Room),player.Position,point);
            var center=(attack.SweepStart+attack.SweepEnd)/2;
            return InArc(player.Position,point,Rotate(attack.Direction,center),attack.Range+radius,attack.SweepEnd-attack.SweepStart+12)&&ClearLine(Layout(player.Room),player.Position,point);
        }

        private bool V5PrimaryContains(Player player,MeleeV5 attack,Enemy enemy)=>attack.Heavy?Vector2.Distance(enemy.Position,attack.Impact)<=attack.Range+Radius(enemy)&&ClearLine(Layout(enemy.Room),attack.Impact,enemy.Position):InArc(player.Position,enemy.Position,attack.Direction,attack.Range+Radius(enemy),attack.Arc)&&ClearLine(Layout(enemy.Room),player.Position,enemy.Position);

        private void EmitV5(Player player,string root,Vector2 position,Vector2 direction,int damage,bool rune,int extra,long sourceLife=0,int room=-1)
        {
            var ranged=Kit(player.Loadout)["ranged"];
            SpawnProjectile(room<0?player.Room:room,position,direction,player.ID,root,damage,Distance(ranged,"projectile_speed_milli_tiles_per_second"),0,false,extra,sourceLife);
            var projectile=projectiles.Last();projectile.Root=root;projectile.Action=projectile.ID;
            projectile.RuneBolt=rune;projectile.Bounces=rune?1:0;projectile.CollisionRadius=Distance(ranged,"projectile_radius_milli_tiles");projectile.FrostEnabled=Has(player,"frost");if(V7){projectile.BornGameMS=now;projectile.ExpiresGameMS=rune?now+6000:0;projectile.Episode=root.StartsWith("shot_",StringComparison.Ordinal)?(player.AttackEpisode??player.LastAttackEpisode):v7ExecutingAction;projectile.EmissionIndex=player.EmissionIndex;projectile.Bounces=rune?int.MaxValue:0;}if(V6){projectile.MuzzlePending=true;projectile.MuzzleOrigin=position;RegisterV6Flight(projectile);}
        }

        private bool FireV5(Player player)
        {
            var ranged=Kit(player.Loadout)["ranged"];var fan=Has(player,"ranger_fan");var angles=fan?new[]{-12f,0f,12f}:new[]{0f};
            if(ProjectileLoad+angles.Length>V5ProjectileCapacity){LastActionReason="capacity";return false;}
            if(V7)player.EmissionIndex++;
            var root=NextID("shot");var damage=V8?V8Damage(player,Number(ranged,"damage"),fan?650:1000):Math.Max(1,Number(ranged,"damage")*(fan?650:1000)/1000);
            foreach(var angle in angles)EmitV5(player,root,Center(player),Rotate(Aim(player.Input.Aim),angle),damage,player.Loadout=="runesmith",Number(ranged,"initial_extra_targets")+(Has(player,"ranger_pierce")?1:0));
            if(MagazineSize(player)>0)player.Magazine--;
            Effect("shot",player.ID,player.Room,player.Position);return true;
        }

        private bool CastV5Volley(Player player,JToken definition)
        {
            var fan=Has(player,"ranger_fan");var count=fan?9:3;
            if(ProjectileLoad+count>V5ProjectileCapacity){LastActionReason="capacity";return false;}
            if(V7)player.EmissionIndex++;
            if(V8)V8Battle(player);
            var root=NextID("volley");var ranged=Kit(player.Loadout)["ranged"];
            var damage=V8?V8Damage(player,Number(ranged,"damage"),Number(definition,"base_damage_milli_factor")*(fan?650:1000)/1000):Math.Max(1,Number(ranged,"damage")*Number(definition,"base_damage_milli_factor")*(fan?650:1000)/1000000);
            foreach(var angle in (JArray)definition["angles_deg"])foreach(var offset in fan?new[]{-12f,0f,12f}:new[]{0f})
                EmitV5(player,root,Center(player),Rotate(Aim(player.Input.Aim),(float)angle+offset),damage,false,Number(ranged,"initial_extra_targets")+(Has(player,"ranger_pierce")?1:0));
            if(now>=player.DodgeUntil)
            {
                player.DodgeDirection=player.Input.Move.sqrMagnitude>0.01f?player.Input.Move.normalized:player.Direction.sqrMagnitude>0.01f?player.Direction:Aim(player.Input.Aim);
                player.DodgeUntil=now+Number(definition,"dash_duration_ms");player.DodgeSpeed=Distance(definition,"dash_distance_milli_tiles")*1000/Number(definition,"dash_duration_ms");player.InvulnerableUntil=Math.Max(player.InvulnerableUntil,now+Number(definition,"dash_invulnerable_ms"));player.MeleeAttack=null;if(V6){player.DashSource="gust";player.GustUntil=now+Number(definition,"dash_invulnerable_ms");}
                Effect("gust_dash",player.ID,player.Room,player.Position);
            }
            if(Has(player,"ward"))GrantWeaponShield(player,15,3000);
            Effect("ability",player.ID,player.Room,player.Position);return true;
        }

        private void TickV5Turret(Turret turret,Player owner)
        {
            if(turret.AttackAt>combatNow||ProjectileLoad>=V5ProjectileCapacity)return;
            var target=enemies.Where(item=>item.Health>0&&item.Room==turret.Room&&Vector2.Distance(item.Position,turret.Position)<=12&&ClearLine(Layout(turret.Room),turret.Position,item.Position)).OrderBy(item=>Vector2.Distance(item.Position,turret.Position)).ThenBy(item=>item.ID,StringComparer.Ordinal).FirstOrDefault();
            if(target==null)return;
            var damage=V8?V8Damage(owner,turret.BaseDamage,Has(owner,"runesmith_heavy")?1500:1000):turret.BaseDamage*(Has(owner,"runesmith_heavy")?1500:1000)/1000;
            EmitV5(owner,NextID("turret_shot"),turret.Position,(target.Position-turret.Position).normalized,damage,true,0,turret.SourceLife,turret.Room);
            turret.AttackAt=combatNow+Number(Kit(owner.Loadout)["primary"],"interval_ms");Effect("turret_shot",owner.ID,turret.Room,turret.Position);
        }

        private void QueueV5Wave(Room room)
        {
            room.GroupIDs.Clear();room.GroupUntil=combatNow+3000;
            if(room.Index==5){if(room.Wave++==0)room.Pending.Enqueue("boss");return;}
            var branch=room.Index>=6;
            if(branch&&room.Wave++>0)return;
            var waves=(JArray)config["rooms"][room.Index]["waves"];
            if(!branch&&room.Wave>=waves.Count)return;
            var count=((branch?P4Number("branch_enemy_count",6):Number(waves[room.Wave++],"count"))*countScale+999)/1000;
            var ranged=(count+2)/3;var melee=count-ranged;
            var denominator=room.Index==4?3:Convert.ToInt32(Rank("mix_"+room.Index+"_"+room.Wave,"weights").Substring(0,4),16)%2==0?6:3;
            var chargers=branch?0:(melee+denominator-1)/denominator;
            var kinds=Enumerable.Repeat("archer",ranged).Concat(Enumerable.Repeat("charger",chargers)).Concat(Enumerable.Repeat("guard",melee-chargers)).ToArray();
            foreach(var item in kinds.Select((kind,index)=>new{Kind=kind,Rank=Rank("wave_"+room.Index+"_"+room.Wave,index.ToString())}).OrderBy(item=>item.Rank,StringComparer.Ordinal))room.Pending.Enqueue(item.Kind);
            if(room.Index==4&&!room.EliteQueued&&room.Wave==2){room.Pending.Enqueue("elite");room.EliteQueued=true;}
        }
    }
}
