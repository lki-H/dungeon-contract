using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public struct GrayboxInput
    {
        public Vector2 Move;
        public ushort Aim;
        public bool Attack;
        public bool Interact;
        public bool AttackPressed;
        public uint Sequence;
        public Vector2 Placement;
        public bool HasPlacement;
    }

    public sealed class GrayboxWorld
    {
        private readonly QualityWorld quality;
        public bool IsQuality => quality != null;
        public string ActionReason => quality?.LastActionReason ?? "accepted";
        public void AdvanceTime(long milliseconds) => quality?.AdvanceTime(milliseconds);
        public void FreezeTime(long milliseconds) => quality?.FreezeTime(milliseconds);
        public bool Action(string id, string action, GrayboxInput intent) { if (quality != null) return quality.Action(id, action, intent); Input(id, intent); return Action(id, action); }
        private sealed class Player
        {
            public string ID;
            public int Slot;
            public string Loadout;
            public Vector2 Position;
            public Vector2 Direction = Vector2.right;
            public GrayboxInput Input;
            public bool Connected = true;
            public bool Left;
            public int Health;
            public int Deaths;
            public long Damage;
            public int Kills;
            public int Interactions;
            public int Reaches;
            public long AttackAt;
            public long AbilityAt;
            public long DodgeAt;
            public long DodgeUntil;
            public long InvulnerableUntil;
            public long ProtectedUntil;
            public long InputAt;
            public string InteractionTarget;
            public long InteractionStarted;
            public string Reinforcement;
            public long RespawnAt;
            public bool SpawnAwaitingAck;
            public bool SupplyUsed;
            public bool ReachedInspect;
        }

        private sealed class Enemy
        {
            public string ID;
            public string Kind;
            public Vector2 Position;
            public Vector2 Aim;
            public Vector2 Origin;
            public int Room;
            public int Health;
            public int Maximum;
            public long AttackAt;
            public long WarningUntil;
            public long ChargeUntil;
            public long FanAt;
            public long StompAt;
            public long SummonAt;
            public int Summoned;
            public string WarningKind;
            public bool Summon;
            public bool Alive => Health > 0;
        }

        private sealed class Projectile
        {
            public string ID;
            public string Action;
            public string Owner;
            public Vector2 Position;
            public Vector2 Direction;
            public float Speed;
            public float Remaining;
            public int Damage;
            public bool Enemy;
            public long Until;
        }

        private sealed class Turret
        {
            public string ID;
            public string Owner;
            public Vector2 Position;
            public long Until;
            public long AttackAt;
        }

        private sealed class VectorConverter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer) { writer.WriteStartObject(); writer.WritePropertyName("x"); writer.WriteValue(value.x); writer.WritePropertyName("y"); writer.WriteValue(value.y); writer.WriteEndObject(); }
            public override Vector2 ReadJson(JsonReader reader, Type type, Vector2 value, bool existing, JsonSerializer serializer) { var point = JObject.Load(reader); return new Vector2((float)point["x"], (float)point["y"]); }
        }

        private readonly JObject config;
        private readonly JObject extension;
        private readonly Dictionary<string, Player> players = new Dictionary<string, Player>();
        private readonly Dictionary<string, JObject> definitions;
        private readonly Dictionary<string, JObject> loadouts;
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly List<Turret> turrets = new List<Turret>();
        private readonly List<JObject> facts = new List<JObject>();
        private readonly Dictionary<string, JObject> requirements = new Dictionary<string, JObject>();
        private readonly HashSet<string> sampledDamageActions = new HashSet<string>();
        private readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new VectorConverter() } });
        private int[] waveIndices;
        private bool[] entered;
        private bool[] completed;
        private long now;
        private long nextWaveAt;
        private long bossWakeAt;
        private long serial;
        private int maximumRoom = 1;
        private int reinforcementRemaining;
        private readonly int healthScale;
        private readonly int countScale;
        private string terminalReason;
        private readonly HashSet<string> aliveAtTick = new HashSet<string>();

        public string RunID { get; }
        private string outcome;
        public string Outcome { get => quality?.Outcome ?? outcome; private set => outcome = value; }
        public bool Terminal => Outcome != null;
        private long worldTick;
        public long WorldTick { get => quality?.WorldTick ?? worldTick; private set => worldTick = value; }
        public int MaximumRoom => quality?.MaximumRoom ?? maximumRoom;

        public GrayboxWorld(JObject manifest)
        {
            RunID = (string)manifest["run_id"];
            if ((int?)manifest["config"]["protocol_version"] == 2) { quality = new QualityWorld(manifest); return; }
            config = (JObject)manifest["config"].DeepClone();
            extension = (JObject)manifest["p1_world"].DeepClone();
            definitions = ((JArray)config["enemies"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            loadouts = ((JArray)config["loadouts"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            waveIndices = new int[((JArray)config["rooms"]).Count];
            entered = new bool[waveIndices.Length];
            completed = new bool[waveIndices.Length];
            completed[0] = true; entered[0] = true;
            reinforcementRemaining = Number(config["operation"], "reinforcements");
            var scaling = ((JArray)config["scaling"]).First(item => (int)item["players"] == ((JArray)manifest["members"]).Count);
            healthScale = Number(scaling, "health_milli"); countScale = Number(scaling, "count_milli");
            foreach (var member in (JArray)manifest["members"])
            {
                var slot = (int)member["slot"];
                var player = new Player { ID = (string)member["player_id"], Slot = slot, Loadout = (string)member["loadout_id"], Position = new Vector2(3 + slot * 0.7f, 6), Health = Number(config["player"], "max_health") };
                players.Add(player.ID, player);
                requirements[player.ID] = manifest["task_requirements"]?[player.ID] as JObject ?? new JObject();
            }
        }

        public void Input(string id, GrayboxInput input)
        {
            if (quality != null) { quality.Input(id, input); return; }
            if (!players.TryGetValue(id, out var player) || player.Left || !player.Connected) return;
            player.Input = input; player.Input.Move = Vector2.ClampMagnitude(input.Move, 1); player.InputAt = now;
        }

        public void Presence(string id, bool connected)
        {
            if (quality != null) { quality.Presence(id, connected); return; }
            if (!players.TryGetValue(id, out var player)) return;
            player.Connected = connected; player.Input = default; player.InteractionTarget = null;
        }

        public void Leave(string id)
        {
            if (quality != null) { quality.Leave(id); return; }
            if (!players.TryGetValue(id, out var player)) return;
            player.Left = true; player.Input = default; player.InteractionTarget = null;
            projectiles.RemoveAll(item => item.Owner == id); turrets.RemoveAll(item => item.Owner == id);
        }

        public bool Action(string id, string action)
        {
            if (quality != null) return quality.Action(id, action);
            if (!players.TryGetValue(id, out var player) || player.Left || !player.Connected || player.Health <= 0 || now < player.ProtectedUntil) return false;
            var settings = config["player"]; var scheme = loadouts[player.Loadout];
            if (action == "dodge" && now >= player.DodgeAt)
            {
                player.InteractionTarget = null; player.DodgeAt = now + Number(settings, "dodge_cooldown_ms"); player.DodgeUntil = now + Number(settings, "dodge_duration_ms"); player.InvulnerableUntil = now + Number(settings, "dodge_invulnerable_ms");
                if (player.Input.Move.sqrMagnitude > 0.01f) player.Direction = player.Input.Move.normalized;
                return true;
            }
            if (action == "ability" && now >= player.AbilityAt)
            {
                player.InteractionTarget = null;
                if (player.Loadout == "runesmith")
                {
                    var point = player.Position + Aim(player.Input.Aim) * 1.2f;
                    if (Blocked(point, Distance(config["player"], "collision_radius_milli_tiles"))) return false;
                    var maximum = Number(scheme, "turret_max"); var owned = turrets.Where(item => item.Owner == id).OrderBy(item => item.Until).ToList();
                    while (owned.Count >= maximum) { turrets.Remove(owned[0]); owned.RemoveAt(0); }
                    turrets.Add(new Turret { ID = NextID("turret"), Owner = id, Position = point, Until = now + Number(scheme, "turret_duration_ms"), AttackAt = now });
                }
                else
                {
                    var actionID = NextID("volley");
                    foreach (var angle in (JArray)scheme["ability_angles_deg"]) SpawnProjectile(player.Position, Rotate(Aim(player.Input.Aim), (float)angle), Number(scheme, "ability_damage"), Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "range_milli_tiles"), id, actionID, false);
                }
                player.AbilityAt = now + Number(scheme, "ability_cooldown_ms"); return true;
            }
            if (action == "supply" && !player.SupplyUsed && maximumRoom >= 3 && player.Health < Number(settings, "max_health") && Vector2.Distance(player.Position, SupplyPosition()) <= Distance(settings, "interaction_radius_milli_tiles"))
            {
                player.Health = Math.Min(Number(settings, "max_health"), player.Health + Number(settings, "supply_health")); player.SupplyUsed = true; return true;
            }
            return false;
        }

        public List<JObject> Tick(long elapsedMilliseconds)
        {
            if (quality != null) return quality.Tick(elapsedMilliseconds);
            if (Terminal) return new List<JObject>();
            now = elapsedMilliseconds; facts.Clear(); WorldTick++;
            if (now >= Number(config["operation"], "hard_deadline_ms")) { SetTerminal("failed", "timeout"); return new List<JObject>(); }
            aliveAtTick.Clear(); foreach (var player in players.Values) if (!player.Left && player.Health > 0) aliveAtTick.Add(player.ID);
            var delta = 1f / Number(config["authority"], "tick_hz");
            foreach (var player in players.Values.OrderBy(item => item.Slot)) TickPlayer(player, delta);
            TickRooms();
            foreach (var enemy in enemies.Where(item => item.Alive).ToArray()) TickEnemy(enemy, delta);
            TickTurrets(); TickProjectiles(delta);
            foreach (var player in players.Values.OrderBy(item => item.Slot)) TickInteraction(player);
            FinishRooms();
            foreach (var player in players.Values.Where(item => item.Health <= 0)) { projectiles.RemoveAll(item => !item.Enemy && item.Owner == player.ID); turrets.RemoveAll(item => item.Owner == player.ID); }
            if (completed[5]) SetTerminal("success", "objectives_complete");
            else if (now >= Number(config["operation"], "hard_deadline_ms")) SetTerminal("failed", "timeout");
            else if (players.Values.All(item => item.Left)) SetTerminal("failed", "team_abandoned");
            else if (players.Values.Where(item => !item.Left).All(item => item.Health <= 0) && !players.Values.Any(item => item.Reinforcement != null) && reinforcementRemaining <= 0) SetTerminal("failed", "team_wipe");
            enemies.RemoveAll(item => !item.Alive);
            var activeActions = new HashSet<string>(projectiles.Select(item => item.Action));
            sampledDamageActions.RemoveWhere(action => !activeActions.Contains(action));
            return facts.Select(item => (JObject)item.DeepClone()).ToList();
        }

        private void TickPlayer(Player player, float delta)
        {
            if (player.Left) return;
            if (player.Health <= 0)
            {
                if (player.Reinforcement != null && now >= player.RespawnAt && !player.SpawnAwaitingAck)
                {
                    player.Health = Number(config["player"], "max_health"); player.Position = SafePoint(); player.InvulnerableUntil = now + Number(config["authority"], "spawn_protection_ms"); player.ProtectedUntil = player.InvulnerableUntil; player.SpawnAwaitingAck = true;
                    facts.Add(Fact("reinforcement_spawned", player.ID, "safe_point", null, 0, player.Reinforcement));
                }
                return;
            }
            if (now - player.InputAt > Number(config["authority"], "input_timeout_ms")) { player.Input = default; player.InteractionTarget = null; }
            if (player.Input.Move.sqrMagnitude > 0.01f) player.Direction = player.Input.Move.normalized;
            if (now < player.DodgeUntil)
            {
                var distance = Distance(config["player"], "dodge_distance_milli_tiles") / (Number(config["player"], "dodge_duration_ms") / 1000f) * delta;
                player.Position = Move(player.Position, player.Direction * distance, Distance(config["player"], "collision_radius_milli_tiles"), true);
            }
            else player.Position = Move(player.Position, player.Input.Move * Distance(config["player"], "move_milli_tiles_per_second") * delta, Distance(config["player"], "collision_radius_milli_tiles"), true);
            if (player.Input.Attack && now >= player.AttackAt && now >= player.ProtectedUntil && player.InteractionTarget == null)
            {
                var scheme = loadouts[player.Loadout];
                SpawnProjectile(player.Position, Aim(player.Input.Aim), Number(scheme, "base_damage"), Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "range_milli_tiles"), player.ID, NextID("attack"), false);
                player.AttackAt = now + Number(scheme, "attack_interval_ms");
            }
            if (player.Position.x > 44 && player.Position.x < 47 && completed[2] && !player.ReachedInspect) { player.ReachedInspect = true; player.Reaches++; facts.Add(Fact("reach", player.ID, "inspect_zone", null, 0)); }
        }

        private void TickRooms()
        {
            for (var room = 1; room <= maximumRoom; room++) if (!entered[room] && players.Values.Any(item => !item.Left && item.Health > 0 && (int)(item.Position.x / 16) == room))
            {
                entered[room] = true;
                if (room == 5) bossWakeAt = now + Number(config["rooms"][room], "boss_warning_ms"); else SpawnRoomWave(room);
            }
            if (entered[5] && !completed[5] && bossWakeAt > 0 && now >= bossWakeAt) { bossWakeAt = 0; SpawnEnemy("boss", new Vector2(80 + (int)extension["encounter"]["boss_spawn"][0], (int)extension["encounter"]["boss_spawn"][1]), 5, false, "boss"); }
            if (entered[3] && !completed[3] && nextWaveAt > 0 && now >= nextWaveAt) { nextWaveAt = 0; SpawnRoomWave(3); }
        }

        private void SpawnRoomWave(int room)
        {
            var waves = (JArray)config["rooms"][room]["waves"];
            if (waveIndices[room] >= waves.Count) return;
            var wave = waves[waveIndices[room]++]; var count = (Number(wave, "count") * countScale + 999) / 1000;
            var mix = (JArray)wave["mix"]; var weights = ((JArray)wave["weights"]).Select(item => (int)item).ToArray(); var total = weights.Sum(); var amounts = weights.Select(weight => count * weight / total).ToArray();
            foreach (var index in Enumerable.Range(0, weights.Length).OrderByDescending(index => count * weights[index] % total).ThenBy(index => index).Take(count - amounts.Sum())) amounts[index]++;
            var cursor = 0;
            for (var kind = 0; kind < amounts.Length; kind++) for (var index = 0; index < amounts[kind]; index++) { SpawnEnemy((string)mix[kind], new Vector2(room * 16 + 7 + cursor % 6, 3 + cursor / 6 * 1.5f), room, false); cursor++; }
            if (room == 4) SpawnEnemy("elite", new Vector2(75, 6), room, false);
        }

        private void SpawnEnemy(string kind, Vector2 position, int room, bool summon, string exactID = null)
        {
            if (enemies.Count(item => item.Alive) >= Number(config["authority"], "max_alive_enemies")) throw new InvalidOperationException("Enemy budget exceeded");
            var definition = definitions[kind]; var health = (Number(definition, "max_health") * healthScale + 999) / 1000;
            enemies.Add(new Enemy { ID = exactID ?? NextID(kind), Kind = kind, Room = room, Position = position, Health = health, Maximum = health, Summon = summon, AttackAt = now + Number(extension["encounter"], "wave_start_warning_ms"), FanAt = now + Number(definition, "fan_interval_ms"), StompAt = now + Number(definition, "stomp_interval_ms"), SummonAt = now + Number(definition, "summon_interval_ms") });
        }

        private void TickEnemy(Enemy enemy, float delta)
        {
            var target = players.Values.Where(item => !item.Left && item.Health > 0 && (int)(item.Position.x / 16) == enemy.Room).OrderBy(item => Vector2.Distance(enemy.Position, item.Position)).FirstOrDefault();
            if (target == null) return;
            var definition = definitions[enemy.Kind]; var distance = Vector2.Distance(target.Position, enemy.Position);
            if (enemy.ChargeUntil > now)
            {
                enemy.Position = Move(enemy.Position, enemy.Aim * Distance(definition, "charge_speed_milli_tiles_per_second", 6000) * delta, Radius(enemy));
                if (distance < Radius(enemy) + Distance(config["player"], "collision_radius_milli_tiles") + 0.2f) Hurt(target, Number(definition, "damage"));
                return;
            }
            if (enemy.WarningUntil > 0)
            {
                if (now >= enemy.WarningUntil)
                {
                    var warning = enemy.WarningKind; enemy.WarningUntil = 0;
                    if (warning == "charge") { enemy.ChargeUntil = now + Number(definition, "charge_duration_ms", 600); enemy.AttackAt = enemy.ChargeUntil + Number(definition, "recovery_ms", 1000); }
                    else if (warning == "stomp") { foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0 && Vector2.Distance(item.Position, enemy.Origin) <= Distance(definition, "stomp_radius_milli_tiles"))) Hurt(player, Number(definition, "stomp_damage")); }
                    else if (warning == "fan") { foreach (var angle in (JArray)definition["fan_angles_deg"]) SpawnProjectile(enemy.Position, Rotate(enemy.Aim, (float)angle), Number(definition, "fan_damage"), Distance(definition, "projectile_speed_milli_tiles_per_second"), 12, enemy.ID, NextID("enemy_fan"), true); }
                    else if (warning == "shot") SpawnProjectile(enemy.Position, enemy.Aim, Number(definition, "damage"), Distance(definition, "projectile_speed_milli_tiles_per_second"), Distance(definition, "range_milli_tiles"), enemy.ID, NextID("enemy_arrow"), true);
                    else if (Vector2.Distance(enemy.Position, target.Position) <= Distance(definition, "attack_range_milli_tiles") + 0.4f) Hurt(target, Number(definition, "damage"));
                }
                return;
            }
            if (enemy.Kind == "boss")
            {
                if (now >= enemy.StompAt) { Warn(enemy, target, "stomp", Number(definition, "stomp_telegraph_ms")); enemy.StompAt = now + Number(definition, "stomp_interval_ms"); }
                else if (now >= enemy.FanAt) { Warn(enemy, target, "fan", Number(definition, "fan_telegraph_ms")); enemy.FanAt = now + Number(definition, "fan_interval_ms"); }
                if (enemy.Health * 100 <= enemy.Maximum * Number(definition, "phase_2_threshold_percent") && now >= enemy.SummonAt)
                {
                    enemy.SummonAt = now + Number(definition, "summon_interval_ms"); var available = Math.Min(Number(definition, "summon_per_wave"), Math.Min(Number(definition, "summon_total_limit") - enemy.Summoned, Number(definition, "summon_alive_limit") - enemies.Count(item => item.Alive && item.Summon)));
                    for (var index = 0; index < available; index++) { SpawnEnemy((string)definition["summon_id"], new Vector2(84 + index * 6, 8), 5, true); enemy.Summoned++; }
                }
                return;
            }
            var keepDistance = enemy.Kind == "archer" ? 5f : Distance(definition, "attack_range_milli_tiles", 1200);
            if (distance > keepDistance) enemy.Position = Move(enemy.Position, (target.Position - enemy.Position).normalized * Distance(definition, "move_milli_tiles_per_second") * delta, Radius(enemy));
            if (now >= enemy.AttackAt && (enemy.Kind == "charger" || enemy.Kind == "archer" || distance <= keepDistance + 0.5f))
            {
                var kind = enemy.Kind == "archer" ? "shot" : enemy.Kind == "charger" ? "charge" : enemy.Kind == "elite" && (WorldTick / 60) % 2 == 0 ? "charge" : "melee";
                Warn(enemy, target, kind, Number(definition, "telegraph_ms")); enemy.AttackAt = now + Number(definition, "attack_interval_ms");
            }
        }

        private void Warn(Enemy enemy, Player target, string kind, int milliseconds) { enemy.WarningKind = kind; enemy.WarningUntil = now + milliseconds; enemy.Aim = (target.Position - enemy.Position).normalized; enemy.Origin = enemy.Position; }

        private void TickTurrets()
        {
            turrets.RemoveAll(item => now >= item.Until || !players.TryGetValue(item.Owner, out var owner) || owner.Left || owner.Health <= 0);
            foreach (var turret in turrets)
            {
                var scheme = loadouts[players[turret.Owner].Loadout]; var target = enemies.Where(item => item.Alive && Vector2.Distance(item.Position, turret.Position) <= Distance(scheme, "turret_range_milli_tiles") && ClearLine(turret.Position, item.Position)).OrderBy(item => Vector2.Distance(item.Position, turret.Position)).FirstOrDefault();
                if (target != null && now >= turret.AttackAt) { SpawnProjectile(turret.Position, (target.Position - turret.Position).normalized, Number(scheme, "turret_damage"), Distance(scheme, "projectile_speed_milli_tiles_per_second"), Distance(scheme, "turret_range_milli_tiles"), turret.Owner, NextID("turret_shot"), false); turret.AttackAt = now + Number(scheme, "turret_attack_interval_ms"); }
            }
        }

        private void SpawnProjectile(Vector2 position, Vector2 direction, int damage, float speed, float range, string owner, string action, bool enemy)
        {
            if (projectiles.Count >= Number(config["authority"], "max_projectiles")) return;
            projectiles.Add(new Projectile { ID = action + "_" + serial++, Action = action, Owner = owner, Position = position, Direction = direction, Damage = damage, Speed = speed, Remaining = range, Enemy = enemy, Until = now + Number(extension["encounter"], "projectile_lifetime_ms") });
        }

        private void TickProjectiles(float delta)
        {
            foreach (var projectile in projectiles.ToArray())
            {
                if (!projectiles.Contains(projectile)) continue;
                if (projectile.Until > 0 && now >= projectile.Until) { projectiles.Remove(projectile); continue; }
                var start = projectile.Position; var distance = Math.Min(projectile.Speed * delta, projectile.Remaining); var step = Distance(extension["physics"], "sweep_step_milli"); var removed = false;
                for (var traveled = 0f; traveled < distance && !removed; traveled += step)
                {
                    var point = start + projectile.Direction * Math.Min(distance, traveled + step);
                    if (Blocked(point, Distance(extension["physics"], "projectile_radius_milli"))) { removed = true; break; }
                    projectile.Position = point;
                    if (projectile.Enemy)
                    {
                        var player = players.Values.FirstOrDefault(item => !item.Left && item.Health > 0 && Vector2.Distance(point, item.Position) < Distance(config["player"], "collision_radius_milli_tiles") + Distance(extension["physics"], "projectile_radius_milli"));
                        if (player != null) { Hurt(player, projectile.Damage); removed = true; }
                    }
                    else
                    {
                        var enemy = enemies.FirstOrDefault(item => item.Alive && Vector2.Distance(point, item.Position) < Radius(item) + Distance(extension["physics"], "projectile_radius_milli"));
                        if (enemy != null) { Hit(enemy, projectile.Owner, projectile.Action, projectile.Damage); removed = true; }
                    }
                }
                projectile.Remaining -= distance;
                if (removed || projectile.Remaining <= 0) projectiles.Remove(projectile);
            }
        }

        private void Hit(Enemy enemy, string ownerID, string action, int requested)
        {
            if (!players.TryGetValue(ownerID, out var owner) || owner.Left || !aliveAtTick.Contains(ownerID)) return;
            var damage = Math.Min(requested, enemy.Health); enemy.Health -= damage; owner.Damage += damage;
            if ((owner.Damage == damage || Wants(owner.ID, "damage", "")) && sampledDamageActions.Add(action)) facts.Add(Fact("damage", owner.ID, enemy.ID, action, damage));
            if (enemy.Health > 0) return;
            owner.Kills++;
            if (enemy.Kind == "boss" || !enemy.Summon && players.Values.Any(item => Wants(item.ID, "kill", ""))) facts.Add(Fact("kill", owner.ID, enemy.ID, action, 0, eligible: !enemy.Summon));
        }

        private void Hurt(Player player, int damage)
        {
            if (player.Health <= 0 || now < player.InvulnerableUntil) return;
            player.Health = Math.Max(0, player.Health - damage); player.InvulnerableUntil = now + Number(extension["physics"], "contact_protection_ms"); player.InteractionTarget = null;
            if (player.Health != 0) return;
            player.Deaths++; facts.Add(Fact("death", player.ID, "life_" + player.Deaths, null, 0));
        }

        private void TickInteraction(Player player)
        {
            var room = (int)(player.Position.x / 16);
            var target = room == 2 && !completed[2] && waveIndices[2] >= ((JArray)config["rooms"][2]["waves"]).Count && !enemies.Any(item => item.Alive && item.Room == 2) ? "hub_a" : room == 3 && !completed[3] && waveIndices[3] >= ((JArray)config["rooms"][3]["waves"]).Count && !enemies.Any(item => item.Alive && item.Room == 3) ? "hub_b" : null;
            if (!player.Connected || player.Left || player.Health <= 0 || now < player.ProtectedUntil || !player.Input.Interact || target == null || player.Input.Attack || player.Input.Move.sqrMagnitude > 0 || now < player.DodgeUntil || Vector2.Distance(player.Position, new Vector2(room * 16 + 8, 6)) > Distance(config["player"], "interaction_radius_milli_tiles")) { player.InteractionTarget = null; return; }
            if (player.InteractionTarget != target) { player.InteractionTarget = target; player.InteractionStarted = now; }
            if (now - player.InteractionStarted < Number(config["player"], "interaction_ms")) return;
            player.Interactions++; facts.Add(Fact("interact", player.ID, target, null, 0)); completed[room] = true; maximumRoom = Math.Max(maximumRoom, room + 1); player.InteractionTarget = null;
        }

        private void FinishRooms()
        {
            foreach (var room in new[] { 1, 4 }) if (entered[room] && !completed[room] && !enemies.Any(item => item.Alive && item.Room == room)) { completed[room] = true; maximumRoom = Math.Max(maximumRoom, room + 1); }
            if (entered[3] && !completed[3] && !enemies.Any(item => item.Alive && item.Room == 3) && waveIndices[3] < ((JArray)config["rooms"][3]["waves"]).Count && nextWaveAt == 0) nextWaveAt = now + Number(config["rooms"][3], "wave_gap_ms");
            if (entered[5] && bossWakeAt == 0 && enemies.Any(item => item.Kind == "boss" && !item.Alive)) completed[5] = true;
        }

        public void Acknowledge(JObject receipt)
        {
            if (quality != null) { quality.Acknowledge(receipt); return; }
            if (receipt["task_requirements"] is JObject updated) { requirements.Clear(); foreach (var property in updated.Properties()) requirements[property.Name] = (JObject)property.Value.DeepClone(); }
            reinforcementRemaining = Number(config["operation"], "reinforcements");
            if (receipt["reinforcements"] is JObject actions) foreach (var property in actions.Properties())
            {
                var action = property.Value; var id = (string)action["player_id"]; if (!players.TryGetValue(id, out var player)) continue; var status = (string)action["status"];
                if (status == "spawned" || status == "pending_spawn") reinforcementRemaining--;
                if (status == "pending_spawn" && player.Reinforcement == null && player.Health <= 0) { player.Reinforcement = property.Name; player.RespawnAt = now + Number(config["authority"], "spawn_delay_ms"); }
                if (status == "spawned" && player.Reinforcement == property.Name) { player.Reinforcement = null; player.SpawnAwaitingAck = false; }
            }
        }

        public JObject Summary() => quality?.Summary() ?? new JObject { ["participants"] = new JArray(players.Values.OrderBy(item => item.Slot).Select(item => new JObject { ["player_id"] = item.ID, ["slot"] = item.Slot, ["loadout_id"] = item.Loadout, ["damage"] = item.Damage, ["kills"] = item.Kills, ["assists"] = 0, ["interactions"] = item.Interactions, ["deaths"] = item.Deaths, ["choices"] = new JArray(), ["effective_contribution"] = item.Damage > 0 || item.Interactions > 0 || item.Reaches > 0 })), ["trial_eligible"] = false, ["ineligible_reason"] = "debug" };
        public JObject TerminalRequest(long frame, long sequence) => quality?.TerminalRequest(frame, sequence) ?? new JObject { ["outcome"] = Outcome, ["reason"] = terminalReason, ["cutoff_frame"] = frame, ["cutoff_sequence"] = sequence };

        public JObject Snapshot() => quality?.Snapshot() ?? new JObject
        {
            ["room"] = maximumRoom, ["elapsed_ms"] = now, ["phase"] = Outcome ?? "running", ["boss_warning_remaining_ms"] = Math.Max(0, bossWakeAt - now), ["reinforcement_remaining"] = reinforcementRemaining,
            ["players"] = new JArray(players.Values.OrderBy(item => item.Slot).Select(item => new JObject { ["player_id"] = item.ID, ["slot"] = item.Slot, ["loadout_id"] = item.Loadout, ["x"] = item.Position.x, ["y"] = item.Position.y, ["health"] = item.Health, ["deaths"] = item.Deaths, ["damage"] = item.Damage, ["connected"] = item.Connected, ["left"] = item.Left, ["dodge_remaining_ms"] = Math.Max(0, item.DodgeAt - now), ["ability_remaining_ms"] = Math.Max(0, item.AbilityAt - now), ["interaction_remaining_ms"] = item.InteractionTarget == null ? 0 : Math.Max(0, Number(config["player"], "interaction_ms") - (now - item.InteractionStarted)), ["supply_used"] = item.SupplyUsed })),
            ["enemies"] = new JArray(enemies.Where(item => item.Alive).Select(item => new JObject { ["id"] = item.ID, ["kind"] = item.Kind, ["x"] = item.Position.x, ["y"] = item.Position.y, ["health"] = item.Health, ["maximum"] = item.Maximum, ["warning"] = item.WarningUntil > now ? item.WarningKind : "", ["warning_x"] = item.Origin.x, ["warning_y"] = item.Origin.y, ["aim_x"] = item.Aim.x, ["aim_y"] = item.Aim.y })),
            ["projectiles"] = new JArray(projectiles.Select(item => new JObject { ["id"] = item.ID, ["x"] = item.Position.x, ["y"] = item.Position.y, ["enemy"] = item.Enemy })),
            ["turrets"] = new JArray(turrets.Select(item => new JObject { ["id"] = item.ID, ["x"] = item.Position.x, ["y"] = item.Position.y })), ["completed"] = new JArray(completed), ["wave"] = waveIndices[3]
        };

        public JObject Capture() => quality?.Capture() ?? new JObject { ["players"] = JArray.FromObject(players.Values, serializer), ["enemies"] = JArray.FromObject(enemies, serializer), ["projectiles"] = JArray.FromObject(projectiles, serializer), ["turrets"] = JArray.FromObject(turrets, serializer), ["waves"] = new JArray(waveIndices), ["entered"] = new JArray(entered), ["completed"] = new JArray(completed), ["now"] = now, ["tick"] = WorldTick, ["serial"] = serial, ["maximum_room"] = maximumRoom, ["wave_at"] = nextWaveAt, ["boss_at"] = bossWakeAt, ["budget"] = reinforcementRemaining, ["outcome"] = Outcome, ["reason"] = terminalReason, ["sampled"] = new JArray(sampledDamageActions), ["requirements"] = JObject.FromObject(requirements) };

        public void Restore(JObject checkpoint)
        {
            if (quality != null) { quality.Restore(checkpoint); return; }
            players.Clear(); foreach (var player in checkpoint["players"].ToObject<List<Player>>(serializer)) players.Add(player.ID, player);
            enemies.Clear(); enemies.AddRange(checkpoint["enemies"].ToObject<List<Enemy>>(serializer)); projectiles.Clear(); projectiles.AddRange(checkpoint["projectiles"].ToObject<List<Projectile>>(serializer)); turrets.Clear(); turrets.AddRange(checkpoint["turrets"].ToObject<List<Turret>>(serializer));
            waveIndices = checkpoint["waves"].ToObject<int[]>(); entered = checkpoint["entered"].ToObject<bool[]>(); completed = checkpoint["completed"].ToObject<bool[]>(); now = (long)checkpoint["now"]; WorldTick = (long)checkpoint["tick"]; serial = (long)checkpoint["serial"]; maximumRoom = (int)checkpoint["maximum_room"]; nextWaveAt = (long)checkpoint["wave_at"]; bossWakeAt = (long)checkpoint["boss_at"]; reinforcementRemaining = (int)checkpoint["budget"]; Outcome = (string)checkpoint["outcome"]; terminalReason = (string)checkpoint["reason"]; sampledDamageActions.Clear(); foreach (var action in (JArray)checkpoint["sampled"]) sampledDamageActions.Add((string)action);
            requirements.Clear(); foreach (var property in ((JObject)checkpoint["requirements"]).Properties()) requirements[property.Name] = (JObject)property.Value.DeepClone();
        }

        public bool Blocked(Vector2 point, float radius)
        {
            if (quality != null) return quality.Blocked(point, radius);
            if (point.x - radius < 1 || point.x + radius > 95 || point.y - radius < 1 || point.y + radius > 11) return true;
            var rows = (JArray)extension["encounter"]["door_rows"];
            for (var door = 16; door < 96; door += 16) if (point.x + radius > door && point.x - radius < door + 1 && (door / 16 > maximumRoom || point.y - radius < (int)rows[0] || point.y + radius > (int)rows[1] + 1)) return true;
            return false;
        }

        private Vector2 Move(Vector2 start, Vector2 delta, float radius, bool playerBody = false)
        {
            var steps = Math.Max(1, Mathf.CeilToInt(delta.magnitude / Distance(extension["physics"], "sweep_step_milli"))); var step = delta / steps;
            for (var index = 0; index < steps; index++) { var horizontal = start + new Vector2(step.x, 0); if (!Blocked(horizontal, radius) && (!playerBody || !EnemyBlocks(start, horizontal, radius))) start = horizontal; var vertical = start + new Vector2(0, step.y); if (!Blocked(vertical, radius) && (!playerBody || !EnemyBlocks(start, vertical, radius))) start = vertical; }
            return start;
        }

        private bool EnemyBlocks(Vector2 before, Vector2 next, float radius) => enemies.Any(enemy => enemy.Alive && Vector2.Distance(next, enemy.Position) < radius + Radius(enemy) && Vector2.Distance(next, enemy.Position) < Vector2.Distance(before, enemy.Position));

        private bool ClearLine(Vector2 start, Vector2 end) { var length = Vector2.Distance(start, end); for (var distance = 0f; distance < length; distance += Distance(extension["physics"], "sweep_step_milli")) if (Blocked(Vector2.Lerp(start, end, distance / length), 0.05f)) return false; return true; }
        private Vector2 SafePoint() => completed[4] ? new Vector2(67, 6) : completed[3] ? new Vector2(51, 6) : new Vector2(3, 6);
        private Vector2 SupplyPosition() => new Vector2(48 + (int)extension["encounter"]["supply_position"][0], (int)extension["encounter"]["supply_position"][1]);
        private float Radius(Enemy enemy) => Distance(extension["physics"], enemy.Kind == "boss" ? "boss_radius_milli" : enemy.Kind == "elite" ? "elite_radius_milli" : "enemy_radius_milli");
        private bool Wants(string id, string kind, string target) { if (!requirements.TryGetValue(id, out var player)) return false; return (long?)player[kind + ":" + target] > 0; }
        private string NextID(string prefix) => prefix + "_" + (++serial);
        private void SetTerminal(string value, string reason) { outcome = value; terminalReason = reason; }
        private static int Number(JToken token, string key, int fallback = 0) => (int?)token?[key] ?? fallback;
        private static float Distance(JToken token, string key, int fallback = 0) => Number(token, key, fallback) / 1000f;
        private static Vector2 Aim(ushort angle) { var radians = angle / 65535f * Mathf.PI * 2; return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)); }
        private static Vector2 Rotate(Vector2 direction, float degrees) { var radians = degrees * Mathf.Deg2Rad; return new Vector2(direction.x * Mathf.Cos(radians) - direction.y * Mathf.Sin(radians), direction.x * Mathf.Sin(radians) + direction.y * Mathf.Cos(radians)); }
        private static JObject Fact(string kind, string player, string target, string action, int amount, string reinforcement = null, bool eligible = true)
        {
            var value = new JObject { ["event_id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["actor_player_id"] = player, ["target_id"] = target, ["contributors"] = new JArray(), ["action_id"] = action, ["effective_amount"] = amount };
            if (kind == "kill") value["contract_kill_eligible"] = eligible;
            if (reinforcement != null) value["reinforcement_action_id"] = reinforcement;
            return value;
        }
    }
}
