using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        public void Acknowledge(JObject receipt)
        {
            if(P3){P3Acknowledge(receipt);return;}
            if (receipt["task_requirements"] is JObject updated) { requirements.Clear(); foreach (var property in updated.Properties()) requirements[property.Name] = (JObject)property.Value.DeepClone(); }
            reinforcementRemaining = Number(config["operation"], "reinforcements");
            if (!(receipt["reinforcements"] is JObject actions)) return;
            foreach (var property in actions.Properties())
            {
                var action = property.Value; if (!players.TryGetValue((string)action["player_id"], out var player)) continue; var status = (string)action["status"];
                if (status == "pending_spawn" || status == "spawned") reinforcementRemaining--;
                if (status == "pending_spawn" && player.Reinforcement == null && player.Health <= 0 && !Terminal) { player.Reinforcement = property.Name; player.RespawnAt = now + Number(config["authority"], "spawn_delay_ms"); }
                if (status == "spawned" && player.Reinforcement == property.Name) { player.Reinforcement = null; player.SpawnAwaitingAck = false; }
            }
        }

        public JObject Summary() => new JObject
        {
            ["participants"] = new JArray(players.Values.OrderBy(player => player.Slot).Select(player => new JObject { ["player_id"] = player.ID, ["slot"] = player.Slot, ["loadout_id"] = player.Loadout, ["damage"] = player.Damage, ["kills"] = player.Kills, ["assists"] = player.Assists, ["interactions"] = player.Interactions, ["deaths"] = player.Deaths, ["knockdowns"]=player.Knockdowns,["rescues"]=player.Rescues,["cleared_projectiles"]=player.ClearedProjectiles,["protected_damage"]=player.ProtectedDamage,["control_effect_ms"]=player.ControlEffect,["choices"] = new JArray(player.Upgrades), ["effective_contribution"] = player.Damage > 0 || player.Interactions > 0 || player.Reaches > 0 || player.Rescues>0 || player.ProtectedDamage>0 || player.ControlEffect>0 || player.ClearedProjectiles>0 })),
            ["trial_eligible"] = (!P3||DcV6Profile.P5((string)manifest["config"]?["definition_id"]))&&(string)manifest["mode"] == "trial", ["ineligible_reason"] = "none"
        };

        public JObject TerminalRequest(long frame, long sequence) => new JObject { ["outcome"] = Outcome, ["reason"] = terminalReason, ["cutoff_frame"] = frame, ["cutoff_sequence"] = sequence };

        public JObject Snapshot() { var snapshot=new JObject
        {
            ["schema_version"] = 3, ["run_id"] = manifest["run_id"], ["world_frame_id"] = WorldTick, ["elapsed_ms"] = now, ["room"] = MaximumRoom, ["safe_node"] = safeNode, ["safe_remaining_ms"] = safeNode < 0 ? 0 : Math.Max(0, safeUntil - now), ["reinforcement_remaining"] = reinforcementRemaining,
            ["completed"] = new JArray(rooms.Take(6).Select(room => room.Status == "cleared")),
            ["rooms"] = new JArray(rooms.Select(room => new JObject { ["index"] = room.Index, ["id"] = Layout(room.Index)["id"], ["connector"]=room.Connector, ["exists"] = room.Exists, ["status"] = room.Status, ["wave"] = room.Wave, ["pending"] = room.Pending.Count, ["mechanism_complete"] = room.Mechanism, ["warning_remaining_ms"] = Math.Max(0, room.WarningAt - combatNow), ["enemies_remaining"] = room.Pending.Count + spawnReservations.Count(item=>item.Room==room.Index&&item.Recovering==null)+enemies.Count(enemy => enemy.Room == room.Index && enemy.Health > 0) })),
            ["spawn_reservations"]=new JArray(spawnReservations.Select(item=>new JObject{["id"]=item.ID,["room_id"]=item.Room,["kind"]=item.Kind,["x"]=item.Position.x,["y"]=item.Position.y,["remaining_ms"]=Math.Max(0,item.Until-combatNow)})),
            ["projectile_load"]=ProjectileLoad,
            ["players"] = new JArray(players.Values.OrderBy(player => player.Slot).Select(player => new JObject
            {
                ["player_id"] = player.ID, ["slot"] = player.Slot, ["loadout_id"] = player.Loadout, ["room_id"] = player.Room, ["explored_rooms"]=new JArray(player.Explored.OrderBy(item=>item)), ["x"] = player.Position.x, ["y"] = player.Position.y, ["health"] = player.Health, ["active_weapon_id"]=WeaponID(player),["melee_equipped"]=player.MeleeEquipped,["weapon_revision"]=player.WeaponRevision,["release_required"]=player.ReleaseRequired,["last_action_sequence"]=player.LastActionSequence,["charge_action_id"]=player.ChargeAction,["switch_remaining_ms"]=Math.Max(0,player.SwitchAt-now),["ranged_ready_ms"]=player.RangedReady,["melee_ready_ms"]=player.MeleeReady,["hurt_protection_remaining_ms"]=Math.Max(0,player.HurtUntil-now),["dodge_speed"]=player.DodgeSpeed,["magazine"]=player.Magazine, ["magazine_max"]=MagazineSize(player), ["reload_remaining_ms"]=Math.Max(0,player.ReloadUntil-now), ["charging"]=player.Charging, ["charge_ms"]=player.Charging?now-player.ChargeAt:0, ["secondary_remaining_ms"]=Math.Max(0,player.SecondaryAt-now), ["life_state"]=player.Life, ["knockdowns"]=player.Knockdowns, ["light_attacks"]=player.LightCount, ["heavy_attacks"]=player.HeavyCount, ["shield"] = player.Shield, ["deaths"] = player.Deaths, ["damage"] = player.Damage, ["connected"] = player.Connected, ["left"] = player.Left,
                ["last_input_sequence"] = player.Ack, ["dodge_direction_x"] = player.DodgeDirection.x, ["dodge_direction_y"] = player.DodgeDirection.y, ["dodge_duration_remaining_ms"] = Math.Max(0, player.DodgeUntil - now), ["dodge_remaining_ms"] = Math.Max(0, player.DodgeAt - now), ["ability_remaining_ms"] = Math.Max(0, player.AbilityAt - now), ["protected_remaining_ms"] = Math.Max(0, player.ProtectedUntil - now),
                ["interaction_remaining_ms"] = player.InteractionTarget == null ? 0 : Math.Max(0, Number(config["player"], "interaction_ms") - (now - player.InteractionAt)), ["supply_used"] = player.Claims.Contains("supply_court"), ["respawn_remaining_ms"] = player.Reinforcement == null ? -1 : Math.Max(0, player.RespawnAt - now),
                ["upgrades"] = new JArray(player.Upgrades), ["candidates"] = new JArray(Candidates(player.ID)), ["choice"] = safeNode >= 0 && player.Choices.TryGetValue(safeNode, out var choice) ? choice : null
            })),
            ["enemies"] = new JArray(enemies.Where(enemy => enemy.Health > 0).Select(enemy => new JObject { ["id"] = enemy.ID, ["kind"] = enemy.Kind, ["room_id"] = enemy.Room, ["x"] = enemy.Position.x, ["y"] = enemy.Position.y, ["health"] = enemy.Health, ["maximum"] = enemy.Maximum, ["warning"] = enemy.WarningUntil > combatNow ? enemy.Warning : "", ["warning_remaining_ms"] = Math.Max(0, enemy.WarningUntil - combatNow), ["warning_x"] = enemy.Origin.x, ["warning_y"] = enemy.Origin.y, ["aim_x"] = enemy.Aim.x, ["aim_y"] = enemy.Aim.y, ["phase"] = enemy.Kind == "boss" && enemy.Health * 100 <= enemy.Maximum * 50 ? 2 : 1 })),
            ["projectiles"] = new JArray(projectiles.Select(projectile => new JObject { ["id"] = projectile.ID, ["room_id"] = projectile.Room, ["x"] = projectile.Position.x, ["y"] = projectile.Position.y, ["enemy"] = projectile.Enemy,["speed"]=projectile.Speed,["radius"]=projectile.CollisionRadius,["bounces"]=projectile.Bounces,["penetrations"]=projectile.Penetration,["remaining_range"]=projectile.Unbounded?JValue.CreateNull():new JValue(projectile.Remaining),["unbounded"]=projectile.Unbounded,["direction_x"]=projectile.Direction.x,["direction_y"]=projectile.Direction.y })),
            ["turrets"] = new JArray(turrets.Select(turret => new JObject { ["id"] = turret.ID, ["owner"] = turret.Owner, ["room_id"] = turret.Room, ["x"] = turret.Position.x, ["y"] = turret.Position.y,["damage"]=turret.BaseDamage })),
            ["covers"]=new JArray(covers.Select(item=>new JObject {["id"]=item.ID,["room_id"]=item.Room,["x"]=item.Position.x,["y"]=item.Position.y,["health"]=item.Health,["revision"]=item.Revision})),
            ["fields"]=new JArray(fields.Select(item=>new JObject {["id"]=item.ID,["kind"]=item.Kind,["room_id"]=item.Room,["x"]=item.Position.x,["y"]=item.Position.y,["radius"]=Distance(Kit(players[item.Owner].Loadout)["secondary"],"radius_milli_tiles")})),
            ["outcome"]=Outcome,["reason"]=terminalReason,["practice"]=Practice,
            ["traps"] = new JArray(traps.Select(trap => new JObject { ["id"] = trap.ID, ["room_id"] = trap.Room, ["x"] = trap.Position.x, ["y"] = trap.Position.y, ["phase"] = trap.Phase, ["remaining_ms"] = Math.Max(0, trap.Until - combatNow) })),
            ["effects"] = new JArray(effects.Where(effect => now - (long)effect["at"] <= 200).Select(effect => effect.DeepClone())), ["boss_warning_remaining_ms"] = rooms[5].Status == "warning" ? Math.Max(0, rooms[5].WarningAt - combatNow) : 0
        };P3Snapshot(snapshot);P4Snapshot(snapshot);if(V5)V5Snapshot(snapshot);if(V6)V6Snapshot(snapshot);return snapshot;}

        public JObject Capture(bool includeProjectiles=true,bool includeTerrain=true) => new JObject
        {
            ["players"] = JArray.FromObject(players.Values, serializer), ["rooms"] = JArray.FromObject(rooms, serializer), ["enemies"] = JArray.FromObject(enemies, serializer), ["projectiles"] = includeProjectiles?JArray.FromObject(projectiles, serializer):new JArray(), ["turrets"] = JArray.FromObject(turrets, serializer), ["traps"] = includeTerrain?JArray.FromObject(traps, serializer):new JArray(),
            ["covers"]=includeTerrain?JArray.FromObject(covers,serializer):new JArray(), ["fields"]=JArray.FromObject(fields,serializer), ["spawn_reservations"]=JArray.FromObject(spawnReservations,serializer),
            ["v7_physics_wait_until"]=v7PhysicsWaitUntil,["v7_contact_budget_parks"]=V7ContactBudgetParks,["v6_tick_epoch"]=v6TickEpoch,["now"] = now, ["combat_now"] = combatNow, ["last_tick_at"] = lastTickAt, ["tick"] = WorldTick, ["serial"] = serial, ["budget"] = reinforcementRemaining, ["safe_node"] = safeNode, ["safe_until"] = safeUntil, ["outcome"] = Outcome, ["reason"] = terminalReason,
            ["requirements"] = JObject.FromObject(requirements), ["defense"] = JObject.FromObject(defense), ["technical_waiting"]=technicalWaiting, ["damage_actions"] = new JArray(damageActions.OrderBy(item => item, StringComparer.Ordinal)), ["effects"] = new JArray(effects.Select(effect => effect.DeepClone()))
            ,["p3_samples"]=JObject.FromObject(p3Samples),["p3_defense"]=JObject.FromObject(p3Defense),["p3_effects"]=new JArray(p3Effects.OrderBy(item=>item,StringComparer.Ordinal))
            ,["action_results"]=JObject.FromObject(actionResults),["action_order"]=new JArray(actionOrder),["queued"]=JArray.FromObject(queued,serializer),["waiting_held"]=JObject.FromObject(waitingHeld,serializer),["foundation_inputs"]=JObject.FromObject(foundationInputs,serializer)
            ,["v5_chain_roots"]=new JArray(v5ChainRoots),["v5_batch"]=v5Batch==null?JValue.CreateNull():JObject.FromObject(v5Batch,serializer)
        };

        public void Restore(JObject checkpoint)
        {
            v7PhysicsWaitUntil=(long?)checkpoint["v7_physics_wait_until"]??0;V7ContactBudgetParks=(int?)checkpoint["v7_contact_budget_parks"]??0;v6TickEpoch=(long?)checkpoint["v6_tick_epoch"]??0;var serialFloor=serial;v6CoverGrid.Clear();v6CoverIDs.Clear();v6Flights.Clear();v6Lifecycle.Clear();if(!V6){pathLayouts.Clear();runtimeState=null;runtimeRecords.Clear();}
            v5ChainRoots.Clear();if(checkpoint["v5_chain_roots"] is JArray chainRoots)foreach(var root in chainRoots)v5ChainRoots.Add((string)root);
            v5Batch=checkpoint["v5_batch"] is JObject batch?batch.ToObject<SummonBatchV5>(serializer):null;
            actionResults.Clear();if(checkpoint["action_results"] is JObject results)foreach(var item in results.Properties())actionResults[item.Name]=(string)item.Value;
            actionOrder.Clear();if(checkpoint["action_order"] is JArray order)foreach(var item in order)actionOrder.Enqueue((string)item);
            queued.Clear();if(checkpoint["queued"] is JArray pending)foreach(var item in pending.ToObject<QueuedAction[]>(serializer))queued.Enqueue(item);
            waitingHeld.Clear();if(checkpoint["waiting_held"] is JObject held)foreach(var item in held.Properties())waitingHeld[item.Name]=item.Value.ToObject<DcP1Input>(serializer);
            foundationInputs.Clear();if(checkpoint["foundation_inputs"] is JObject inputState)foreach(var item in inputState.Properties())foundationInputs[item.Name]=item.Value.ToObject<DcP1Input>(serializer);
            p3Samples.Clear();if(checkpoint["p3_samples"] is JObject samples)foreach(var item in samples.Properties())p3Samples[item.Name]=(int)item.Value;
            p3Defense.Clear();if(checkpoint["p3_defense"] is JObject proofs)foreach(var item in proofs.Properties())p3Defense[item.Name]=(JObject)item.Value.DeepClone();
            p3Effects.Clear();if(checkpoint["p3_effects"] is JArray effectsSeen)foreach(var item in effectsSeen)p3Effects.Add((string)item);
            technicalWaiting=(bool?)checkpoint["technical_waiting"]??false;
            spawnReservations.Clear();if(checkpoint["spawn_reservations"] is JArray reservations)spawnReservations.AddRange(reservations.ToObject<SpawnReservation[]>(serializer));
            players.Clear(); foreach (var player in checkpoint["players"].ToObject<Player[]>(serializer)) players[player.ID] = player;
            rooms = checkpoint["rooms"].ToObject<Room[]>(serializer); enemies.Clear(); enemies.AddRange(checkpoint["enemies"].ToObject<Enemy[]>(serializer)); projectiles.Clear(); projectiles.AddRange(checkpoint["projectiles"].ToObject<Projectile[]>(serializer)); turrets.Clear(); turrets.AddRange(checkpoint["turrets"].ToObject<Turret[]>(serializer)); traps.Clear(); traps.AddRange(checkpoint["traps"].ToObject<Trap[]>(serializer));
            covers.Clear();covers.AddRange(checkpoint["covers"].ToObject<Cover[]>(serializer));fields.Clear();fields.AddRange(checkpoint["fields"].ToObject<Field[]>(serializer));
            now = (long)checkpoint["now"]; combatNow = (long)checkpoint["combat_now"]; lastTickAt = (long)checkpoint["last_tick_at"]; WorldTick = (long)checkpoint["tick"]; serial = V6?Math.Max(serialFloor,(long)checkpoint["serial"]):(long)checkpoint["serial"]; reinforcementRemaining = (int)checkpoint["budget"]; safeNode = (int)checkpoint["safe_node"]; safeUntil = (long)checkpoint["safe_until"]; Outcome = (string)checkpoint["outcome"]; terminalReason = (string)checkpoint["reason"];
            requirements.Clear(); foreach (var property in ((JObject)checkpoint["requirements"]).Properties()) requirements[property.Name] = (JObject)property.Value.DeepClone();
            defense.Clear(); foreach (var property in ((JObject)checkpoint["defense"]).Properties()) defense[property.Name] = (long)property.Value;
            damageActions.Clear(); foreach (var action in (JArray)checkpoint["damage_actions"]) damageActions.Add((string)action);
            effects.Clear(); foreach (var effect in (JArray)checkpoint["effects"]) effects.Enqueue((JObject)effect.DeepClone());
        }

        public bool Blocked(Vector2 point, float radius) => Blocked(Layout(0), point, radius);
    }
}
