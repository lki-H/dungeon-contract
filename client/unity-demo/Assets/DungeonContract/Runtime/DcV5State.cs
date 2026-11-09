using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private void V5Snapshot(JObject snapshot,bool cachedFlights=false)
        {
            snapshot["v5_stage"]=true;snapshot["schema_version"]=5;
            var projectileIndex=0;
            var presentation=DcV6Profile.P5((string)manifest["config"]?["definition_id"]);
            foreach(var item in snapshot["projectiles"])
            {
                var projectile=projectiles[projectileIndex++];
                if(cachedFlights&&projectile.RuntimeSnapshotBound)continue;
                var record=(JObject)item;Put(record,"network_group",projectile.NetworkGroup);Put(record,"root",projectile.Root);Put(record,"source_life",projectile.SourceLife);Put(record,"rune_bolt",projectile.RuneBolt);Put(record,"wind_volley",presentation&&projectile.Root?.StartsWith("volley_",StringComparison.Ordinal)==true);
            }
            foreach(var item in snapshot["players"])
            {
                var player=players[(string)item["player_id"]];var attack=player.MeleeAttack;
                item["max_health"]=MaxHealth(player);
                item["attack_episode_id"]=player.AttackEpisode;item["charge_episode_id"]=player.ChargeAction;
                item["melee_action"]=attack==null?JValue.CreateNull():new JObject{["root"]=attack.Root,["phase"]=attack.Phase,["heavy"]=attack.Heavy,["started_tick"]=attack.StartedTick,["active_tick"]=attack.ActiveTick,["recovery_tick"]=attack.RecoveryTick,["end_tick"]=attack.EndTick,["aim_x"]=attack.Direction.x,["aim_y"]=attack.Direction.y,["damage"]=attack.Damage,["range"]=attack.Range,["arc"]=attack.Arc,["sweep_start"]=attack.SweepStart,["sweep_end"]=attack.SweepEnd,["impact_x"]=attack.Impact.x,["impact_y"]=attack.Impact.y};
            }
            snapshot["spawn_reservations"]=new JArray(spawnReservations.Where(item=>item.Begin<=combatNow).Select(item=>new JObject{["id"]=item.ID,["room_id"]=item.Room,["kind"]=item.Kind,["x"]=item.Position.x,["y"]=item.Position.y,["remaining_ms"]=Math.Max(0,item.Until-combatNow),["revision"]=item.Revision,["batch_id"]=item.Batch,["summon"]=item.Summon}));
            foreach(var item in snapshot["enemies"])
            {
                var enemy=enemies.First(value=>value.ID==(string)item["id"]);var state=enemy.V5State;
                item["summon"]=enemy.Summon;
                item["visual_phase"]=enemy.WarningUntil>combatNow?"prepare":enemy.ChargeUntil>combatNow||state?.Emitter!=null||state?.BurstRemaining>0||combatNow-enemy.PresentationAttackAt<180?"attack":state?.RecoverUntil>combatNow||combatNow-enemy.PresentationAttackAt<500?"recover":"idle";
                if(state==null){if(V6)foreach(var name in new[]{"phase_pending","barrage_active","barrage_pulse","reserved_shots","barrage_remaining_ms","summon_pending","summon_live","summon_reserved"})((JObject)item).Remove(name);continue;}
                item["phase"]=state.Phase;item["phase_pending"]=state.PhasePending&&state.Phase==1;
                item["barrage_active"]=state.Emitter!=null;item["barrage_pulse"]=state.Pulse;item["reserved_shots"]=enemy.ReservedShots;item["barrage_remaining_ms"]=Math.Max(0,state.EmitterEnd-combatNow);
                item["summon_pending"]=state.SummonPending;item["summon_live"]=enemies.Count(value=>value.Health>0&&value.Summon);item["summon_reserved"]=spawnReservations.Count(value=>value.Summon&&value.Recovering==null);
            }
        }
    }
}
