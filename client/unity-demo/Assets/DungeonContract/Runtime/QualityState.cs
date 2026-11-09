using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class QualityWorld
    {
        public void Acknowledge(JObject receipt)
        {
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
            ["participants"] = new JArray(players.Values.OrderBy(player => player.Slot).Select(player => new JObject { ["player_id"] = player.ID, ["slot"] = player.Slot, ["loadout_id"] = player.Loadout, ["damage"] = player.Damage, ["kills"] = player.Kills, ["assists"] = player.Assists, ["interactions"] = player.Interactions, ["deaths"] = player.Deaths, ["choices"] = new JArray(player.Upgrades), ["effective_contribution"] = player.Damage > 0 || player.Interactions > 0 || player.Reaches > 0 })),
            ["trial_eligible"] = (string)manifest["mode"] == "trial", ["ineligible_reason"] = "none"
        };

        public JObject TerminalRequest(long frame, long sequence) => new JObject { ["outcome"] = Outcome, ["reason"] = terminalReason, ["cutoff_frame"] = frame, ["cutoff_sequence"] = sequence };

        public JObject Snapshot() => new JObject
        {
            ["schema_version"] = 2, ["run_id"] = manifest["run_id"], ["world_frame_id"] = WorldTick, ["elapsed_ms"] = now, ["room"] = MaximumRoom, ["safe_node"] = safeNode, ["safe_remaining_ms"] = safeNode < 0 ? 0 : Math.Max(0, safeUntil - now), ["reinforcement_remaining"] = reinforcementRemaining,
            ["completed"] = new JArray(rooms.Take(6).Select(room => room.Status == "cleared")),
            ["rooms"] = new JArray(rooms.Select(room => new JObject { ["index"] = room.Index, ["id"] = Layout(room.Index)["id"], ["exists"] = room.Exists, ["status"] = room.Status, ["wave"] = room.Wave, ["pending"] = room.Pending.Count, ["mechanism_complete"] = room.Mechanism, ["warning_remaining_ms"] = Math.Max(0, room.WarningAt - combatNow), ["enemies_remaining"] = room.Pending.Count + enemies.Count(enemy => enemy.Room == room.Index && enemy.Health > 0) })),
            ["players"] = new JArray(players.Values.OrderBy(player => player.Slot).Select(player => new JObject
            {
                ["player_id"] = player.ID, ["slot"] = player.Slot, ["loadout_id"] = player.Loadout, ["room_id"] = player.Room, ["x"] = player.Position.x, ["y"] = player.Position.y, ["health"] = player.Health, ["shield"] = player.Shield, ["deaths"] = player.Deaths, ["damage"] = player.Damage, ["connected"] = player.Connected, ["left"] = player.Left,
                ["last_input_sequence"] = player.Ack, ["dodge_direction_x"] = player.DodgeDirection.x, ["dodge_direction_y"] = player.DodgeDirection.y, ["dodge_duration_remaining_ms"] = Math.Max(0, player.DodgeUntil - now), ["dodge_remaining_ms"] = Math.Max(0, player.DodgeAt - now), ["ability_remaining_ms"] = Math.Max(0, player.AbilityAt - now), ["protected_remaining_ms"] = Math.Max(0, player.ProtectedUntil - now),
                ["interaction_remaining_ms"] = player.InteractionTarget == null ? 0 : Math.Max(0, Number(config["player"], "interaction_ms") - (now - player.InteractionAt)), ["supply_used"] = player.Claims.Contains("supply_court"), ["respawn_remaining_ms"] = player.Reinforcement == null ? -1 : Math.Max(0, player.RespawnAt - now),
                ["upgrades"] = new JArray(player.Upgrades), ["candidates"] = new JArray(Candidates(player.ID)), ["choice"] = safeNode >= 0 && player.Choices.TryGetValue(safeNode, out var choice) ? choice : null
            })),
            ["enemies"] = new JArray(enemies.Where(enemy => enemy.Health > 0).Select(enemy => new JObject { ["id"] = enemy.ID, ["kind"] = enemy.Kind, ["room_id"] = enemy.Room, ["x"] = enemy.Position.x, ["y"] = enemy.Position.y, ["health"] = enemy.Health, ["maximum"] = enemy.Maximum, ["warning"] = enemy.WarningUntil > combatNow ? enemy.Warning : "", ["warning_remaining_ms"] = Math.Max(0, enemy.WarningUntil - combatNow), ["warning_x"] = enemy.Origin.x, ["warning_y"] = enemy.Origin.y, ["aim_x"] = enemy.Aim.x, ["aim_y"] = enemy.Aim.y, ["phase"] = enemy.Kind == "boss" && enemy.Health * 100 <= enemy.Maximum * 50 ? 2 : 1 })),
            ["projectiles"] = new JArray(projectiles.Select(projectile => new JObject { ["id"] = projectile.ID, ["room_id"] = projectile.Room, ["x"] = projectile.Position.x, ["y"] = projectile.Position.y, ["enemy"] = projectile.Enemy })),
            ["turrets"] = new JArray(turrets.Select(turret => new JObject { ["id"] = turret.ID, ["owner"] = turret.Owner, ["room_id"] = turret.Room, ["x"] = turret.Position.x, ["y"] = turret.Position.y })),
            ["traps"] = new JArray(traps.Select(trap => new JObject { ["id"] = trap.ID, ["room_id"] = trap.Room, ["x"] = trap.Position.x, ["y"] = trap.Position.y, ["phase"] = trap.Phase, ["remaining_ms"] = Math.Max(0, trap.Until - combatNow) })),
            ["effects"] = new JArray(effects.Where(effect => now - (long)effect["at"] <= 200).Select(effect => effect.DeepClone())), ["boss_warning_remaining_ms"] = rooms[5].Status == "warning" ? Math.Max(0, rooms[5].WarningAt - combatNow) : 0
        };

        public JObject Capture() => new JObject
        {
            ["players"] = JArray.FromObject(players.Values, serializer), ["rooms"] = JArray.FromObject(rooms, serializer), ["enemies"] = JArray.FromObject(enemies, serializer), ["projectiles"] = JArray.FromObject(projectiles, serializer), ["turrets"] = JArray.FromObject(turrets, serializer), ["traps"] = JArray.FromObject(traps, serializer),
            ["now"] = now, ["combat_now"] = combatNow, ["last_tick_at"] = lastTickAt, ["tick"] = WorldTick, ["serial"] = serial, ["budget"] = reinforcementRemaining, ["safe_node"] = safeNode, ["safe_until"] = safeUntil, ["outcome"] = Outcome, ["reason"] = terminalReason,
            ["requirements"] = JObject.FromObject(requirements), ["defense"] = JObject.FromObject(defense), ["damage_actions"] = new JArray(damageActions.OrderBy(item => item, StringComparer.Ordinal)), ["effects"] = new JArray(effects.Select(effect => effect.DeepClone()))
        };

        public void Restore(JObject checkpoint)
        {
            players.Clear(); foreach (var player in checkpoint["players"].ToObject<Player[]>(serializer)) players[player.ID] = player;
            rooms = checkpoint["rooms"].ToObject<Room[]>(serializer); enemies.Clear(); enemies.AddRange(checkpoint["enemies"].ToObject<Enemy[]>(serializer)); projectiles.Clear(); projectiles.AddRange(checkpoint["projectiles"].ToObject<Projectile[]>(serializer)); turrets.Clear(); turrets.AddRange(checkpoint["turrets"].ToObject<Turret[]>(serializer)); traps.Clear(); traps.AddRange(checkpoint["traps"].ToObject<Trap[]>(serializer));
            now = (long)checkpoint["now"]; combatNow = (long)checkpoint["combat_now"]; lastTickAt = (long)checkpoint["last_tick_at"]; WorldTick = (long)checkpoint["tick"]; serial = (long)checkpoint["serial"]; reinforcementRemaining = (int)checkpoint["budget"]; safeNode = (int)checkpoint["safe_node"]; safeUntil = (long)checkpoint["safe_until"]; Outcome = (string)checkpoint["outcome"]; terminalReason = (string)checkpoint["reason"];
            requirements.Clear(); foreach (var property in ((JObject)checkpoint["requirements"]).Properties()) requirements[property.Name] = (JObject)property.Value.DeepClone();
            defense.Clear(); foreach (var property in ((JObject)checkpoint["defense"]).Properties()) defense[property.Name] = (long)property.Value;
            damageActions.Clear(); foreach (var action in (JArray)checkpoint["damage_actions"]) damageActions.Add((string)action);
            effects.Clear(); foreach (var effect in (JArray)checkpoint["effects"]) effects.Enqueue((JObject)effect.DeepClone());
        }

        public bool Blocked(Vector2 point, float radius) => RoomGeometry.Blocked(Layout(0), point, radius);
    }
}
