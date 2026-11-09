using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private void SpawnProjectile(int room, Vector2 position, Vector2 direction, string owner, string action, int damage, float speed, float range, bool hostile, int penetration,long sourceLife=0)
        {
            if (ProjectileLoad >= Number(config["authority"], "max_projectiles")) return;
            if(V7&&hostile){var enemy=enemies.FirstOrDefault(e=>e.ID==owner);if(enemy!=null)position+=Body(enemy).Center;}
            var networkGroup=V5?ChooseV5NetworkGroup():0;
            projectiles.Add(new Projectile {ProtocolVersion=V8?8:V7?7:V6?6:5,BornGameMS=now,NetworkGroup=networkGroup,SourceLife=sourceLife>0?sourceLife:players.TryGetValue(owner,out var emitter)?emitter.LifeGeneration:0,EmissionFrame=WorldTick, ID = NextID("projectile"), Action = action, Owner = owner, Room = room, Position = position, Direction = direction, Damage = damage, Speed = speed, Remaining = range, Enemy = hostile, Penetration = penetration, Unbounded = Weapons && P2, ChainEnabled=P2&&players.TryGetValue(owner,out var shooter)&&Has(shooter,"runesmith_chain"), FrostEnabled=P2&&players.TryGetValue(owner,out var frostOwner)&&Has(frostOwner,"frost") });
        }

        private void TickProjectiles()
        {
            if(Weapons){TickWeaponProjectiles();return;}
            foreach (var projectile in projectiles.ToArray())
            {
                var distance = Math.Min(projectile.Remaining, projectile.Speed / 30f); var removed = false;
                var steps = Math.Max(1, Mathf.CeilToInt(distance / Distance(extension["physics"], "sweep_step_milli")));
                for (var index = 0; index < steps && !removed; index++)
                {
                    projectile.Position += projectile.Direction * (distance / steps);
                    if (ProjectileTerrain(projectile)) { removed = true; break; }
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
                            if (players.TryGetValue(projectile.Owner, out var owner) && Has(owner, "runesmith_chain"))
                            {
                                var next = enemies.Where(item => item.Health > 0 && item.Room == projectile.Room && item.ID != enemy.ID && !projectile.Hit.Contains(item.ID) && Vector2.Distance(item.Position, enemy.Position) <= 3 && ClearLine(Layout(item.Room), enemy.Position, item.Position)).OrderBy(item => Vector2.Distance(item.Position, enemy.Position)).ThenBy(item => item.ID, StringComparer.Ordinal).FirstOrDefault();
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

        private void Hit(Enemy enemy, string actor, string action, int requested, Vector2? direction = null, bool existingEmission=false,long sourceLife=0,long emissionFrame=0,string rootAction=null,bool? frostSnapshot=null,bool noKnockback=false)
        {
            if (!players.TryGetValue(actor, out var player) || player.Left || !(P2&&existingEmission)&&!aliveAtTick.Contains(actor) || enemy.Health <= 0 || requested <= 0) return;
            if(Weapons&&!enemy.HitActions.Add(action))return;
            var damage = Math.Min(requested, enemy.Health); enemy.Health -= damage; player.Damage += damage; enemy.Damagers[actor] = combatNow;
            var objective = Layout(enemy.Room)["objective"];
            if (player.Room == enemy.Room && objective is JArray && Vector2.Distance(player.Position, RoomGeometry.Point(objective)) <= Distance(config["player"], "defense_radius_milli_tiles")) defense[player.Room + ":" + actor] = combatNow;
            var root=rootAction??action;
            if (!(V5&&enemy.Summon)&&damageActions.Add(root) && (!P3 || NeedP3Fact("damage",actor,""))) {var fact=Fact("damage", actor, enemy.ID, root, damage);if(P3)fact["proof"]=new JObject{["origin_life_generation"]=sourceLife>0?sourceLife:player.LifeGeneration,["emission_frame"]=emissionFrame>0?emissionFrame:WorldTick};if(V5)fact["spawn_origin"]=enemy.Kind=="boss"?"boss_body":enemy.Room>=6?"branch_wave":"main_wave";facts.Add(fact);}
            if(P3)RecordP3Defense(player,"damage",action,damage,enemy.Room);
            if (!noKnockback && enemy.Kind != "elite" && enemy.Kind != "boss" && enemy.ChargeUntil <= combatNow && enemy.WarningUntil <= combatNow && combatNow >= enemy.KnockbackAt)
            {
                enemy.Position = V7?V7MoveEnemy(enemy,(direction??(Center(enemy)-Center(player)).normalized)*Distance(extension["physics"],"knockback_milli")):Move(Layout(enemy.Room), enemy.Position, (direction ?? (enemy.Position - player.Position).normalized) * Distance(extension["physics"], "knockback_milli"), Radius(enemy)); enemy.KnockbackAt = combatNow + Number(extension["physics"], "knockback_interval_ms");
            }
            if (frostSnapshot??Has(player, "frost")) ApplySlow(enemy,enemy.Kind=="boss"?0.95f:0.8f,2000);
            Effect("hit", actor, enemy.Room, enemy.Position, damage, enemy.ID);
            if (enemy.Health > 0) return;
            if(P3&&enemy.Kind=="boss")rooms[5].Status="cleared";
            enemy.ReservedShots=0;
            player.Kills++;
            foreach (var other in enemy.Damagers.Where(item => item.Key != actor && combatNow - item.Value <= 5000)) if (players.TryGetValue(other.Key, out var helper) && !helper.Left) helper.Assists++;
            if (enemy.Kind == "boss" || !enemy.Summon) { if(!P3||enemy.Kind=="boss"||NeedP3Fact("kill",actor,"")){var fact = Fact("kill", actor, enemy.ID, action, 0); fact["contract_kill_eligible"] = !enemy.Summon;if(V5)fact["spawn_origin"]=enemy.Kind=="boss"?"boss_body":enemy.Room>=6?"branch_wave":"main_wave"; facts.Add(fact);} }
            Effect("kill", actor, enemy.Room, enemy.Position, 0, enemy.ID);
        }

        private void Hurt(Player player, int requested,string attack=null)
        {
            if (Practice || player.Health <= 0 || player.Left || (V6&&v6ContactTime>=0?v6ContactTime:now) < (V6?Math.Max(Math.Max(player.SpaceUntil,player.GustUntil),player.ProtectedUntil):player.InvulnerableUntil) || Weapons&&now<player.HurtUntil) return;
            if(Weapons&&attack!=null&&!player.HurtActions.Add(attack))return;
            var absorbed = Math.Min(player.Shield, requested); player.Shield -= absorbed;
            var damage = Math.Min(player.Health, requested - absorbed); player.Health -= damage;
            if (damage + absorbed <= 0) return;
            if(P3){player.HurtAt=now;player.RescueTarget=null;if(absorbed>0)P3Protection(player,absorbed,attack);}
            if(Weapons)player.HurtUntil=(V6&&v6ContactTime>=0?(long)Math.Ceiling(v6ContactTime):now)+Number(parameters["player"],"hurt_invulnerable_ms");
            player.InteractionTarget = null; Effect("hurt", player.ID, player.Room, player.Position, damage + absorbed);
            if (player.Health > 0) return;
            if(V8)V8StopSpin(player,"life_exhausted");player.Knockdowns++; player.Life="downed"; player.Input=default; CancelBasic(player); player.DodgeUntil=0;
            if(P3){player.DownedGeneration++;player.DownedUntil=now+Number(parameters["player"],"downed_duration_ms");player.RescueTarget=null;fields.RemoveAll(field=>field.Owner==player.ID);facts.Add(P3LifeFact("downed",player));}
            Effect("downed", player.ID, player.Room, player.Position);
        }

        private void TickTurrets()
        {
            turrets.RemoveAll(item => combatNow >= item.Until || !players.TryGetValue(item.Owner, out var owner) || owner.Left || (!P2 && owner.Health <= 0));
            foreach (var turret in turrets)
            {
                var owner = players[turret.Owner]; var scheme = schemes[owner.Loadout];
                if(V5){TickV5Turret(turret,owner);continue;}
                var target = enemies.Where(item => item.Health > 0 && item.Room == turret.Room && Vector2.Distance(item.Position, turret.Position) <= (P2?12f:Distance(scheme, "turret_range_milli_tiles")) && ClearLine(Layout(turret.Room), turret.Position, item.Position)).OrderBy(item => Vector2.Distance(item.Position, turret.Position)).ThenBy(item => item.ID, StringComparer.Ordinal).FirstOrDefault();
                if (target == null || turret.AttackAt > combatNow) continue;
                if(P2&&ProjectileLoad>=Number(config["authority"],"max_projectiles"))continue;
                var damage = Weapons?turret.BaseDamage:Has(owner, "runesmith_twin") ? 5 : Number(scheme, "turret_damage"); if (Has(owner, "runesmith_heavy")) damage = damage * 1500 / 1000;
                SpawnProjectile(turret.Room, turret.Position, (target.Position - turret.Position).normalized, owner.ID, NextID("turret_shot"), damage, Distance(scheme, "projectile_speed_milli_tiles_per_second"), P2?0:Distance(scheme, "turret_range_milli_tiles"), false, 0,turret.SourceLife);
                turret.AttackAt = combatNow + Number(Kit(owner.Loadout)["primary"], "interval_ms"); Effect("turret_shot", owner.ID, turret.Room, turret.Position);
            }
        }

        private void TickEnemy(Enemy enemy){var previous=v7MovingBody;try{if(V7){V7Facing(enemy);v7MovingBody=Body(enemy);}TickEnemyCore(enemy);}finally{v7MovingBody=previous;}}
        private void TickEnemyCore(Enemy enemy)
        {
            if(V6&&TickV6CoverAI(enemy))return;
            if(V5&&(enemy.Kind=="boss"||enemy.Kind=="archer")){TickV5Enemy(enemy);return;}
            if(P2&&enemy.ReadyAt==long.MaxValue)return;
            if(Weapons&&now<enemy.SuppressedUntil){enemy.StuckAt=combatNow;return;}
            var target = P2?P21EnemyTarget(enemy):players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room).OrderBy(item => Vector2.Distance(item.Position, enemy.Position)).ThenBy(item => item.Slot).FirstOrDefault();
            if (target == null) { enemy.StuckAt = combatNow;if(P2){enemy.ReservedShots=0;enemy.Warning=null;enemy.WarningUntil=0;} return; }
            var definition = enemyDefinitions[enemy.Kind];
            if (enemy.ChargeUntil > combatNow)
            {
                enemy.StuckAt = combatNow;
                var before = enemy.Position; enemy.Position = Move(Layout(enemy.Room), enemy.Position, enemy.Aim * Distance(definition, "charge_speed_milli_tiles_per_second", 6000) / 30, Radius(enemy));
                foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room)) if (V7?V7ChargeStrike(enemy,player):Vector2.Distance(player.Position, enemy.Position) <= Radius(enemy) + PlayerHurtRadius) Hurt(player, Number(definition, "damage"),enemy.AttackAction);
                if (Vector2.Distance(before, enemy.Position) < 0.01f) enemy.ChargeUntil = combatNow;
                return;
            }
            if (enemy.WarningUntil > 0)
            {
                enemy.StuckAt = combatNow;
                if (combatNow < enemy.WarningUntil) {if(P2)PrepareEnemyMotion(enemy,target);return;}
                ExecuteEnemyAttack(enemy, definition); enemy.WarningUntil = 0; enemy.Warning = null;
                enemy.AttackAt = Math.Max(enemy.AttackAt, combatNow + Number(definition, "recovery_ms", enemy.Kind == "boss" ? 900 : 500)); return;
            }
            var keep = enemy.Kind == "archer" ? 5f : enemy.Kind == "boss" ? 4f : Distance(definition, "attack_range_milli_tiles", 1200);
            var distance = Vector2.Distance(enemy.Position, target.Position);
            if (distance > keep || !ClearLine(Layout(enemy.Room), enemy.Position, target.Position))
            {
                var direction = P2?EnemyPath(enemy,target.Position):PathDirection(Layout(enemy.Room), enemy.Position, target.Position, Radius(enemy));
                if(P2)direction=SeparateEnemy(enemy,direction);
                var step=direction * Distance(definition, "move_milli_tiles_per_second") * (enemy.SlowUntil > combatNow ? enemy.SlowFactor : 1) / 30;
                enemy.Position = V7?WalkV7Enemy(enemy,step):Move(Layout(enemy.Room), enemy.Position, step, Radius(enemy),point=>WeaponWindBlocks(enemy,point)||P2&&EnemyOverlap(enemy,point));
                if (Vector2.Distance(enemy.Position, enemy.StuckPosition) > 0.3f) { enemy.StuckPosition = enemy.Position; enemy.StuckAt = combatNow; }
                else if (combatNow - enemy.StuckAt >= 5000)
                {
                    if (enemy.RecoveryCount++ >= 2) { AbortTechnical(); return; }
                    var position = V7?V7SpawnPoint(enemy.Room,enemy.ID+":recovery",enemy.Kind,enemy.ID):SpawnPoint(enemy.Room, enemy.ID + ":recovery");
                    if (position.HasValue) { if(P2){ReserveSpawn(enemy.Room,enemy.Kind,position.Value,enemy.Summon,enemy.ID);enemy.ReadyAt=long.MaxValue;}else{enemy.Position = position.Value; enemy.StuckPosition = position.Value;} }
                    enemy.StuckAt = combatNow;
                }
            }
            else {enemy.StuckAt = combatNow;if(P2){var retreat=enemy.Kind=="archer"&&distance<keep-1?(enemy.Position-target.Position).normalized:Vector2.zero;enemy.Position=Move(Layout(enemy.Room),enemy.Position,SeparateEnemy(enemy,retreat)*Distance(definition,"move_milli_tiles_per_second")/30,Radius(enemy),point=>WeaponWindBlocks(enemy,point)||EnemyOverlap(enemy,point));}}
            if (combatNow < enemy.AttackAt || P2&&combatNow<enemy.ReadyAt || !ClearLine(Layout(enemy.Room), enemy.Position, target.Position)) return;
            string kind;
            if (enemy.Kind == "boss")
            {
                var pattern = (JArray)definition[enemy.Health * 100 <= enemy.Maximum * Number(definition, "phase_2_threshold_percent") ? "phase_2" : "phase_1"];
                kind = (string)pattern[enemy.Pattern++ % pattern.Count];
                if (kind == "summon") { if(combatNow-enemy.SummonAt>=Number(definition,"summon_interval_ms")){Summon(enemy, definition);enemy.SummonAt=combatNow;} enemy.AttackAt = combatNow + 500; return; }
            }
            else if (enemy.Kind == "elite") { var pattern = (JArray)definition["phase_pattern"]; kind = (string)pattern[enemy.Pattern++ % pattern.Count]; }
            else kind = enemy.Kind == "archer" ? "shot" : enemy.Kind == "charger" ? "charge" : "melee";
            if (kind == "melee" && distance > keep + 0.4f) return;
            if(P2){var startRange=kind=="shot"?8f:kind=="fan"?12f:kind=="stomp"?Distance(definition,"stomp_radius_milli_tiles")+0.3f:kind=="charge"?8f:keep+0.4f;if(distance>startRange){enemy.Pattern=Math.Max(0,enemy.Pattern-1);return;}var shots=kind=="fan"?((JArray)definition["fan_angles_deg"]).Count:kind=="shot"?1:0;if(ProjectileLoad+shots>Number(config["authority"],"max_projectiles")){enemy.Pattern=Math.Max(0,enemy.Pattern-1);return;}enemy.ReservedShots=shots;}
            if(Weapons)enemy.AttackAction=NextID("enemy_attack");
            enemy.Warning = kind;enemy.TargetID=target.ID; enemy.Origin = enemy.Position; enemy.Aim = (Center(target)-Center(enemy)).normalized;
            enemy.WarningUntil = combatNow + (kind=="charge"&&enemy.Kind=="elite"?Number(definition["charge"],"telegraph_ms"):Number(definition, kind == "fan" ? "fan_telegraph_ms" : kind == "stomp" ? "stomp_telegraph_ms" : "telegraph_ms", 750));
            enemy.AttackAt = enemy.WarningUntil + Number(definition, kind == "fan" ? "fan_interval_ms" : kind == "stomp" ? "stomp_interval_ms" : "attack_interval_ms", 1000);
            Effect("warning", enemy.ID, enemy.Room, enemy.Origin);
        }

        private void ExecuteEnemyAttack(Enemy enemy, JObject definition)
        {
            if(V5)enemy.PresentationAttackAt=combatNow;
            var kind = enemy.Warning;
            if (kind == "charge") { var charge=enemy.Kind=="elite"?definition["charge"]:definition; enemy.ChargeUntil=combatNow+Number(charge,enemy.Kind=="elite"?"duration_ms":"charge_duration_ms",600); enemy.AttackAt=enemy.ChargeUntil+Number(charge,"recovery_ms",1000); return; }
            if (kind == "fan" || kind == "shot")
            {
                var angles = kind == "fan" ? ((JArray)definition["fan_angles_deg"]).Select(item => (float)item).ToArray() : new[] { 0f };
                foreach (var angle in angles) {if(P2)enemy.ReservedShots=Math.Max(0,enemy.ReservedShots-1);SpawnProjectile(enemy.Room, enemy.Origin, Rotate(enemy.Aim, angle), enemy.ID, Weapons?enemy.AttackAction:NextID("enemy_attack"), Number(definition, kind == "fan" ? "fan_damage" : "damage"), Distance(definition, "projectile_speed_milli_tiles_per_second"), P2?0:Distance(definition, "range_milli_tiles", 12000), true, 0);}
                return;
            }
            var radius = kind == "stomp" ? Distance(definition, "stomp_radius_milli_tiles") : Distance(definition, "attack_range_milli_tiles");
            foreach(var cover in covers.Where(item=>item.Room==enemy.Room&&CoverSolid(item)).ToArray())if(InArc(enemy.Origin,cover.Position,enemy.Aim,radius+0.45f,kind=="stomp"?360:120))DamageCover(cover,Number(definition,kind=="stomp"?"stomp_damage":"damage"));
            foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == enemy.Room))
            {
                var offset = player.Position - enemy.Origin;
                if (V7?V7EnemyStrike(enemy,player,enemy.Origin,radius,kind=="stomp"?360:120):offset.magnitude <= radius + PlayerHurtRadius && (kind == "stomp" || Vector2.Dot(offset.normalized, enemy.Aim) >= Mathf.Cos(60*Mathf.Deg2Rad)) && ClearLine(Layout(enemy.Room), enemy.Origin, player.Position)) Hurt(player, Number(definition, kind == "stomp" ? "stomp_damage" : "damage"),enemy.AttackAction);
            }
        }

        private void Summon(Enemy boss, JObject definition)
        {
            var amount = Math.Min(Number(definition, "summon_per_wave"), Math.Min(Number(definition, "summon_total_limit") - boss.Summoned, Number(definition, "summon_alive_limit") - enemies.Count(item => item.Health > 0 && item.Summon)));
            for (var index = 0; index < amount; index++)
            {
                if (EnemyLoad >= Number(config["authority"], "max_alive_enemies")) break;
                var point = SpawnPoint(boss.Room, "summon_" + boss.Summoned);
                if (!point.HasValue) break;
                if(P2)ReserveSpawn(boss.Room,(string)definition["summon_id"],point.Value,true);else SpawnEnemy(boss.Room, (string)definition["summon_id"], point.Value, true); boss.Summoned++;
            }
            Effect("summon", boss.ID, boss.Room, boss.Position);
        }

        private void TickTraps()
        {
            if(V6){TickV6Traps();return;}
            foreach (var trap in traps.Where(item => rooms[item.Room].Status == "active" || P2 && rooms[item.Room].Status == "cleared"))
            {
                var settings = extension["trap"];
                var triggering = players.Values.Any(item => !item.Left && item.Health > 0 && item.Room == trap.Room && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli")) || enemies.Any(item => item.Health > 0 && item.Room == trap.Room && item.Kind != "elite" && item.Kind != "boss" && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli"));
                if (trap.Phase == "armed" && triggering) { trap.Trigger++;trap.Phase = "warning"; trap.Until = combatNow + Number(settings, "warning_ms"); trap.Hit.Clear(); Effect("trap_warning", trap.ID, trap.Room, trap.Position); }
                else if (trap.Phase == "warning" && combatNow >= trap.Until) { trap.Phase = "active"; trap.Until = combatNow + Number(settings, "active_ms"); }
                else if (trap.Phase == "active")
                {
                    foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && item.Room == trap.Room && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli") + 0.3f)) if (trap.Hit.Add(player.ID + ":" + player.Deaths)) Hurt(player, Number(settings, "player_damage"),trap.ID+":"+trap.Trigger);
                    foreach (var enemy in enemies.Where(item => item.Health > 0 && item.Room == trap.Room && item.Kind != "elite" && item.Kind != "boss" && Vector2.Distance(item.Position, trap.Position) <= Distance(settings, "radius_milli") + Radius(item))) if (trap.Hit.Add(enemy.ID)) { enemy.Health = Math.Max(0, enemy.Health - Number(settings, "enemy_damage")); Effect("environment_hit", trap.ID, trap.Room, enemy.Position, Number(settings, "enemy_damage"), enemy.ID); }
                    if (combatNow >= trap.Until) { trap.Phase = "cooldown"; trap.Until = combatNow + Number(settings, "cooldown_ms"); }
                }
                else if (trap.Phase == "cooldown" && combatNow >= trap.Until) trap.Phase = "armed";
            }
        }

        private void TickRooms()
        {
            foreach (var room in rooms.Where(item => item.Exists && item.Index > 0 && !item.Connector).OrderBy(item=>P4?(item.Index+(int)(WorldTick%rooms.Length))%rooms.Length:item.Index))
            {
                if (room.Status == "unvisited" && players.Values.Any(item => !item.Left && item.Health > 0 && item.Connected && item.Room == room.Index)) { room.Status = "warning"; room.WarningAt = combatNow + (room.Index == 5 ? Number(parameters["clocks"], "boss_warning_ms") : Number(extension["encounter"], "room_warning_ms")); Effect("room_warning", null, room.Index, Vector2.zero); }
                if (room.Status == "warning" && combatNow >= room.WarningAt) { room.Status = "active"; QueueWave(room); }
                if (room.Status != "active") continue;
                if (room.NextWaveAt > 0 && combatNow >= room.NextWaveAt) { room.NextWaveAt = 0; QueueWave(room); }
                var remaining = (V5&&room.Index==5?13:roomLimit) - enemies.Count(item => item.Health > 0 && item.Room == room.Index)-spawnReservations.Count(item=>item.Room==room.Index);
                if (remaining > 0 && room.Pending.Count > 0 && combatNow>=room.SpawnRetryAt && EnemyLoad < Number(config["authority"], "max_alive_enemies"))
                {
                    var point = SpawnPoint(room.Index, "wave_" + room.Wave + "_" + room.Pending.Count);
                    if (point.HasValue) {var kind=room.Pending.Dequeue();if(P2)ReserveSpawn(room.Index,kind,point.Value);else SpawnEnemy(room.Index,kind,point.Value); room.SpawnBlockedAt = 0; }
                    else if (room.SpawnBlockedAt == 0) {room.SpawnBlockedAt=combatNow;room.SpawnRetryAt=combatNow+250;}
                    else if (combatNow - room.SpawnBlockedAt > Number(extension["encounter"], "room_spawn_block_timeout_ms")) {if(room.SpawnRecoveries++>=2){AbortTechnical();return;}room.SpawnBlockedAt=combatNow;room.SpawnRetryAt=combatNow+250;}
                }
            }
        }

        private void QueueWave(Room room)
        {
            if(V5){QueueV5Wave(room);return;}
            if(P2){room.GroupIDs.Clear();room.GroupUntil=combatNow+Number(parameters["ai"],"group_duration_ms",3000);}
            if (room.Index == 5) { if (room.Wave++ == 0) room.Pending.Enqueue("boss"); return; }
            if (room.Index >= 6) { if (room.Wave++ == 0) for (var index = 0; index < ((P4?P4Number("branch_enemy_count",6):4) * countScale + 999) / 1000; index++) room.Pending.Enqueue(P4?(index%6<4?"guard":"archer"):index%3==0?"archer":"guard"); return; }
            var waves = (JArray)config["rooms"][room.Index]["waves"]; if (room.Wave >= waves.Count) return;
            var wave = waves[room.Wave++]; var count = (Number(wave, "count") * countScale + 999) / 1000;
            var mix = (JArray)wave["mix"]; var weights = ((JArray)wave["weights"]).Select(item => (int)item).ToArray(); if(P4&&room.Index!=4){var variant=Convert.ToInt32(Rank("mix_"+room.Index+"_"+room.Wave,"weights").Substring(0,4),16)%2;weights=variant==0?new[]{6,3,1}:new[]{5,3,2};}var total = weights.Sum();
            var cycle=new List<string>();for(var index=0;index<mix.Count;index++)for(var amount=0;amount<weights[index];amount++)cycle.Add((string)mix[index]);
            var kinds=Enumerable.Range(0,count).Select(index=>cycle[index%total]).ToList();
            foreach (var kind in kinds.Select((kind, index) => new { Kind = kind, Rank = Rank("wave_" + room.Index + "_" + room.Wave, index.ToString()) }).OrderBy(item => item.Rank, StringComparer.Ordinal)) room.Pending.Enqueue(kind.Kind);
            if (room.Index == 4 && !room.EliteQueued && (!P4||room.Wave==2)) { room.Pending.Enqueue("elite"); room.EliteQueued = true; }
        }

        private Vector2? SpawnPoint(int room, string decision)
        {
            if(P2)return P2SpawnPoint(room,decision);
            var layout = Layout(room); var points = new List<Vector2>();
            for (var horizontal = 4; horizontal <= 14; horizontal++) for (var vertical = 2; vertical <= 8; vertical++)
            {
                var point = new Vector2(horizontal, vertical);
                if (Blocked(layout, point, 0.9f) || players.Values.Any(item => !item.Left && item.Health > 0 && item.Room == room && Vector2.Distance(item.Position, point) < 2) || enemies.Any(item => item.Health > 0 && item.Room == room && Vector2.Distance(item.Position, point) < Radius(item) + 0.6f) || traps.Any(item => item.Room == room && Vector2.Distance(item.Position, point) < 1.1f) || layout["objective"] is JArray && Vector2.Distance(RoomGeometry.Point(layout["objective"]), point) < 1.2f) continue;
                if(PathDirection(layout,RoomGeometry.Point(layout["spawn"]),point,0.9f).sqrMagnitude<0.01f && Vector2.Distance(point,RoomGeometry.Point(layout["spawn"]))>0.1f)continue;
                points.Add(point);
            }
            return points.Count == 0 ? (Vector2?)null : points.OrderBy(point => Rank(decision, point.x + ":" + point.y), StringComparer.Ordinal).First();
        }

        private void SpawnEnemy(int room, string kind, Vector2 position, bool summon = false)
        {
            var health = (Number(enemyDefinitions[kind], "max_health") * healthScale + 999) / 1000;
            enemies.Add(new Enemy { ID = kind == "boss" ? "boss" : NextID(V5&&summon?"summon_"+kind:kind), Kind = kind, Room = room, Position = position, TickStart=position, Origin = position, Health = health, Maximum = health, Summon = summon, ReadyAt=combatNow+(kind=="boss"||kind=="elite"?800:500), AttackAt = combatNow + Number(extension["encounter"], "wave_start_warning_ms")+Convert.ToInt32(Rank("attack_phase",kind+":"+serial).Substring(0,4),16)%500, StuckAt = combatNow, StuckPosition = position }); Effect("spawn", kind, room, position);
            if(P2&&kind!="boss"&&kind!="elite"&&rooms[room].GroupIDs.Count<3)rooms[room].GroupIDs.Add(enemies.Last().ID);
        }

        private void TickInteraction(Player player)
        {
            if(P3&&TickP3Rescue(player))return;
            if(TickP4Supply(player))return;
            if (player.Room != 2 && player.Room != 3) { player.InteractionTarget = null; return; }
            var room = rooms[player.Room]; var target = player.Room == 2 ? "hub_a" : "hub_b";
            if (room.Mechanism || room.Status != "active" || player.Left || !player.Connected || player.Health <= 0 || now < player.ProtectedUntil || now < player.DodgeUntil || !player.Input.Interact || player.Input.Attack || player.Charging || player.Input.Melee || player.Input.Move.sqrMagnitude > 0 || Vector2.Distance(player.Position, RoomGeometry.Point(Layout(player.Room)["objective"])) > Distance(config["player"], "interaction_radius_milli_tiles")) { player.InteractionTarget = null; return; }
            if (players.Values.Any(item => item.ID != player.ID && item.InteractionTarget == target)) { player.InteractionTarget = null; return; }
            if (player.InteractionTarget != target) { player.InteractionTarget = target; player.InteractionAt = now; }
            if (now - player.InteractionAt < Number(config["player"], "interaction_ms")) return;
            room.Mechanism = true; player.Interactions++;
            var fact = Fact("interact", player.ID, target, null, 0);
            fact["contributors"] = new JArray(players.Values.Where(item => !item.Left && item.ID != player.ID && item.Room == player.Room && defense.TryGetValue(player.Room + ":" + item.ID, out var evidence) && combatNow - evidence <= Number(config["player"], "defense_window_ms")).Select(item => item.ID));
            if(P3)P3Defenders(fact,player);
            facts.Add(fact); player.InteractionTarget = null; Effect("interaction", player.ID, player.Room, RoomGeometry.Point(Layout(player.Room)["objective"]));
        }

        private void FinishRooms()
        {
            foreach (var room in rooms.Where(item => item.Status == "active" && !item.Connector))
            {
                if (room.Pending.Count > 0 || spawnReservations.Any(item=>item.Room==room.Index) || enemies.Any(item => item.Health > 0 && item.Room == room.Index)) continue;
                var totalWaves = room.Index >= 5 ? 1 : ((JArray)config["rooms"][room.Index]["waves"]).Count;
                if (room.Wave < totalWaves) { if (room.NextWaveAt == 0) room.NextWaveAt = combatNow + 1; continue; }
                if ((room.Index == 2 || room.Index == 3) && !room.Mechanism) continue;
                room.Status = "cleared"; Effect("room_clear", null, room.Index, Vector2.zero);
                if(P2){projectiles.RemoveAll(item=>item.Room==room.Index);turrets.RemoveAll(item=>item.Room==room.Index);}
                if (P4 && room.Index == 6) foreach (var member in players.Values.Where(item => !item.Left)) if (member.Claims.Add("rune_cache")) member.PendingShield = P4Number("branch_shield",15);
            }
            if (rooms[5].Status == "cleared" || players.Values.Where(item => !item.Left).All(item => item.Health <= 0) && reinforcementRemaining <= 0 && !players.Values.Any(item => item.Reinforcement != null)) return;
        }

        public string[] Candidates(string id)
        {
            if(P4)return players.TryGetValue(id,out var participant)&&!participant.Left?PendingChoice(participant)?.Options??Array.Empty<string>():Array.Empty<string>();
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
                if (!player.Choices.TryGetValue(safeNode, out var option)) { var candidates = Candidates(player.ID); if (candidates.Length != 3) { AbortTechnical(); return; } option = candidates[0]; player.Choices[safeNode] = option; }
                if (!player.Upgrades.Contains(option)) player.Upgrades.Add(option);
            }
            safeNode = -1; playerInputsClear(); Effect("choice_end", null, 0, Vector2.zero);
        }

        private bool Wants(string id, string kind, string target) => requirements.TryGetValue(id, out var remaining) && (long?)remaining[kind + ":" + target] > 0;
        private static float Distance(JToken token, string key, int fallback) => Number(token, key, fallback) / 1000f;
    }
}
