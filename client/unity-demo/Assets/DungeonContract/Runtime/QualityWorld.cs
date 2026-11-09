using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class QualityWorld
    {
        private sealed class Player
        {
            public string ID;
            public int Slot;
            public string Loadout;
            public int Room;
            public Vector2 Position;
            public Vector2 Direction;
            public Vector2 DodgeDirection;
            public GrayboxInput Input;
            public uint Ack;
            public bool Connected = true;
            public bool Left;
            public int Health;
            public int Shield;
            public int PendingShield;
            public long ShieldUntil;
            public long Damage;
            public int Kills;
            public int Assists;
            public int Interactions;
            public int Deaths;
            public int Reaches;
            public long InputAt;
            public long AttackAt;
            public long AbilityAt;
            public long DodgeAt;
            public long DodgeUntil;
            public long InvulnerableUntil;
            public long ProtectedUntil;
            public long DoorAt;
            public long InteractionAt;
            public string InteractionTarget;
            public string Reinforcement;
            public long RespawnAt;
            public bool SpawnAwaitingAck;
            public string BufferedAction;
            public long BufferUntil;
            public readonly HashSet<string> Claims = new HashSet<string>();
            public readonly HashSet<string> Reached = new HashSet<string>();
            public readonly List<string> Upgrades = new List<string>();
            public readonly Dictionary<int, string> Choices = new Dictionary<int, string>();
        }

        private sealed class Room
        {
            public int Index;
            public bool Exists = true;
            public string Status = "unvisited";
            public long WarningAt;
            public long NextWaveAt;
            public int Wave;
            public bool Mechanism;
            public bool EliteQueued;
            public bool NodeOpened;
            public long SpawnBlockedAt;
            public readonly Queue<string> Pending = new Queue<string>();
        }

        private sealed class Enemy
        {
            public string ID;
            public string Kind;
            public int Room;
            public Vector2 Position;
            public Vector2 Origin;
            public Vector2 Aim;
            public int Health;
            public int Maximum;
            public bool Summon;
            public string Warning;
            public long WarningUntil;
            public long AttackAt;
            public long ChargeUntil;
            public long KnockbackAt;
            public long SlowUntil;
            public long StuckAt;
            public Vector2 StuckPosition;
            public int RecoveryCount;
            public int Pattern;
            public int Summoned;
            public readonly Dictionary<string, long> Damagers = new Dictionary<string, long>();
        }

        private sealed class Projectile
        {
            public string ID;
            public string Action;
            public string Owner;
            public int Room;
            public Vector2 Position;
            public Vector2 Direction;
            public float Remaining;
            public float Speed;
            public int Damage;
            public bool Enemy;
            public int Penetration;
            public readonly HashSet<string> Hit = new HashSet<string>();
        }

        private sealed class Turret { public string ID; public string Owner; public int Room; public Vector2 Position; public long Until; public long AttackAt; }
        private sealed class Trap { public string ID; public int Room; public Vector2 Position; public string Phase = "armed"; public long Until; public readonly HashSet<string> Hit = new HashSet<string>(); }
        private sealed class VectorConverter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer) { writer.WriteStartObject(); writer.WritePropertyName("x"); writer.WriteValue(value.x); writer.WritePropertyName("y"); writer.WriteValue(value.y); writer.WriteEndObject(); }
            public override Vector2 ReadJson(JsonReader reader, Type type, Vector2 value, bool existing, JsonSerializer serializer) { var point = JObject.Load(reader); return new Vector2((float)point["x"], (float)point["y"]); }
        }

        private readonly JObject manifest;
        private readonly JObject config;
        private readonly JObject extension;
        private readonly Dictionary<string, Player> players = new Dictionary<string, Player>();
        private readonly Dictionary<string, JObject> enemyDefinitions;
        private readonly Dictionary<string, JObject> schemes;
        private readonly Dictionary<string, JObject> upgrades;
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly List<Turret> turrets = new List<Turret>();
        private readonly List<Trap> traps = new List<Trap>();
        private readonly List<JObject> facts = new List<JObject>();
        private readonly Queue<JObject> effects = new Queue<JObject>();
        private readonly Dictionary<string, JObject> requirements = new Dictionary<string, JObject>();
        private readonly Dictionary<string, long> defense = new Dictionary<string, long>();
        private readonly HashSet<string> damageActions = new HashSet<string>();
        private readonly HashSet<string> aliveAtTick = new HashSet<string>();
        private readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new VectorConverter() } });
        private Room[] rooms;
        private long now;
        private long combatNow;
        private long lastTickAt;
        private long serial;
        private int reinforcementRemaining;
        private int safeNode = -1;
        private long safeUntil;
        private string terminalReason;
        private readonly int healthScale;
        private readonly int countScale;
        private readonly int roomLimit;
        public long WorldTick { get; private set; }
        public string Outcome { get; private set; }
        public bool Terminal => Outcome != null;
        public int MaximumRoom => Math.Min(5, rooms.Take(6).Count(room => room.Status == "cleared"));
        public string LastActionReason { get; private set; }

        public QualityWorld(JObject allocation)
        {
            manifest = (JObject)allocation.DeepClone(); config = (JObject)allocation["config"].DeepClone(); extension = (JObject)allocation["p1_world"].DeepClone();
            enemyDefinitions = ((JArray)config["enemies"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            schemes = ((JArray)config["loadouts"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            upgrades = ((JArray)config["upgrades"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            var count = ((JArray)allocation["members"]).Count;
            var scaling = ((JArray)config["scaling"]).First(item => (int)item["players"] == count);
            healthScale = Number(scaling, "health_milli"); countScale = Number(scaling, "count_milli"); roomLimit = (int)extension["encounter"]["room_alive_limits"][count - 1];
            reinforcementRemaining = Number(config["operation"], "reinforcements");
            rooms = Enumerable.Range(0, 8).Select(index => new Room { Index = index }).ToArray(); rooms[0].Status = "cleared";
            rooms[6].Exists = (string)allocation["mode"] == "trial" || Rank("branch_a", "include").CompareTo(Rank("branch_a", "exclude")) < 0;
            rooms[7].Exists = (string)allocation["mode"] == "trial" || Rank("branch_b", "include").CompareTo(Rank("branch_b", "exclude")) < 0;
            foreach (var member in (JArray)allocation["members"])
            {
                var id = (string)member["player_id"]; var slot = (int)member["slot"];
                players[id] = new Player { ID = id, Slot = slot, Loadout = (string)member["loadout_id"], Position = RoomGeometry.Point(Layout(0)["spawn"]) + new Vector2(slot * 0.6f, 0), Health = Number(config["player"], "max_health") };
                requirements[id] = allocation["task_requirements"]?[id] as JObject ?? new JObject();
            }
            foreach (var room in rooms.Where(item => item.Exists)) foreach (var point in (JArray)Layout(room.Index)["trap_points"]) traps.Add(new Trap { ID = "trap_" + room.Index + "_" + traps.Count, Room = room.Index, Position = RoomGeometry.Point(point) });
        }

        public void AdvanceTime(long milliseconds) { now = milliseconds; }
        public void FreezeTime(long milliseconds) { now = milliseconds; lastTickAt = milliseconds; }
        public void Input(string id, GrayboxInput input)
        {
            if (!players.TryGetValue(id, out var player) || player.Left || !player.Connected || input.Sequence != 0 && input.Sequence < player.Ack) return;
            var pressed = player.Input.AttackPressed;
            player.Input = input; player.Input.Move = Vector2.ClampMagnitude(input.Move, 1); player.Input.AttackPressed |= pressed; player.InputAt = now; player.Ack = Math.Max(player.Ack, input.Sequence);
        }
        public void Presence(string id, bool connected) { if (players.TryGetValue(id, out var player)) { player.Connected = connected; player.Input = default; player.BufferedAction = null; player.InteractionTarget = null; } }
        public void Leave(string id) { if (players.TryGetValue(id, out var player)) { player.Left = true; player.Input = default; player.BufferedAction = null; player.InteractionTarget = null; projectiles.RemoveAll(item => item.Owner == id); turrets.RemoveAll(item => item.Owner == id); } }

        public bool Action(string id, string action, GrayboxInput? intent = null)
        {
            LastActionReason = "inactive";
            if (!players.TryGetValue(id, out var player) || player.Left || Terminal) return false;
            if (action.StartsWith("choose:", StringComparison.Ordinal)) return SelectUpgrade(id, action.Substring(7));
            if (!player.Connected || player.Health <= 0 || now < player.ProtectedUntil) return false;
            if (intent.HasValue)
            {
                var actionInput = intent.Value;
                actionInput.Attack = player.Input.Attack; actionInput.Interact = player.Input.Interact;
                if (actionInput.Sequence >= player.Ack) Input(id, actionInput);
                else { player.Input.Aim = actionInput.Aim; player.Input.Placement = actionInput.Placement; player.Input.HasPlacement = actionInput.HasPlacement; }
            }
            if (action == "attack") { if (safeNode >= 0 || now < player.AttackAt) { LastActionReason = "cooldown"; return false; } player.Input.AttackPressed = true; LastActionReason = "accepted"; return true; }
            if (safeNode >= 0) { LastActionReason = "safe_window"; return false; }
            var settings = config["player"]; var scheme = schemes[player.Loadout];
            if (action == "dodge" || action == "ability")
            {
                var readyAt = action == "dodge" ? player.DodgeAt : player.AbilityAt;
                if (now < readyAt)
                {
                    LastActionReason = "cooldown";
                    if (readyAt - now <= Number(extension["input"], "buffer_ms") && player.BufferedAction == null) { player.BufferedAction = action; player.BufferUntil = now + Number(extension["input"], "buffer_ms"); LastActionReason = "buffered"; return true; }
                    return false;
                }
                if (action == "dodge")
                {
                    player.DodgeDirection = player.Input.Move.sqrMagnitude > 0.01f ? player.Input.Move.normalized : player.Direction.sqrMagnitude > 0.01f ? player.Direction : Aim(player.Input.Aim);
                    player.DodgeAt = now + Math.Max(Number(settings, "minimum_dodge_cooldown_ms"), Number(settings, "dodge_cooldown_ms") - (Has(player, "swift_dodge") ? 600 : 0));
                    player.DodgeUntil = now + Number(settings, "dodge_duration_ms"); player.InvulnerableUntil = now + Number(settings, "dodge_invulnerable_ms"); Effect("dodge", id, player.Room, player.Position);
                }
                else
                {
                    if (!Ability(player, scheme)) { LastActionReason = "invalid_position"; return false; }
                    player.AbilityAt = now + Number(scheme, "ability_cooldown_ms");
                    if (Has(player, "rune_shield")) { player.Shield = Math.Max(player.Shield, 15); player.ShieldUntil = Math.Max(player.ShieldUntil, combatNow + 3000); }
                }
                player.InteractionTarget = null; LastActionReason = "accepted"; return true;
            }
            if (action == "supply")
            {
                var key = player.Room == 7 ? "branch_supply" : "supply_court";
                var position = player.Room == 7 ? new Vector2(5, 4) : new Vector2(3, 3);
                if ((player.Room != 3 && player.Room != 7 || player.Room == 7 && rooms[7].Status != "cleared") || player.Claims.Contains(key) || player.Health >= Number(settings, "max_health") || Vector2.Distance(player.Position, position) > Distance(settings, "interaction_radius_milli_tiles")) { LastActionReason = "unavailable_supply"; return false; }
                player.Health = Math.Min(Number(settings, "max_health"), player.Health + Number(settings, "supply_health")); player.Claims.Add(key); Effect("supply", id, player.Room, position); LastActionReason = "accepted"; return true;
            }
            return false;
        }

        public List<JObject> Tick(long elapsedMilliseconds)
        {
            if (Terminal) return new List<JObject>();
            now = elapsedMilliseconds; facts.Clear(); WorldTick++;
            var passed = Math.Max(0, now - lastTickAt); lastTickAt = now;
            if (now >= Number(config["operation"], "hard_deadline_ms")) { SetTerminal("failed", "timeout"); return new List<JObject>(); }
            var safe = safeNode >= 0;
            if (!safe) combatNow += passed;
            aliveAtTick.Clear(); foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0)) aliveAtTick.Add(player.ID);
            foreach (var player in players.Values.OrderBy(item => item.Slot)) TickPlayer(player, safe);
            if (safe)
            {
                if (now >= safeUntil || players.Values.Where(item => !item.Left).All(item => item.Choices.ContainsKey(safeNode))) CloseChoice();
            }
            else
            {
                TickRooms(); TickTraps();
                foreach (var enemy in enemies.Where(item => item.Health > 0).ToArray()) TickEnemy(enemy);
                TickTurrets(); TickProjectiles();
                foreach (var player in players.Values.OrderBy(item => item.Slot)) TickInteraction(player);
            }
            if (!safe) FinishRooms();
            if (rooms[5].Status == "cleared") SetTerminal("success", "objectives_complete");
            else if (players.Values.All(item => item.Left)) SetTerminal("failed", "team_abandoned");
            else if (players.Values.Where(item => !item.Left).All(item => item.Health <= 0) && !players.Values.Any(item => item.Reinforcement != null) && reinforcementRemaining <= 0) SetTerminal("failed", "team_wipe");
            foreach (var player in players.Values.Where(item => item.Left || item.Health <= 0)) { projectiles.RemoveAll(item => item.Owner == player.ID); turrets.RemoveAll(item => item.Owner == player.ID); }
            enemies.RemoveAll(item => item.Health <= 0);
            var activeActions = new HashSet<string>(projectiles.Select(item => item.Action)); damageActions.RemoveWhere(action => !activeActions.Contains(action));
            foreach (var key in defense.Where(item => combatNow - item.Value > 5000).Select(item => item.Key).ToArray()) defense.Remove(key);
            return facts.Select(item => (JObject)item.DeepClone()).ToList();
        }

        private void TickPlayer(Player player, bool safe)
        {
            if (player.Left) return;
            if (player.Health <= 0)
            {
                if (player.Reinforcement != null && now >= player.RespawnAt && !player.SpawnAwaitingAck)
                {
                    player.Health = Number(config["player"], "max_health"); player.Room = rooms[4].Status == "cleared" ? 4 : rooms[3].Status == "cleared" ? 3 : 0; player.Position = RoomGeometry.Point(Layout(player.Room)["spawn"]);
                    player.ProtectedUntil = now + Number(config["authority"], "spawn_protection_ms"); player.InvulnerableUntil = player.ProtectedUntil; player.SpawnAwaitingAck = true; player.Input = default; player.BufferedAction = null;
                    facts.Add(Fact("reinforcement_spawned", player.ID, "safe_point", null, 0, player.Reinforcement)); Effect("respawn", player.ID, player.Room, player.Position);
                }
                return;
            }
            if (now - player.InputAt > Number(config["authority"], "input_timeout_ms")) { player.Input = default; player.BufferedAction = null; player.InteractionTarget = null; }
            if (player.ShieldUntil <= combatNow) player.Shield = 0;
            if (player.BufferedAction != null)
            {
                var buffered = player.BufferedAction;
                if (now > player.BufferUntil || safe) player.BufferedAction = null;
                else if (now >= (buffered == "dodge" ? player.DodgeAt : player.AbilityAt)) { player.BufferedAction = null; Action(player.ID, buffered); }
            }
            if (player.Input.Move.sqrMagnitude > 0.01f) player.Direction = player.Input.Move.normalized;
            var dodging = !safe && now < player.DodgeUntil;
            var movement = dodging ? player.DodgeDirection * Distance(config["player"], "dodge_distance_milli_tiles") / (Number(config["player"], "dodge_duration_ms") / 1000f) : player.Input.Move * Distance(config["player"], "move_milli_tiles_per_second");
            var before = player.Position;
            player.Position = RoomGeometry.Move(Layout(player.Room), player.Position, movement / 30, Distance(config["player"], "collision_radius_milli_tiles"), point => enemies.Any(enemy => enemy.Room == player.Room && enemy.Health > 0 && (!dodging || enemy.Kind == "boss" || enemy.Kind == "elite") && Vector2.Distance(point, enemy.Position) < Radius(enemy) + 0.3f && Vector2.Distance(point, enemy.Position) < Vector2.Distance(before, enemy.Position)));
            if (!safe) TryDoor(player);
            if (!safe && player.PendingShield > 0 && rooms[player.Room].Status == "active" && now >= player.ProtectedUntil) { player.Shield = Math.Max(player.Shield, player.PendingShield); player.PendingShield = 0; player.ShieldUntil = combatNow + 30000; }
            if (!safe && player.InteractionTarget != null && (player.Input.Attack || player.Input.AttackPressed)) player.InteractionTarget = null;
            if (!safe && (player.Input.Attack || player.Input.AttackPressed) && now >= player.AttackAt && now >= player.ProtectedUntil && player.InteractionTarget == null)
            {
                FirePlayer(player); player.AttackAt = now + Number(schemes[player.Loadout], "attack_interval_ms");
            }
            player.Input.AttackPressed = false;
            if (!dodging && player.DodgeUntil > 0 && now >= player.DodgeUntil) { if (Has(player, "landing_impact") && !safe) foreach (var enemy in enemies.Where(item => item.Health > 0 && item.Room == player.Room && Vector2.Distance(item.Position, player.Position) <= 1.5f).ToArray()) Hit(enemy, player.ID, NextID("impact"), 10); player.DodgeUntil = 0; }
            if (player.Room == 2 && rooms[2].Status == "cleared" && !player.Reached.Contains("inspect_zone") && Vector2.Distance(player.Position, new Vector2((int)Layout(2)["width"] - 3, (int)Layout(2)["height"] / 2f)) < 2)
            {
                player.Reached.Add("inspect_zone"); player.Reaches++; facts.Add(Fact("reach", player.ID, "inspect_zone", null, 0));
            }
        }

        private void TryDoor(Player player)
        {
            if (now < player.DoorAt || !player.Connected || now < player.ProtectedUntil) return;
            foreach (var door in (JArray)extension["doors"])
            {
                var from = (int)door["from"]; var to = (int)door["to"]; var forward = player.Room == from; if (!forward && player.Room != to) continue;
                var side = forward ? (string)door["side"] : RoomGeometry.Opposite((string)door["side"]);
                var point = RoomGeometry.DoorPoint(Layout(player.Room), side);
                var direction = side == "east" ? Vector2.right : side == "west" ? Vector2.left : side == "north" ? Vector2.up : Vector2.down;
                if (Vector2.Distance(player.Position, point) > 0.85f || Vector2.Dot(player.Input.Move, direction) < 0.4f) continue;
                var destination = forward ? to : from;
                if (!rooms[destination].Exists || forward && rooms[from].Status != "cleared" || !forward && rooms[to].Status != "cleared") continue;
                player.Room = destination; player.Position = RoomGeometry.DoorPoint(Layout(destination), RoomGeometry.Opposite(side)) + direction * 0.7f; player.DoorAt = now + 500; player.InteractionTarget = null; player.Input.AttackPressed = false; player.BufferedAction = null; player.DodgeUntil = 0;
                projectiles.RemoveAll(item => item.Owner == player.ID); Effect("door", player.ID, destination, player.Position); return;
            }
        }

        private JObject Layout(int room) => (JObject)extension["rooms"][room];
        private bool Has(Player player, string upgrade) => player.Upgrades.Contains(upgrade);
        private string Rank(string decision, string option) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest["seed"] + ":" + config["world"]["generator_version"] + ":" + decision + ":" + option))).Replace("-", ""); }
        private string NextID(string prefix) => prefix + "_" + (++serial);
        private void SetTerminal(string outcome, string reason) { Outcome = outcome; terminalReason = reason; playerInputsClear(); }
        private void playerInputsClear() { foreach (var player in players.Values) { player.Input = default; player.BufferedAction = null; } }
        private static int Number(JToken token, string key, int fallback = 0) => (int?)token?[key] ?? fallback;
        private static float Distance(JToken token, string key) => Number(token, key) / 1000f;
        private float Radius(Enemy enemy) => Distance(extension["physics"], enemy.Kind == "boss" ? "boss_radius_milli" : enemy.Kind == "elite" ? "elite_radius_milli" : "enemy_radius_milli");
        private static Vector2 Aim(ushort angle) { var radians = angle / 65535f * Mathf.PI * 2; return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)); }
        private static Vector2 Rotate(Vector2 direction, float degrees) { var radians = degrees * Mathf.Deg2Rad; return new Vector2(direction.x * Mathf.Cos(radians) - direction.y * Mathf.Sin(radians), direction.x * Mathf.Sin(radians) + direction.y * Mathf.Cos(radians)); }
        private static JObject Fact(string kind, string actor, string target, string action, int amount, string reinforcement = null)
        {
            var fact = new JObject { ["event_id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["actor_player_id"] = actor, ["target_id"] = target, ["action_id"] = action, ["effective_amount"] = amount, ["contributors"] = new JArray() };
            if (reinforcement != null) fact["reinforcement_action_id"] = reinforcement;
            return fact;
        }
        private void Effect(string kind, string actor, int room, Vector2 position, int amount = 0, string target = null) { if (effects.Count >= 64) effects.Dequeue(); effects.Enqueue(new JObject { ["id"] = NextID("fx"), ["kind"] = kind, ["actor"] = actor, ["target"] = target, ["room"] = room, ["x"] = position.x, ["y"] = position.y, ["amount"] = amount, ["at"] = now }); }
    }
}
