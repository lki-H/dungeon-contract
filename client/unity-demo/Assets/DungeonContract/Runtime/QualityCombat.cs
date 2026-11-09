using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class QualityWorld
    {
        private bool Ability(Player player, JObject scheme)
        {
            if (player.Loadout == "runesmith")
            {
                var point = player.Input.HasPlacement ? player.Input.Placement : player.Position + Aim(player.Input.Aim) * 1.2f;
                if (Vector2.Distance(point, player.Position) > Distance(scheme, "turret_placement_range_milli_tiles")) return false;
                var dual = Has(player, "dual_turret");
                var aim = Aim(player.Input.Aim); var perpendicular = new Vector2(-aim.y, aim.x) * 0.5f;
                var points = dual ? new[] { point - perpendicular, point + perpendicular } : new[] { point };
                if (points.Any(position => RoomGeometry.Blocked(Layout(player.Room), position, 0.3f) || enemies.Any(enemy => enemy.Health > 0 && enemy.Room == player.Room && Vector2.Distance(position, enemy.Position) < Radius(enemy) + 0.3f)) || turrets.Count + points.Length - turrets.Count(item => item.Owner == player.ID) > Number(config["authority"], "max_turrets")) return false;
                turrets.RemoveAll(item => item.Owner == player.ID);
                foreach (var position in points) turrets.Add(new Turret { ID = NextID("turret"), Owner = player.ID, Room = player.Room, Position = position, Until = combatNow + Number(scheme, "turret_duration_ms"), AttackAt = combatNow });
            }
            else
            {
                var angles = Has(player, "charged_volley") ? new[] { -24f, -12f, 0f, 12f, 24f } : ((JArray)scheme["ability_angles_deg"]).Select(item => (float)item).ToArray();
                if (projectiles.Count + angles.Length > Number(config["authority"], "max_projectiles")) return false;
                var action = NextID("ability");
                foreach (var angle in angles) SpawnProjectile(player.Room, player.Position, Rotate(Aim(player.Input.Aim), angle), player.ID, action, Number(scheme, "ability_damage"), Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "range_milli_tiles"), false, 0);
            }
            Effect("ability", player.ID, player.Room, player.Position); return true;
        }

        private void FirePlayer(Player player)
        {
            var scheme = schemes[player.Loadout]; var action = NextID("attack"); var fan = Has(player, "fan_shot");
            var damage = fan ? Number(scheme, "base_damage") * 650 / 1000 : Number(scheme, "base_damage");
            var angles = fan ? new[] { -12f, 0f, 12f } : new[] { 0f };
            if (projectiles.Count + angles.Length > Number(config["authority"], "max_projectiles")) return;
            foreach (var angle in angles) SpawnProjectile(player.Room, player.Position, Rotate(Aim(player.Input.Aim), angle), player.ID, action, damage, Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "range_milli_tiles"), false, Has(player, "piercing_bolt") ? 1 : 0);
            Effect("shot", player.ID, player.Room, player.Position);
        }

        private void SpawnProjectile(int room, Vector2 position, Vector2 direction, string owner, string action, int damage, float speed, float range, bool hostile, int penetration)
        {
            if (projectiles.Count >= Number(config["authority"], "max_projectiles")) return;
            projectiles.Add(new Projectile { ID = NextID("projectile"), Action = action, Owner = owner, Room = room, Position = position, Direction = direction, Damage = damage, Speed = speed, Remaining = range, Enemy = hostile, Penetration = penetration });
        }

        private void TickProjectiles()
        {
            foreach (var projectile in projectiles.ToArray())
            {
                var distance = Math.Min(projectile.Remaining, projectile.Speed / 30f); var removed = false;
                var steps = Math.Max(1, Mathf.CeilToInt(distance / Distance(extension["physics"], "sweep_step_milli")));
                for (var index = 0; index < steps && !removed; index++)
                {
                    projectile.Position += projectile.Direction * (distance / steps);
                    if (RoomGeometry.Blocked(Layout(projectile.Room), projectile.Position, Distance(extension["physics"], "projectile_radius_milli"))) { removed = true; break; }
                    if (projectile.Enemy)
                    {
                        var player = players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == projectile.Room).OrderBy(item => item.Slot).FirstOrDefault(item => Vector2.Distance(item.Position, projectile.Position) < 0.3f + Distance(extension["physics"], "projectile_radius_milli"));
                        if (player != null) { Hurt(player, projectile.Damage); removed = true; }
                    }
                    else
                    {
                        var enemy = enemies.FirstOrDefault(item => item.Health > 0 && item.Room == projectile.Room && !projectile.Hit.Contains(item.ID) && Vector2.Distance(item.Position, projectile.Position) < Radius(item) + Distance(extension["physics"], "projectile_radius_milli"));
                        if (enemy != null)
                        {
                            projectile.Hit.Add(enemy.ID); Hit(enemy, projectile.Owner, projectile.Action, projectile.Damage, projectile.Direction);
                            if (players.TryGetValue(projectile.Owner, out var owner) && Has(owner, "chain_rune"))
                            {
                                var next = enemies.Where(item => item.Health > 0 && item.Room == projectile.Room && item.ID != enemy.ID && !projectile.Hit.Contains(item.ID) && Vector2.Distance(item.Position, enemy.Position) <= 3 && RoomGeometry.ClearLine(Layout(item.Room), enemy.Position, item.Position)).OrderBy(item => Vector2.Distance(item.Position, enemy.Position)).ThenBy(item => item.ID, StringComparer.Ordinal).FirstOrDefault();
                                if (next != null) { projectile.Hit.Add(next.ID); Hit(next, owner.ID, projectile.Action, projectile.Damage * 700 / 1000); }
                            }
                            if (projectile.Penetration-- <= 0) removed = true;
                        }
                    }
                }
                projectile.Remaining -= distance;
                if (removed || projectile.Remaining <= 0) projectiles.Remove(projectile);
            }
        }

        private void Hit(Enemy enemy, string actor, string action, int requested, Vector2? direction = null)
        {
            if (!players.TryGetValue(actor, out var player) || player.Left || !aliveAtTick.Contains(actor) || enemy.Health <= 0 || requested <= 0) return;
            var damage = Math.Min(requested, enemy.Health); enemy.Health -= damage; player.Damage += damage; enemy.Damagers[actor] = combatNow;
            var objective = Layout(enemy.Room)["objective"];
            if (player.Room == enemy.Room && objective is JArray && Vector2.Distance(player.Position, RoomGeometry.Point(objective)) <= Distance(config["player"], "defense_radius_milli_tiles")) defense[player.Room + ":" + actor] = combatNow;
            if ((player.Damage == damage || Wants(actor, "damage", "")) && damageActions.Add(action)) facts.Add(Fact("damage", actor, enemy.ID, action, damage));
            if (enemy.Kind != "elite" && enemy.Kind != "boss" && enemy.ChargeUntil <= combatNow && enemy.WarningUntil <= combatNow && combatNow >= enemy.KnockbackAt)
            {
                enemy.Position = RoomGeometry.Move(Layout(enemy.Room), enemy.Position, (direction ?? (enemy.Position - player.Position).normalized) * Distance(extension["physics"], "knockback_milli"), Radius(enemy)); enemy.KnockbackAt = combatNow + Number(extension["physics"], "knockback_interval_ms");
            }
            if (Has(player, "frost")) enemy.SlowUntil = combatNow + 2000;
            Effect("hit", actor, enemy.Room, enemy.Position, damage, enemy.ID);
            if (enemy.Health > 0) return;
            player.Kills++;
            foreach (var other in enemy.Damagers.Where(item => item.Key != actor && combatNow - item.Value <= 5000)) if (players.TryGetValue(other.Key, out var helper) && !helper.Left) helper.Assists++;
            if (enemy.Kind == "boss" || !enemy.Summon && players.Keys.Any(id => Wants(id, "kill", ""))) { var fact = Fact("kill", actor, enemy.ID, action, 0); fact["contract_kill_eligible"] = !enemy.Summon; facts.Add(fact); }
            Effect("kill", actor, enemy.Room, enemy.Position, 0, enemy.ID);
        }

        private void Hurt(Player player, int requested)
        {
            if (player.Health <= 0 || player.Left || now < player.InvulnerableUntil) return;
            var absorbed = Math.Min(player.Shield, requested); player.Shield -= absorbed;
            var damage = Math.Min(player.Health, requested - absorbed); player.Health -= damage;
            if (damage + absorbed <= 0) return;
            player.InvulnerableUntil = now + Number(extension["physics"], "contact_protection_ms"); player.InteractionTarget = null; Effect("hurt", player.ID, player.Room, player.Position, damage + absorbed);
            if (player.Health > 0) return;
            player.Deaths++; player.Input = default; player.BufferedAction = null; player.DodgeUntil = 0;
            facts.Add(Fact("death", player.ID, "life_" + player.Deaths, null, 0)); Effect("death", player.ID, player.Room, player.Position);
        }

        private void TickTurrets()
        {
            turrets.RemoveAll(item => combatNow >= item.Until || !players.TryGetValue(item.Owner, out var owner) || owner.Left || owner.Health <= 0);
            foreach (var turret in turrets)
            {
                var owner = players[turret.Owner]; var scheme = schemes[owner.Loadout];
                var target = enemies.Where(item => item.Health > 0 && item.Room == turret.Room && Vector2.Distance(item.Position, turret.Position) <= Distance(scheme, "turret_range_milli_tiles") && RoomGeometry.ClearLine(Layout(turret.Room), turret.Position, item.Position)).OrderBy(item => Vector2.Distance(item.Position, turret.Position)).ThenBy(item => item.ID, StringComparer.Ordinal).FirstOrDefault();
                if (target == null || turret.AttackAt > combatNow) continue;
                var damage = Has(owner, "dual_turret") ? 5 : Number(scheme, "turret_damage"); if (Has(owner, "heavy_turret")) damage = damage * 1500 / 1000;
                SpawnProjectile(turret.Room, turret.Position, (target.Position - turret.Position).normalized, owner.ID, NextID("turret_shot"), damage, Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "turret_range_milli_tiles"), false, 0);
                turret.AttackAt = combatNow + Number(scheme, "turret_attack_interval_ms"); Effect("turret_shot", owner.ID, turret.Room, turret.Position);
            }
        }

        private void TickEnemy(Enemy enemy)
        {
            var target = players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room).OrderBy(item => Vector2.Distance(item.Position, enemy.Position)).ThenBy(item => item.Slot).FirstOrDefault();
            if (target == null) { enemy.StuckAt = combatNow; return; }
            var definition = enemyDefinitions[enemy.Kind];
            if (enemy.ChargeUntil > combatNow)
            {
                enemy.StuckAt = combatNow;
                var before = enemy.Position; enemy.Position = RoomGeometry.Move(Layout(enemy.Room), enemy.Position, enemy.Aim * Distance(definition, "charge_speed_milli_tiles_per_second", 6000) / 30, Radius(enemy));
                foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room)) if (Vector2.Distance(player.Position, enemy.Position) <= Radius(enemy) + 0.3f) Hurt(player, Number(definition, "damage"));
                if (Vector2.Distance(before, enemy.Position) < 0.01f) enemy.ChargeUntil = combatNow;
                return;
            }
            if (enemy.WarningUntil > 0)
            {
                enemy.StuckAt = combatNow;
                if (combatNow < enemy.WarningUntil) return;
                ExecuteEnemyAttack(enemy, definition); enemy.WarningUntil = 0; enemy.Warning = null;
                enemy.AttackAt = Math.Max(enemy.AttackAt, combatNow + Number(definition, "recovery_ms", enemy.Kind == "boss" ? 900 : 500)); return;
            }
            var keep = enemy.Kind == "archer" ? 5f : enemy.Kind == "boss" ? 4f : Distance(definition, "attack_range_milli_tiles", 1200);
            var distance = Vector2.Distance(enemy.Position, target.Position);
            if (distance > keep || !RoomGeometry.ClearLine(Layout(enemy.Room), enemy.Position, target.Position))
            {
                var direction = RoomGeometry.PathDirection(Layout(enemy.Room), enemy.Position, target.Position, Radius(enemy));
                enemy.Position = RoomGeometry.Move(Layout(enemy.Room), enemy.Position, direction * Distance(definition, "move_milli_tiles_per_second") * (enemy.SlowUntil > combatNow ? enemy.Kind == "boss" ? 0.95f : 0.8f : 1) / 30, Radius(enemy));
                if (Vector2.Distance(enemy.Position, enemy.StuckPosition) > 0.3f) { enemy.StuckPosition = enemy.Position; enemy.StuckAt = combatNow; }
                else if (combatNow - enemy.StuckAt >= 5000)
                {
                    if (enemy.RecoveryCount++ >= 2) { SetTerminal("aborted", "technical_failure"); return; }
                    var position = SpawnPoint(enemy.Room, enemy.ID + ":recovery");
                    if (position.HasValue) { enemy.Position = position.Value; enemy.StuckPosition = position.Value; }
                    enemy.StuckAt = combatNow;
                }
            }
            else enemy.StuckAt = combatNow;
            if (combatNow < enemy.AttackAt || !RoomGeometry.ClearLine(Layout(enemy.Room), enemy.Position, target.Position)) return;
            string kind;
            if (enemy.Kind == "boss")
            {
                var pattern = (JArray)definition[enemy.Health * 100 <= enemy.Maximum * Number(definition, "phase_2_threshold_percent") ? "phase_2" : "phase_1"];
                kind = (string)pattern[enemy.Pattern++ % pattern.Count];
                if (kind == "summon") { Summon(enemy, definition); enemy.AttackAt = combatNow + 1500; return; }
            }
            else if (enemy.Kind == "elite") { var pattern = (JArray)definition["phase_pattern"]; kind = (string)pattern[enemy.Pattern++ % pattern.Count]; }
            else kind = enemy.Kind == "archer" ? "shot" : enemy.Kind == "charger" ? "charge" : "melee";
            if (kind == "melee" && distance > keep + 0.4f) return;
            enemy.Warning = kind; enemy.Origin = enemy.Position; enemy.Aim = (target.Position - enemy.Position).normalized;
            enemy.WarningUntil = combatNow + Number(definition, kind == "fan" ? "fan_telegraph_ms" : kind == "stomp" ? "stomp_telegraph_ms" : "telegraph_ms", 750);
            enemy.AttackAt = enemy.WarningUntil + Number(definition, kind == "fan" ? "fan_interval_ms" : kind == "stomp" ? "stomp_interval_ms" : "attack_interval_ms", 1000);
            Effect("warning", enemy.ID, enemy.Room, enemy.Origin);
        }

        private void ExecuteEnemyAttack(Enemy enemy, JObject definition)
        {
            var kind = enemy.Warning;
            if (kind == "charge") { enemy.ChargeUntil = combatNow + Number(definition, "charge_duration_ms", 600); enemy.AttackAt = enemy.ChargeUntil + Number(definition, "recovery_ms", 1000); return; }
            if (kind == "fan" || kind == "shot")
            {
                var angles = kind == "fan" ? ((JArray)definition["fan_angles_deg"]).Select(item => (float)item).ToArray() : new[] { 0f };
                foreach (var angle in angles) SpawnProjectile(enemy.Room, enemy.Origin, Rotate(enemy.Aim, angle), enemy.ID, NextID("enemy_attack"), Number(definition, kind == "fan" ? "fan_damage" : "damage"), Distance(definition, "projectile_speed_milli_tiles_per_second"), Distance(definition, "range_milli_tiles", 12000), true, 0);
                return;
            }
            var radius = kind == "stomp" ? Distance(definition, "stomp_radius_milli_tiles") : Distance(definition, "attack_range_milli_tiles");
            foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room))
            {
                var offset = player.Position - enemy.Origin;
                if (offset.magnitude <= radius + 0.3f && (kind == "stomp" || Vector2.Dot(offset.normalized, enemy.Aim) >= 0.25f) && RoomGeometry.ClearLine(Layout(enemy.Room), enemy.Origin, player.Position)) Hurt(player, Number(definition, kind == "stomp" ? "stomp_damage" : "damage"));
            }
        }

        private void Summon(Enemy boss, JObject definition)
        {
            var amount = Math.Min(Number(definition, "summon_per_wave"), Math.Min(Number(definition, "summon_total_limit") - boss.Summoned, Number(definition, "summon_alive_limit") - enemies.Count(item => item.Health > 0 && item.Summon)));
            for (var index = 0; index < amount; index++)
            {
                if (enemies.Count(item => item.Health > 0) >= Number(config["authority"], "max_alive_enemies")) break;
                var point = SpawnPoint(boss.Room, "summon_" + boss.Summoned);
                if (!point.HasValue) break;
                SpawnEnemy(boss.Room, (string)definition["summon_id"], point.Value, true); boss.Summoned++;
            }
            Effect("summon", boss.ID, boss.Room, boss.Position);
        }

        private void TickTraps()
        {
            foreach (var trap in traps.Where(item => rooms[item.Room].Status == "active"))
            {
                var settings = extension["trap"];
                var triggering = players.Values.Any(item => !item.Left && item.Health > 0 && item.Room == trap.Room && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli")) || enemies.Any(item => item.Health > 0 && item.Room == trap.Room && item.Kind != "elite" && item.Kind != "boss" && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli"));
                if (trap.Phase == "armed" && triggering) { trap.Phase = "warning"; trap.Until = combatNow + Number(settings, "warning_ms"); trap.Hit.Clear(); Effect("trap_warning", trap.ID, trap.Room, trap.Position); }
                else if (trap.Phase == "warning" && combatNow >= trap.Until) { trap.Phase = "active"; trap.Until = combatNow + Number(settings, "active_ms"); }
                else if (trap.Phase == "active")
                {
                    foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == trap.Room && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli") + 0.3f)) if (trap.Hit.Add(player.ID + ":" + player.Deaths)) Hurt(player, Number(settings, "player_damage"));
                    foreach (var enemy in enemies.Where(item => item.Health > 0 && item.Room == trap.Room && item.Kind != "elite" && item.Kind != "boss" && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli") + Radius(item))) if (trap.Hit.Add(enemy.ID)) { enemy.Health = Math.Max(0, enemy.Health - Number(settings, "enemy_damage")); Effect("environment_hit", trap.ID, trap.Room, enemy.Position, Number(settings, "enemy_damage"), enemy.ID); }
                    if (combatNow >= trap.Until) { trap.Phase = "cooldown"; trap.Until = combatNow + Number(settings, "cooldown_ms"); }
                }
                else if (trap.Phase == "cooldown" && combatNow >= trap.Until) trap.Phase = "armed";
            }
        }

        private void TickRooms()
        {
            foreach (var room in rooms.Where(item => item.Exists && item.Index > 0))
            {
                if (room.Status == "unvisited" && players.Values.Any(item => !item.Left && item.Health > 0 && item.Connected && item.Room == room.Index)) { room.Status = "warning"; room.WarningAt = combatNow + (room.Index == 5 ? Number(config["rooms"][5], "boss_warning_ms") : Number(extension["encounter"], "room_warning_ms")); Effect("room_warning", null, room.Index, Vector2.zero); }
                if (room.Status == "warning" && combatNow >= room.WarningAt) { room.Status = "active"; QueueWave(room); }
                if (room.Status != "active") continue;
                if (room.NextWaveAt > 0 && combatNow >= room.NextWaveAt) { room.NextWaveAt = 0; QueueWave(room); }
                var remaining = roomLimit - enemies.Count(item => item.Health > 0 && item.Room == room.Index);
                if (remaining > 0 && room.Pending.Count > 0 && enemies.Count(item => item.Health > 0) < Number(config["authority"], "max_alive_enemies"))
                {
                    var point = SpawnPoint(room.Index, "wave_" + room.Wave + "_" + room.Pending.Count);
                    if (point.HasValue) { SpawnEnemy(room.Index, room.Pending.Dequeue(), point.Value); room.SpawnBlockedAt = 0; }
                    else if (room.SpawnBlockedAt == 0) room.SpawnBlockedAt = combatNow;
                    else if (combatNow - room.SpawnBlockedAt > Number(extension["encounter"], "room_spawn_block_timeout_ms")) { SetTerminal("aborted", "technical_failure"); return; }
                }
            }
        }

        private void QueueWave(Room room)
        {
            if (room.Index == 5) { if (room.Wave++ == 0) room.Pending.Enqueue("boss"); return; }
            if (room.Index >= 6) { if (room.Wave++ == 0) for (var index = 0; index < (4 * countScale + 999) / 1000; index++) room.Pending.Enqueue(index % 3 == 0 ? "archer" : "guard"); return; }
            var waves = (JArray)config["rooms"][room.Index]["waves"]; if (room.Wave >= waves.Count) return;
            var wave = waves[room.Wave++]; var count = (Number(wave, "count") * countScale + 999) / 1000;
            var mix = (JArray)wave["mix"]; var weights = ((JArray)wave["weights"]).Select(item => (int)item).ToArray(); var total = weights.Sum();
            var amounts = weights.Select(weight => count * weight / total).ToArray(); foreach (var index in Enumerable.Range(0, weights.Length).OrderByDescending(index => count * weights[index] % total).ThenBy(index => index).Take(count - amounts.Sum())) amounts[index]++;
            var kinds = new List<string>(); for (var index = 0; index < mix.Count; index++) for (var amount = 0; amount < amounts[index]; amount++) kinds.Add((string)mix[index]);
            foreach (var kind in kinds.Select((kind, index) => new { Kind = kind, Rank = Rank("wave_" + room.Index + "_" + room.Wave, index.ToString()) }).OrderBy(item => item.Rank, StringComparer.Ordinal)) room.Pending.Enqueue(kind.Kind);
            if (room.Index == 4 && !room.EliteQueued) { room.Pending.Enqueue("elite"); room.EliteQueued = true; }
        }

        private Vector2? SpawnPoint(int room, string decision)
        {
            var layout = Layout(room); var points = new List<Vector2>();
            for (var horizontal = 2; horizontal < (int)layout["width"] - 2; horizontal++) for (var vertical = 2; vertical < (int)layout["height"] - 2; vertical++)
            {
                var point = new Vector2(horizontal + 0.5f, vertical + 0.5f);
                if (RoomGeometry.Blocked(layout, point, 0.9f) || players.Values.Any(item => !item.Left && item.Health > 0 && item.Room == room && Vector2.Distance(item.Position, point) < 2) || enemies.Any(item => item.Health > 0 && item.Room == room && Vector2.Distance(item.Position, point) < Radius(item) + 0.6f) || traps.Any(item => item.Room == room && Vector2.Distance(item.Position, point) < 1.1f) || layout["objective"] is JArray && Vector2.Distance(RoomGeometry.Point(layout["objective"]), point) < 1.2f) continue;
                points.Add(point);
            }
            return points.Count == 0 ? (Vector2?)null : points.OrderBy(point => Rank(decision, point.x + ":" + point.y), StringComparer.Ordinal).First();
        }

        private void SpawnEnemy(int room, string kind, Vector2 position, bool summon = false)
        {
            var health = (Number(enemyDefinitions[kind], "max_health") * healthScale + 999) / 1000;
            enemies.Add(new Enemy { ID = kind == "boss" ? "boss" : NextID(kind), Kind = kind, Room = room, Position = position, Origin = position, Health = health, Maximum = health, Summon = summon, AttackAt = combatNow + Number(extension["encounter"], "wave_start_warning_ms"), StuckAt = combatNow, StuckPosition = position }); Effect("spawn", kind, room, position);
        }

        private void TickInteraction(Player player)
        {
            if (player.Room != 2 && player.Room != 3) { player.InteractionTarget = null; return; }
            var room = rooms[player.Room]; var target = player.Room == 2 ? "hub_a" : "hub_b";
            if (room.Mechanism || room.Status != "active" || player.Left || !player.Connected || player.Health <= 0 || now < player.ProtectedUntil || now < player.DodgeUntil || !player.Input.Interact || player.Input.Attack || player.Input.Move.sqrMagnitude > 0 || Vector2.Distance(player.Position, RoomGeometry.Point(Layout(player.Room)["objective"])) > Distance(config["player"], "interaction_radius_milli_tiles")) { player.InteractionTarget = null; return; }
            if (players.Values.Any(item => item.ID != player.ID && item.InteractionTarget == target)) { player.InteractionTarget = null; return; }
            if (player.InteractionTarget != target) { player.InteractionTarget = target; player.InteractionAt = now; }
            if (now - player.InteractionAt < Number(config["player"], "interaction_ms")) return;
            room.Mechanism = true; player.Interactions++;
            var fact = Fact("interact", player.ID, target, null, 0);
            fact["contributors"] = new JArray(players.Values.Where(item => !item.Left && item.ID != player.ID && item.Room == player.Room && defense.TryGetValue(player.Room + ":" + item.ID, out var evidence) && combatNow - evidence <= Number(config["player"], "defense_window_ms")).Select(item => item.ID));
            facts.Add(fact); player.InteractionTarget = null; Effect("interaction", player.ID, player.Room, RoomGeometry.Point(Layout(player.Room)["objective"]));
        }

        private void FinishRooms()
        {
            foreach (var room in rooms.Where(item => item.Status == "active"))
            {
                if (room.Pending.Count > 0 || enemies.Any(item => item.Health > 0 && item.Room == room.Index)) continue;
                var totalWaves = room.Index >= 5 ? 1 : ((JArray)config["rooms"][room.Index]["waves"]).Count;
                if (room.Wave < totalWaves) { if (room.NextWaveAt == 0) room.NextWaveAt = combatNow + Number(config["rooms"][room.Index], "wave_gap_ms", 1000); continue; }
                if ((room.Index == 2 || room.Index == 3) && !room.Mechanism) continue;
                room.Status = "cleared"; Effect("room_clear", null, room.Index, Vector2.zero);
                if (room.Index == 6) foreach (var member in players.Values.Where(item => !item.Left)) if (member.Claims.Add("rune_cache")) member.PendingShield = 15;
            }
            if (rooms[5].Status == "cleared" || players.Values.Where(item => !item.Left).All(item => item.Health <= 0) && reinforcementRemaining <= 0 && !players.Values.Any(item => item.Reinforcement != null)) return;
            foreach (var index in new[] { 1, 3, 4 }) if (rooms[index].Status == "cleared" && !rooms[index].NodeOpened) { rooms[index].NodeOpened = true; safeNode = index == 1 ? 0 : index == 3 ? 1 : 2; safeUntil = now + Number(config["player"], "safe_choice_timeout_ms"); projectiles.Clear(); foreach (var player in players.Values) { player.Input.Attack = false; player.Input.AttackPressed = false; player.BufferedAction = null; player.InteractionTarget = null; player.DodgeUntil = 0; } Effect("choice", null, index, Vector2.zero); break; }
        }

        public string[] Candidates(string id)
        {
            if (safeNode < 0 || !players.TryGetValue(id, out var player)) return Array.Empty<string>();
            var legal = upgrades.Values.Where(item => ((string)item["scope"] == "common" || (string)item["scope"] == player.Loadout) && !player.Upgrades.Contains((string)item["id"])).Select(item => (string)item["id"]).ToArray();
            if ((string)manifest["mode"] == "trial")
            {
                var priority = ((JArray)config["trial"]["candidate_priority"][player.Loadout]).Select(item => (string)item).ToArray();
                var rotated = Enumerable.Range(0, priority.Length).Select(index => priority[(index + safeNode * 2) % priority.Length]); return rotated.Where(legal.Contains).Take(3).ToArray();
            }
            return legal.OrderBy(option => Rank("choice_" + (safeNode + 1) + ":" + player.Loadout, option), StringComparer.Ordinal).Take(3).ToArray();
        }

        public bool SelectUpgrade(string id, string option)
        {
            if (safeNode < 0 || now >= safeUntil || !players.TryGetValue(id, out var player) || player.Left || player.Choices.ContainsKey(safeNode) || !Candidates(id).Contains(option)) { LastActionReason = "invalid_choice"; return false; }
            player.Choices[safeNode] = option; LastActionReason = "accepted"; return true;
        }

        private void CloseChoice()
        {
            foreach (var player in players.Values.Where(item => !item.Left))
            {
                if (!player.Choices.TryGetValue(safeNode, out var option)) { var candidates = Candidates(player.ID); if (candidates.Length != 3) { SetTerminal("aborted", "technical_failure"); return; } option = candidates[0]; player.Choices[safeNode] = option; }
                if (!player.Upgrades.Contains(option)) player.Upgrades.Add(option);
            }
            safeNode = -1; playerInputsClear(); Effect("choice_end", null, 0, Vector2.zero);
        }

        private bool Wants(string id, string kind, string target) => requirements.TryGetValue(id, out var remaining) && (long?)remaining[kind + ":" + target] > 0;
        private static float Distance(JToken token, string key, int fallback) => Number(token, key, fallback) / 1000f;
    }
}
