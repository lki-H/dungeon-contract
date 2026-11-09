using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private sealed class EnemyV5
        {
            public int Phase=1;
            public bool PhasePending;
            public string Emitter;
            public long EmitterStart;
            public int Pulse;
            public long NextPulse;
            public long EmitterEnd;
            public int DashRequests;
            public long RecoverUntil;
            public long LastSummon;
            public bool SummonPending;
            public bool MeleeAfterDash;
            public int BurstRemaining;
            public long BurstNext;
            public long MeleeEndTick;
        }
        private sealed class SummonBatchV5
        {
            public string ID;
            public string Boss;
            public long Started;
            public long BlockedAt;
            public int Recoveries;
            public long RetryAt;
        }
        private SummonBatchV5 v5Batch;

        private void EmitV5Enemy(Enemy enemy,string root,Vector2 origin,Vector2 direction,int damage,float speed)
        {
            enemy.PresentationAttackAt=combatNow;
            SpawnProjectile(enemy.Room,origin,direction,enemy.ID,root,damage,speed,0,true,0);
            var projectile=projectiles.Last();projectile.Root=root;projectile.Action=projectile.ID;if(V6)RegisterV6Flight(projectile);
        }

        private void TickV5Enemy(Enemy enemy)
        {
            if(enemy.ReadyAt==long.MaxValue)return;
            enemy.V5State??=new EnemyV5();
            if(V6&&(enemy.SuppressedUntil>now||enemy.WarningUntil>0||enemy.V5State.BurstRemaining>0||enemy.V5State.Emitter!=null||enemy.V5State.RecoverUntil>combatNow||enemy.ChargeUntil>combatNow)){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;}
            if(enemy.Kind=="boss")TickV5Boss(enemy);else TickV5Archer(enemy);
        }

        private void V5Walk(Enemy enemy,Player target,float speed,float keep)
        {
            var distance=Vector2.Distance(enemy.Position,target.Position);
            var direction=distance>keep||!ClearLine(Layout(enemy.Room),enemy.Position,target.Position)?EnemyPath(enemy,target.Position):distance<keep-1?(enemy.Position-target.Position).normalized:Vector2.zero;
            direction=SeparateEnemy(enemy,direction);
            var before=enemy.Position;
            var step=direction*speed*(enemy.SlowUntil>now?enemy.SlowFactor:1)/30;
            enemy.Position=V7?WalkV7Enemy(enemy,step):Move(Layout(enemy.Room),before,step,Radius(enemy),point=>WeaponWindBlocks(enemy,point)||EnemyOverlap(enemy,point));
            if(Vector2.Distance(enemy.Position,enemy.StuckPosition)>0.3f){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;}
            if(direction.sqrMagnitude<0.01f){enemy.StuckAt=combatNow;return;}
            if(combatNow-enemy.StuckAt<5000||enemy.WarningUntil>0||enemy.V5State.Emitter!=null||enemy.ChargeUntil>combatNow)return;
            if(enemy.RecoveryCount++>=2){AbortTechnical();return;}
            var point=V7?V7SpawnPoint(enemy.Room,enemy.ID+":recovery",enemy.Kind,enemy.ID):SpawnPoint(enemy.Room,enemy.ID+":recovery");
            if(point.HasValue){ReserveSpawn(enemy.Room,enemy.Kind,point.Value,enemy.Summon,enemy.ID);enemy.ReadyAt=long.MaxValue;}
            enemy.StuckAt=combatNow;
        }

        private void TickV5Archer(Enemy enemy)
        {
            if(now<enemy.SuppressedUntil){enemy.ReservedShots=0;enemy.WarningUntil=0;enemy.Warning=null;enemy.V5State.BurstRemaining=0;return;}
            var target=P21EnemyTarget(enemy);var definition=enemyDefinitions[enemy.Kind];var state=enemy.V5State;
            if(target==null){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;enemy.ReservedShots=0;enemy.WarningUntil=0;state.BurstRemaining=0;return;}
            if(state.BurstRemaining>0)
            {
                if(combatNow<state.BurstNext)return;
                enemy.ReservedShots--;EmitV5Enemy(enemy,enemy.AttackAction,enemy.Origin,enemy.Aim,Number(definition,"damage"),Distance(definition,"projectile_speed_milli_tiles_per_second"));
                state.BurstRemaining--;state.BurstNext+=Number(definition,"burst_interval_ms");
                if(state.BurstRemaining==0)state.RecoverUntil=combatNow+Number(definition,"recovery_ms");
                return;
            }
            if(enemy.WarningUntil>0)
            {
                if(combatNow<enemy.WarningUntil){PrepareEnemyMotion(enemy,target);return;}
                if(enemy.Warning=="fan_three")
                {
                    foreach(var angle in (JArray)definition["fan_angles_deg"]){enemy.ReservedShots--;EmitV5Enemy(enemy,enemy.AttackAction,enemy.Origin,Rotate(enemy.Aim,(float)angle),Number(definition,"damage"),Distance(definition,"projectile_speed_milli_tiles_per_second"));}
                    state.RecoverUntil=combatNow+Number(definition,"recovery_ms");
                }
                else{state.BurstRemaining=3;state.BurstNext=combatNow;}
                enemy.WarningUntil=0;enemy.Warning=null;return;
            }
            if(combatNow<state.RecoverUntil)return;
            V5Walk(enemy,target,Distance(definition,"move_milli_tiles_per_second"),5);
            if(combatNow<enemy.AttackAt||combatNow<enemy.ReadyAt||Vector2.Distance(enemy.Position,target.Position)>8||!ClearLine(Layout(enemy.Room),enemy.Position,target.Position)||ProjectileLoad+3>V5ProjectileCapacity)return;
            enemy.ReservedShots=3;enemy.Warning=enemy.Pattern++%2==0?"fan_three":"burst_three";enemy.WarningUntil=combatNow+Number(definition,"telegraph_ms");enemy.AttackAt=combatNow+Number(definition,"attack_interval_ms");enemy.AttackAction=NextID("archer_attack");enemy.Origin=enemy.Position;enemy.Aim=(Center(target)-Center(enemy)).normalized;
            Effect("warning",enemy.ID,enemy.Room,enemy.Position);
        }

        private JToken V5BossPhase(Enemy enemy)=>enemyDefinitions["boss"]["phases"][enemy.V5State.Phase-1];

        private void TickV5Emitter(Enemy enemy)
        {
            var state=enemy.V5State;
            if(state.Emitter==null)return;
            var phase=V5BossPhase(enemy);var pulses=Number(phase,"barrage_duration_ms")/Number(phase,"barrage_interval_ms");
            var emitted=0;
            while(state.Pulse<pulses&&combatNow>=state.NextPulse&&emitted++<4)
            {
                var count=Number(phase,"barrage_projectiles_per_pulse");
                var initial=Convert.ToUInt32(Rank("boss_barrage",state.Emitter).Substring(0,8),16)%360;
                for(var index=0;index<count;index++)
                {
                    enemy.ReservedShots--;var angle=initial+state.Pulse*15-150+300f*index/(count-1);
                    EmitV5Enemy(enemy,state.Emitter,enemy.Position,Rotate(Vector2.right,angle),Number(phase,"projectile_damage"),Distance(phase,"projectile_speed_milli_tiles_per_second"));
                }
                state.Pulse++;state.NextPulse=state.EmitterStart+(long)state.Pulse*Number(phase,"barrage_interval_ms");
                Effect("barrage_pulse",enemy.ID,enemy.Room,enemy.Position);
            }
            if(state.Pulse==pulses&&combatNow>=state.EmitterEnd){state.Emitter=null;state.RecoverUntil=Math.Max(state.RecoverUntil,combatNow+500);}
        }

        private void PrepareV5Boss(Enemy enemy,Player target,float speed)
        {
            if(enemy.WarningUntil-combatNow<=200)return;
            var direction=(Center(target)-Center(enemy)).normalized;
            var side=Convert.ToInt32(Rank("boss_feint",enemy.ID).Substring(0,4),16)%2==0?1:-1;
            enemy.Position=Move(Layout(enemy.Room),enemy.Position,new Vector2(-direction.y,direction.x)*side*speed*0.45f/30,Radius(enemy),point=>EnemyOverlap(enemy,point));
            enemy.Origin=enemy.Position;enemy.Aim=(Center(target)-Center(enemy)).normalized;
        }

        private void TickV5Boss(Enemy enemy)
        {
            var state=enemy.V5State;var definition=enemyDefinitions["boss"];
            if(enemy.Health*100<=enemy.Maximum*Number(definition,"phase_2_threshold_percent"))state.PhasePending=true;
            TickV5Emitter(enemy);
            var target=P21EnemyTarget(enemy);
            if(target==null){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;return;}
            var phase=V5BossPhase(enemy);
            if(state.MeleeEndTick>0)
            {
                if(WorldTick<state.MeleeEndTick){V5BossMelee(enemy);return;}
                state.MeleeEndTick=0;state.RecoverUntil=Math.Max(state.RecoverUntil,combatNow+Number(definition["melee"],"recovery_ms"));
            }
            if(enemy.ChargeUntil>0)
            {
                if(combatNow<enemy.ChargeUntil)
                {
                    var before=enemy.Position;
                    enemy.Position=Move(Layout(enemy.Room),before,enemy.Aim*Distance(phase,"dash_speed_milli_tiles_per_second")/30,Radius(enemy),point=>EnemyOverlap(enemy,point));
                    foreach(var player in players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==enemy.Room))if(V7?V7ChargeStrike(enemy,player):Vector2.Distance(player.Position,enemy.Position)<=Radius(enemy)+PlayerHurtRadius)Hurt(player,Number(definition["dash"],"damage"),enemy.AttackAction);
                    if(Vector2.Distance(before,enemy.Position)>0.001f)return;
                }
                enemy.ChargeUntil=0;state.RecoverUntil=combatNow+Number(definition["dash"],"recovery_ms");state.MeleeAfterDash=true;
            }
            if(enemy.WarningUntil>0)
            {
                if(combatNow<enemy.WarningUntil){PrepareV5Boss(enemy,target,Distance(phase,"move_milli_tiles_per_second"));return;}
                var kind=enemy.Warning;enemy.Warning=null;enemy.WarningUntil=0;
                if(kind=="barrage")
                {
                    state.Emitter=enemy.AttackAction;state.EmitterStart=combatNow;state.NextPulse=combatNow;state.Pulse=0;state.DashRequests=0;state.EmitterEnd=combatNow+Number(phase,"barrage_duration_ms");TickV5Emitter(enemy);
                }
                else if(kind=="charge")enemy.ChargeUntil=combatNow+Number(definition["dash"],"duration_ms");
                else
                {
                    state.MeleeEndTick=WorldTick+Number(definition["melee"],"active_ticks");V5BossMelee(enemy);
                }
                return;
            }
            var firstDue=rooms[enemy.Room].WarningAt+10000;
            if(combatNow>=firstDue&&(state.LastSummon==0||combatNow-state.LastSummon>=20000))state.SummonPending=true;
            if(combatNow<state.RecoverUntil||combatNow<enemy.ReadyAt)return;
            if(state.MeleeAfterDash)
            {
                state.MeleeAfterDash=false;
                if(Vector2.Distance(enemy.Position,target.Position)<=Distance(definition["melee"],"range_milli_tiles")){V5BossWarning(enemy,target,"melee",Number(definition["melee"],"telegraph_ms"));return;}
            }
            if(state.Emitter!=null)
            {
                var requests=Number(phase,"dashes_per_barrage");
                if(state.DashRequests<requests&&combatNow>=state.EmitterStart+(long)Number(phase,"barrage_duration_ms")*(state.DashRequests+1)/(requests+1))
                {
                    state.DashRequests++;V5BossWarning(enemy,target,"charge",Number(definition["dash"],"telegraph_ms"));return;
                }
                V5Walk(enemy,target,Distance(phase,"move_milli_tiles_per_second"),3);return;
            }
            if(state.PhasePending&&state.Phase==1){state.Phase=2;phase=V5BossPhase(enemy);Effect("boss_phase",enemy.ID,enemy.Room,enemy.Position);}
            if(v5Batch!=null&&combatNow<v5Batch.Started+1000)return;
            if(v5Batch==null&&state.SummonPending&&TryV5Summon(enemy)){state.RecoverUntil=combatNow+1000;return;}
            V5Walk(enemy,target,Distance(phase,"move_milli_tiles_per_second"),4);
            if(Vector2.Distance(enemy.Position,target.Position)>12||!ClearLine(Layout(enemy.Room),enemy.Position,target.Position))return;
            var shots=Number(phase,"barrage_duration_ms")/Number(phase,"barrage_interval_ms")*Number(phase,"barrage_projectiles_per_pulse");
            if(ProjectileLoad+shots>V5ProjectileCapacity)return;
            enemy.ReservedShots=shots;V5BossWarning(enemy,target,"barrage",Number(definition,"barrage_telegraph_ms"));
        }

        private void V5BossWarning(Enemy enemy,Player target,string kind,int warning)
        {
            enemy.Warning=kind;enemy.WarningUntil=combatNow+warning;enemy.Origin=enemy.Position;enemy.Aim=(Center(target)-Center(enemy)).normalized;enemy.AttackAction=NextID("boss_"+kind);enemy.TargetID=target.ID;
            Effect("warning",enemy.ID,enemy.Room,enemy.Position);
        }

        private void V5BossMelee(Enemy enemy)
        {
            enemy.PresentationAttackAt=combatNow;
            var melee=enemyDefinitions["boss"]["melee"];
            foreach(var player in players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==enemy.Room))if(V7?V7EnemyStrike(enemy,player,enemy.Origin,Distance(melee,"range_milli_tiles"),Number(melee,"arc_deg")):InArc(enemy.Origin,player.Position,enemy.Aim,Distance(melee,"range_milli_tiles")+PlayerHurtRadius,Number(melee,"arc_deg"))&&ClearLine(Layout(enemy.Room),enemy.Origin,player.Position))Hurt(player,Number(melee,"damage"),enemy.AttackAction);
            var origin=V7?enemy.Origin+Body(enemy).Center:enemy.Origin;foreach(var cover in covers.Where(item=>item.Room==enemy.Room&&CoverSolid(item)&&InArc(origin,item.Position,enemy.Aim,Distance(melee,"range_milli_tiles")+0.45f,Number(melee,"arc_deg"))))if(enemy.V5State.MeleeEndTick==WorldTick+Number(melee,"active_ticks")&&ClearV5CoverLine(enemy.Room,origin,cover))DamageCover(cover,Number(melee,"damage"));
        }

        private bool TryV5Summon(Enemy boss)
        {
            var summons=enemies.Count(item=>item.Health>0&&item.Summon)+spawnReservations.Count(item=>item.Summon&&item.Recovering==null);
            var roomCount=enemies.Count(item=>item.Health>0&&item.Room==boss.Room)+spawnReservations.Count(item=>item.Room==boss.Room&&item.Recovering==null);
            if(summons+6>12||roomCount+6>13||EnemyLoad+6>24)return false;
            var batch=new SummonBatchV5{ID=NextID("summon_batch"),Boss=boss.ID,Started=combatNow};
            foreach(var kind in new[]{"archer","archer","guard","guard","charger","elite"})
            {
                var point=V7?V7SpawnPoint(boss.Room,batch.ID+":"+kind+":"+spawnReservations.Count,kind):SpawnPoint(boss.Room,batch.ID+":"+kind+":"+spawnReservations.Count);
                if(!point.HasValue){spawnReservations.RemoveAll(item=>item.Batch==batch.ID);return false;}
                spawnReservations.Add(new SpawnReservation{ID=NextID("summon_warning"),Room=boss.Room,Kind=kind,Position=point.Value,Begin=combatNow+200,Until=combatNow+1000,Summon=true,Batch=batch.ID});
            }
            v5Batch=batch;Effect("summon",boss.ID,boss.Room,boss.Position);return true;
        }

        private void TickV5SummonBatch()
        {
            if(v5Batch==null)return;
            var batch=v5Batch;var boss=enemies.FirstOrDefault(item=>item.ID==batch.Boss&&item.Health>0);
            if(boss==null){spawnReservations.RemoveAll(item=>item.Batch==batch.ID);v5Batch=null;return;}
            if(combatNow<batch.RetryAt)return;
            var members=spawnReservations.Where(item=>item.Batch==batch.ID).ToArray();
            foreach(var reservation in members)
            {
                if(SpawnLegal(reservation.Room,reservation.Position,null,reservation.ID))continue;
                spawnReservations.Remove(reservation);var point=V7?V7SpawnPoint(reservation.Room,reservation.ID+":"+reservation.Revision,reservation.Kind):SpawnPoint(reservation.Room,reservation.ID+":"+reservation.Revision);spawnReservations.Add(reservation);
                if(point.HasValue){reservation.Position=point.Value;reservation.Begin=combatNow;reservation.Until=combatNow+800;reservation.Revision++;batch.BlockedAt=0;}
                else
                {
                    if(batch.BlockedAt==0)batch.BlockedAt=combatNow;
                    if(combatNow-batch.BlockedAt>=5000){if(batch.Recoveries++>=2){AbortTechnical();return;}batch.BlockedAt=combatNow;}
                    batch.RetryAt=combatNow+250;return;
                }
            }
            if(members.Any(item=>combatNow<item.Until))return;
            foreach(var reservation in members){spawnReservations.Remove(reservation);SpawnEnemy(reservation.Room,reservation.Kind,reservation.Position,true);}
            boss.V5State.LastSummon=combatNow;boss.V5State.SummonPending=false;boss.Summoned+=6;v5Batch=null;
        }
    }
}
