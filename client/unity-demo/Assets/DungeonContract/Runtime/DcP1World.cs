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
    public sealed partial class DcP1World
    {
        private sealed class Player
        {
            public string ID;
            public int Slot;
            public string Loadout;
            public string BodyFacing="down"; public uint EmissionIndex;
            [Newtonsoft.Json.JsonIgnore] public DcV7Geometry.Body CachedBody;
            public Vector2 TickStartCenter;
            [Newtonsoft.Json.JsonIgnore] public bool V7InspectCrossed;
            public bool MeleeEquipped;
            public uint WeaponRevision=1;
            public long SwitchAt;
            public bool ReleaseRequired;
            public long RangedReady;
            public long MeleeReady;
            public MeleeV5 MeleeAttack;
            public bool ChargeRequested;
            public string ChargeAction;
            public string AttackEpisode;public string LastAttackEpisode;
            public uint LastActionSequence; public uint RejectedActionThrough;
            public long HurtUntil;
            public readonly HashSet<string> HurtActions=new HashSet<string>();
            public int Room;
            public Vector2 Position;
            public Vector2 Direction;
            public Vector2 DodgeDirection;
            public DcP1Input Input;
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
            public int TurretCharges; public string TurretGroup; public long BattleUntil; public string SpinRoot,SpinEndReason; public int SpinRoom,SpinPulse,SpinDamage; public long SpinStart,SpinUntil; public bool SpinFrost,SpinChain; public JObject LastSlam;
            public long DodgeAt;
            public long DodgeUntil;
            public float DodgeSpeed=8;
            public long InvulnerableUntil;
            public long ProtectedUntil; public long RescueUntil; public long ReinforcementUntil;
            public long DoorAt;
            public long InteractionAt;
            public string InteractionTarget;
            public string Reinforcement;
            public long RespawnAt;
            public bool SpawnAwaitingAck;
            public int Magazine;
            public long ReloadUntil; public long ReloadStarted; public int ReloadDuration; public string DashSource; public long SpaceUntil; public long GustUntil; public Vector2 TickStart;
            public bool Charging;
            public long ChargeAt;
            public long SecondaryAt;
            public int Knockdowns;
            public int Rescues;
            public long LifeGeneration=1;
            public long DownedGeneration;
            public long DownedUntil;
            public long HurtAt=-1;
            public long LastCombatAt=-1;
            public string RescueTarget;
            public string RescueAction;
            public long RescueEpisode;
            public long RescueAt;
            public string ShieldOwner;
            public string ShieldCast;
            public long ProtectedDamage;
            public long ControlEffect;
            public int ClearedProjectiles;
            public string Life = "alive";
            public long LightCount;
            public long HeavyCount;
            public string BufferedAction;
            public string BufferedID;
            public DcP1Input BufferedIntent;
            public long BufferUntil;
            public readonly HashSet<string> Claims = new HashSet<string>();
            public readonly HashSet<string> Reached = new HashSet<string>();
            public readonly HashSet<int> Explored = new HashSet<int>{0};
            public readonly List<GrowthChoice> Growth = new List<GrowthChoice>();
            public readonly List<string> Upgrades = new List<string>();
            public readonly Dictionary<int, string> Choices = new Dictionary<int, string>();
        }

        private sealed class Room
        {
            public int Index;
            public bool Connector;
            public bool Exists = true;
            public string Status = "unvisited";
            public long WarningAt;
            public long NextWaveAt;
            public int Wave;
            public bool Mechanism;
            public bool EliteQueued;
            public bool NodeOpened;
            public long SpawnBlockedAt;
            public int SpawnRecoveries;
            public long SpawnRetryAt;
            public long GroupUntil;
            public readonly HashSet<string> GroupIDs=new HashSet<string>();
            public Queue<string> Pending = new Queue<string>();
        }

        private sealed class Enemy
        {
            public string ID;
            public string Kind;
            public string BodyFacing="down";
            [Newtonsoft.Json.JsonIgnore] public DcV7Geometry.Body CachedBody;
            public long RecoveryPlacementUntil,RecoveryPlacementRetryAt;
            public int Room;
            public Vector2 Position;
            public Vector2 Origin; public Vector2 TickStart;
            public Vector2 Aim;
            public int Health;
            public int Maximum;
            public bool Summon;
            public string Warning;
            public long WarningUntil;
            public long AttackAt;
            public long ChargeUntil;
            public long PresentationAttackAt=-10000;
            public long KnockbackAt;
            public long SlowUntil;
            public float SlowFactor = 1;
            public long SummonAt;
            public long StuckAt;
            public Vector2 StuckPosition;
            public int RecoveryCount;
            public string AttackAction;
            public long SuppressedUntil;
            public readonly HashSet<string> HitActions=new HashSet<string>();
            public int Pattern;
            public int Summoned;
            public int ReservedShots;
            public string CoverTarget; public long CoverTargetUntil; public long CoverBlockedAt; public long CoverProbeAt; public Vector2 CoverProbePosition; public bool CoverNoDetour;
            public string TargetID;
            public long TargetUntil;
            public int PathTerrainRevision=-1;
            public int FeintSign;
            public long ReadyAt;
            public long PathAt;
            public Vector2 Path;
            public readonly Dictionary<string, long> Damagers = new Dictionary<string, long>();
            public EnemyV5 V5State;
        }

        private sealed class Projectile
        {
            [Newtonsoft.Json.JsonIgnore] public bool RuntimeSnapshotBound;
            public bool MuzzlePending; public Vector2 MuzzleOrigin; public int TrajectoryRevision; public string TerminationReason;
            public long SourceLife;
            public int ProtocolVersion; public long BornGameMS, ExpiresGameMS; public uint EmissionIndex; public string Episode; public int ContactStalls;
            public readonly HashSet<string> Inside = new HashSet<string>();
            public readonly Dictionary<string,int> Entries = new Dictionary<string,int>();
            public long EmissionFrame;
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
            public int Bounces;
            public float CollisionRadius=0.1f;
            public bool RuneBolt;
            public bool ChainTriggered;
            public bool Unbounded;
            public bool ChainEnabled;
            public bool FrostEnabled;
            public string Root;
            public int NetworkGroup;
            public readonly HashSet<string> Hit = new HashSet<string>();
        }

        private sealed class Cover { public string ID; public int Room; public Vector2 Position; public int Health; public int Revision; }
        private sealed class Field { public string ID; public string Owner; public string Kind; public int Room; public Vector2 Position; public long Until; public readonly HashSet<string> Granted = new HashSet<string>(); }
        private sealed class QueuedAction { public string Player; public string Kind; public DcP1Input Intent; public string ID; }
        private sealed class Turret { public string Group; public string ID; public string Owner; public long SourceLife; public int Room; public Vector2 Position; public long Until; public long AttackAt; public int BaseDamage; }
        private sealed class Trap { public string ID; public int Room; public Vector2 Position; public string Phase = "armed"; public string Group; public long Epoch=-1; public int ActivationGeneration; public long Cycle=-1; public long Until; public long Trigger; public readonly HashSet<string> Hit = new HashSet<string>(); }
        private sealed class SpawnReservation { public string ID; public int Room; public string Kind; public Vector2 Position; public long Until; public bool Summon; public string Recovering; public string Batch; public long Begin; public int Revision=1; }
        private readonly List<SpawnReservation> spawnReservations=new List<SpawnReservation>();
        private bool technicalWaiting;
        private sealed class VectorConverter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer) { writer.WriteStartObject(); writer.WritePropertyName("x"); writer.WriteValue(value.x); writer.WritePropertyName("y"); writer.WriteValue(value.y); writer.WriteEndObject(); }
            public override Vector2 ReadJson(JsonReader reader, Type type, Vector2 value, bool existing, JsonSerializer serializer) { var point = JObject.Load(reader); return new Vector2((float)point["x"], (float)point["y"]); }
        }

        private readonly JObject manifest;
        private readonly JObject config;
        private readonly JObject parameters;
        private readonly List<Cover> covers = new List<Cover>();
        private readonly List<Field> fields = new List<Field>();
        private readonly HashSet<string> frameCovers = new HashSet<string>();
        private readonly Dictionary<string, string> actionResults = new Dictionary<string, string>();
        private readonly Queue<string> actionOrder = new Queue<string>();
        private readonly Queue<QueuedAction> queued = new Queue<QueuedAction>();
        public bool Practice { get; private set; }
        public JObject Definition => extension;
        public bool Weapons {get;private set;}
        public bool P2 {get;private set;}
        public bool P3 {get;private set;}
        public bool V5 {get;private set;}
        public bool V6 {get;private set;}
        public bool V7 {get;private set;}
        private readonly Dictionary<string,DcP1Input> waitingHeld=new Dictionary<string,DcP1Input>();
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
        private string terminalReason; private string technicalDetail;
        private void AbortTechnical([System.Runtime.CompilerServices.CallerMemberName]string source=null){technicalDetail=source??"unknown";if(V6)Debug.LogError("DC v6 technical_stop origin="+technicalDetail+" frame="+WorldTick);SetTerminal("aborted","technical_failure");}
        private readonly int healthScale;
        private readonly int countScale;
        private readonly int roomLimit;
        private readonly float normalRadius;
        private readonly float eliteRadius;
        private readonly float bossRadius;
        public long WorldTick { get; private set; }
        public string Outcome { get; private set; }
        public bool Terminal => Outcome != null;
        public int MaximumRoom => Math.Min(5, rooms.Take(6).Count(room => room.Status == "cleared"));
        public string LastActionReason { get; private set; }
        public Action<string,double> TickPhaseObserver {get;set;}
        private void ObserveTickPhase(string phase,ref long stamp){if(TickPhaseObserver==null)return;var next=System.Diagnostics.Stopwatch.GetTimestamp();TickPhaseObserver(phase,(next-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency);stamp=next;}

        public DcP1World(JObject allocation)
        {
            V8=(int?)allocation["parameters"]?["versions"]?["battle_protocol"]==8; V7=V8||(int?)allocation["parameters"]?["versions"]?["battle_protocol"]==7;
            V6=V7||(int?)allocation["parameters"]?["versions"]?["battle_protocol"]==6;
            V5=V6||(int?)allocation["parameters"]?["versions"]?["battle_protocol"]==5;
            P4=(bool?)allocation["p4_stage"]??((string)allocation["config"]?["lifecycle_stage"]=="personal_growth_content_stage");
            P3=(bool?)allocation["p3_stage"]??((string)allocation["config"]?["lifecycle_stage"]=="cooperation_business_stage");
            P2=(bool?)allocation["p2_stage"]??((string)allocation["config"]?["lifecycle_stage"]=="room_camera_projectile_stage");
            Weapons=((bool?)allocation["weapon_stage"]??P2)||((string)allocation["config"]?["lifecycle_stage"]=="weapon_stage");
            manifest = (JObject)allocation.DeepClone(); config = DcP1Definition.Simulation((JObject)allocation["parameters"]); extension = DcP1Definition.NormalizeGeometry((JObject)allocation["p1_world"]); parameters = (JObject)allocation["parameters"].DeepClone();if(V6)extension["parameters"]=parameters.DeepClone();
            normalRadius=Distance(extension["physics"],"enemy_radius_milli");eliteRadius=Distance(extension["physics"],"elite_radius_milli");bossRadius=Distance(extension["physics"],"boss_radius_milli");
            if(P2)config["world"]["generator_version"]="dc_rooms.v4.p2.1";
            if(V5)config["world"]["generator_version"]=(string)allocation["config"]?["versions"]?["world"]??"dc_rooms.v5.p4.1";
            enemyDefinitions = ((JArray)config["enemies"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            schemes = ((JArray)config["loadouts"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            upgrades = ((JArray)config["upgrades"]).Cast<JObject>().ToDictionary(item => (string)item["id"]);
            var count = ((JArray)allocation["members"]).Count;
            var scaling = ((JArray)config["scaling"]).First(item => (int)item["players"] == count);
            healthScale = Number(scaling, "health_milli"); countScale = Number(scaling, "count_milli"); roomLimit = (int)extension["encounter"]["room_alive_limits"][count - 1];
            reinforcementRemaining = P3?Number(parameters["player"],"reinforcement_budget"):0; Practice = (bool?)allocation["practice"] ?? false;
            if(P3){config["operation"]["reinforcements"]=reinforcementRemaining;config["authority"]["spawn_delay_ms"]=Number(parameters["player"],"reinforcement_delay_ms");}
            var roomCount=((JArray)extension["rooms"]).Count;
            rooms = Enumerable.Range(0, roomCount).Select(index => new Room { Index = index, Exists=!P4||((bool?)Layout(index)["exists"]??true), Connector=(string)Layout(index)["kind"]=="corridor" }).ToArray(); rooms[0].Status = "cleared";
            if(rooms.Length>6&&!P2){rooms[6].Exists=false;rooms[7].Exists=false;}
            foreach(var room in rooms.Where(item=>item.Connector)){room.Status="cleared";if(!P4)room.Exists=true;}
            foreach (var member in (JArray)allocation["members"])
            {
                var id = (string)member["player_id"]; var slot = (int)member["slot"];
                players[id] = new Player { ID = id, Slot = slot, Loadout = (string)member["loadout_id"], Position = RoomGeometry.Point(Layout(0)["spawn"]) + new Vector2(slot * 0.6f, 0), Health = Number(config["player"], "max_health"), Magazine = Number(Kit((string)member["loadout_id"])["ranged"], "magazine_size") };
                requirements[id] = allocation["task_requirements"]?[id] as JObject ?? new JObject();
            }
            foreach(var room in rooms.Where(item=>item.Exists&&!item.Connector)) foreach(var point in (JArray)Layout(room.Index)["cover_points"]) covers.Add(new Cover {ID=NextID("cover"),Room=room.Index,Position=RoomGeometry.Point(point),Health=Number(parameters["terrain"],"cover_health")});
            InitializeV6();
            if(Practice) { rooms[0].Status="practice"; SpawnEnemy(0,"guard",new Vector2(9,5)); SpawnEnemy(0,"archer",new Vector2(13,7)); }
            foreach (var room in rooms.Where(item => item.Exists&&!item.Connector)) foreach (var point in (JArray)Layout(room.Index)["trap_points"]) traps.Add(new Trap { ID = "trap_" + room.Index + "_" + traps.Count, Room = room.Index, Position = RoomGeometry.Point(point) });
        }

        public void AdvanceTime(long milliseconds) { now = milliseconds; }
        public void FreezeTime(long milliseconds) { now = milliseconds; lastTickAt = milliseconds; }
        public void Input(string id, DcP1Input input)
        {
            if (!players.TryGetValue(id, out var player) || player.Left || !player.Connected || input.Sequence != 0 && input.Sequence < player.Ack) return;
            if(Weapons){if(input.WeaponRevision!=player.WeaponRevision||input.WeaponID!=WeaponID(player))return;
                if(input.ActionWatermark>player.LastActionSequence){waitingHeld[id]=input;player.Input.Move=Vector2.ClampMagnitude(input.Move,1);player.Input.Aim=input.Aim;player.InputAt=now;player.Ack=Math.Max(player.Ack,input.Sequence);return;}
                if(input.ActionWatermark<player.LastActionSequence){player.Input.Move=Vector2.ClampMagnitude(input.Move,1);player.Input.Aim=input.Aim;player.InputAt=now;player.Ack=Math.Max(player.Ack,input.Sequence);return;}
                if(!input.Attack&&!input.Melee)player.ReleaseRequired=false;if(player.ReleaseRequired){input.Attack=false;input.Melee=false;}
                if(V5){if(player.AttackEpisode==null||input.AttackEpisode!=player.AttackEpisode){input.Attack=false;input.Melee=false;}if(player.ChargeAction==null||input.ChargeEpisode!=player.ChargeAction)input.Charge=false;}
            }
            var pressed = player.Input.AttackPressed;
            player.Input = input; player.Input.Move = Vector2.ClampMagnitude(input.Move, 1); player.Input.AttackPressed |= pressed; player.InputAt = now; player.Ack = Math.Max(player.Ack, input.Sequence);
        }
        public void Presence(string id, bool connected) { if (players.TryGetValue(id, out var player)) { player.Connected = connected; player.Input = default; CancelWeaponBuffer(player); player.InteractionTarget = null;player.RescueTarget=null; player.Charging = false; if(V5){CancelBasic(player);player.ReleaseRequired=true;} if(!Weapons)player.ReloadUntil = 0; } }
        public void Reconnect(string id){if(players.TryGetValue(id,out var player)){var count=queued.Count;for(var index=0;index<count;index++){var action=queued.Dequeue();if(action.Player==id){actionResults[action.ID]="stale_connection";actionOrder.Enqueue(action.ID);}else queued.Enqueue(action);}while(actionOrder.Count>512)actionResults.Remove(actionOrder.Dequeue());Presence(id,true);player.Ack=0;player.LastActionSequence=0;player.RejectedActionThrough=0;player.ChargeAction=null;player.ReleaseRequired=true;waitingHeld.Remove(id);foundationInputs.Remove(id);}}
        public void Leave(string id) { if (players.TryGetValue(id, out var player)) { if(V8){V8StopSpin(player,"permanent_exit");player.TurretCharges=0;player.TurretGroup=null;}player.Left = true; player.Input = default; CancelWeaponBuffer(player); player.InteractionTarget = null; projectiles.RemoveAll(item => item.Owner == id); turrets.RemoveAll(item => item.Owner == id); if(V5){CancelBasic(player);player.RescueTarget=null;fields.RemoveAll(item=>item.Owner==id);} } }

        public List<JObject> Tick(long elapsedMilliseconds)
        {
            if (Terminal) return new List<JObject>();
            var phaseStamp=TickPhaseObserver==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
            now = elapsedMilliseconds; facts.Clear(); WorldTick++;
            if(V7&&v7PhysicsWaitUntil>0){if(now<v7PhysicsWaitUntil){combatNow+=Math.Max(0,now-lastTickAt);lastTickAt=now;turrets.RemoveAll(t=>combatNow>=t.Until);return new List<JObject>();}TechnicalWait(false,now);v7PhysicsWaitUntil=0;}
            if(V6){foreach(var p in players.Values){p.TickStart=p.Position;if(V7){p.TickStartCenter=Center(p);p.V7InspectCrossed=false;}}foreach(var e in enemies)e.TickStart=e.Position;BeginV6LifecycleTick();} frameCovers.Clear(); foreach(var cover in covers.Where(item=>item.Health>0)) frameCovers.Add(cover.ID);
            var passed = Math.Max(0, now - lastTickAt); lastTickAt = now;
            if (now >= Number(config["operation"], "hard_deadline_ms")) { SetTerminal("failed", "timeout"); return new List<JObject>(); }
            var safe = safeNode >= 0;
            if (!safe) combatNow += passed;
            aliveAtTick.Clear(); foreach (var player in players.Values.Where(item => !item.Left && item.Health > 0)) aliveAtTick.Add(player.ID);
            while(queued.Count>0) {var action=queued.Dequeue(); ExecuteAction(action.Player,action.Kind,action.Intent,action.ID);}
            if(V6)foreach(var p in players.Values){p.LastActionSequence=Math.Max(p.LastActionSequence,p.RejectedActionThrough);p.RejectedActionThrough=0;}
            if(Weapons)FlushWeaponHeld();
            TickGrowth();
            ObserveTickPhase("lifecycle_inputs",ref phaseStamp);
            foreach (var player in players.Values.OrderBy(item => item.Slot)) TickPlayer(player, safe);
            ObserveTickPhase("players",ref phaseStamp);
            if (safe)
            {
                if (now >= safeUntil || players.Values.Where(item => !item.Left).All(item => item.Choices.ContainsKey(safeNode))) CloseChoice();
            }
            else
            {
                TickRooms(); if(P2)TickSpawnReservations();ObserveTickPhase("rooms_spawn",ref phaseStamp); TickTraps(); TickFields(); if(V8)TickV8Skills();ObserveTickPhase("traps_fields",ref phaseStamp);
                if(!Practice) foreach (var enemy in enemies.Where(item => item.Health > 0).ToArray()) TickEnemy(enemy);
                ObserveTickPhase("enemy_ai",ref phaseStamp);if(V6)ResolveV6Melee(); TickTurrets();ObserveTickPhase("melee_turrets",ref phaseStamp); TickProjectiles();ObserveTickPhase("projectiles",ref phaseStamp);
                foreach (var player in players.Values.OrderBy(item => item.Slot)) TickInteraction(player);
            }
            if (!safe && !Practice) FinishRooms();
            if(P3)TickP3Life(rooms[5].Status=="cleared");
            OpenGrowth();
            ObserveTickPhase("interaction_life_rooms",ref phaseStamp);
            if (rooms[5].Status == "cleared") SetTerminal("success", "objectives_complete");
            else if (players.Values.All(item => item.Left)) SetTerminal(P3?"aborted":"failed", "team_abandoned");
            else if (!Practice && !P3 && players.Values.Where(item => !item.Left).All(item => item.Health <= 0)) SetTerminal("failed", "p1_incapacitated");
            foreach (var player in players.Values.Where(item => item.Left || item.Health <= 0)) { if(!P2)projectiles.RemoveAll(item => item.Owner == player.ID); if(!P2)turrets.RemoveAll(item => item.Owner == player.ID); }
            foreach(var cover in covers.Where(item=>item.Health<=0 && frameCovers.Contains(item.ID))) cover.Revision++;
            if(Practice) foreach(var enemy in enemies.Where(item=>item.Health<=0)) {enemy.Health=enemy.Maximum;enemy.Damagers.Clear();}
            enemies.RemoveAll(item => item.Health <= 0);
            var activeActions = new HashSet<string>(projectiles.Select(item => item.Action));
            if(V8)foreach(var p in players.Values.Where(p=>V8Spinning(p))){activeActions.Add(p.SpinRoot);foreach(var e in enemies)activeActions.UnionWith(e.HitActions.Where(id=>id.StartsWith(p.SpinRoot+":",StringComparison.Ordinal)||id==p.SpinRoot+"_chain"));}
            if(V5){activeActions.UnionWith(projectiles.Select(item=>item.Root).Where(item=>item!=null));activeActions.UnionWith(players.Values.Where(item=>item.MeleeAttack!=null).Select(item=>item.MeleeAttack.Root));v5ChainRoots.RemoveWhere(item=>!activeActions.Contains(item));foreach(var enemy in enemies)enemy.HitActions.RemoveWhere(item=>!activeActions.Contains(item));}
            damageActions.RemoveWhere(action => !activeActions.Contains(action));
            if(P3){activeActions.UnionWith(enemies.Select(item=>item.AttackAction).Where(item=>item!=null));activeActions.UnionWith(traps.Where(item=>item.Phase=="active"||item.Phase=="warning").Select(item=>item.ID+":"+item.Trigger));foreach(var player in players.Values)player.HurtActions.RemoveWhere(action=>!activeActions.Contains(action));}
            foreach (var key in defense.Where(item => combatNow - item.Value > 5000).Select(item => item.Key).ToArray()) defense.Remove(key);
            if(V6)EndV6LifecycleTick();
            ObserveTickPhase("cleanup_lifecycle",ref phaseStamp);
            return P3?P3Facts():facts.Select(item => (JObject)item.DeepClone()).ToList();
        }

        private void TryDoor(Player player)
        {
            if(V6){TryV6Door(player);return;}
            if (Practice || now < player.DoorAt || !player.Connected || now < player.ProtectedUntil) return;
            foreach (var door in (JArray)extension["doors"])
            {
                var from = (int)door["from"]; var to = (int)door["to"]; var forward = player.Room == from; if (!forward && player.Room != to) continue;
                var side = forward ? (string)door["side"] : RoomGeometry.Opposite((string)door["side"]);
                var point = RoomGeometry.DoorPoint(Layout(player.Room), side);
                var direction = side == "east" ? Vector2.right : side == "west" ? Vector2.left : side == "north" ? Vector2.up : Vector2.down;
                if (Vector2.Distance(player.Position, point) > 0.85f || Vector2.Dot(player.Input.Move, direction) < 0.4f) continue;
                var destination = forward ? to : from;
                if (!CanTraverse(player.Room,destination)) continue;
                if(Weapons)fields.RemoveAll(field=>field.Owner==player.ID);
                player.Room = destination;player.Explored.Add(destination); player.Position = RoomGeometry.DoorPoint(Layout(destination), RoomGeometry.Opposite(side)) + direction * 0.7f; player.DoorAt = now + 500; player.InteractionTarget = null; CancelBasic(player); player.DodgeUntil = 0;
                if(!P2)projectiles.RemoveAll(item => item.Owner == player.ID); Effect("door", player.ID, destination, player.Position); return;
            }
        }

        private JObject Layout(int room) => (JObject)extension["rooms"][room];
        private bool IsConnector(int room)=>rooms[room].Connector;
        private bool Has(Player player, string upgrade) => player.Upgrades.Contains(upgrade);
        private string Rank(string decision, string option) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest["seed"] + ":" + config["world"]["generator_version"] + ":" + decision + ":" + option))).Replace("-", ""); }
        private string NextID(string prefix) => prefix + "_" + (P3?(++serial).ToString("D6"): (++serial).ToString());
        private void SetTerminal(string outcome, string reason) { if(V8)foreach(var p in players.Values){V8StopSpin(p,"terminal");p.TurretCharges=0;p.TurretGroup=null;}Outcome = outcome; terminalReason = reason; playerInputsClear(); if(P2){projectiles.Clear();turrets.Clear();spawnReservations.Clear();foreach(var enemy in enemies)enemy.ReservedShots=0;} if(V5){v5Batch=null;v5ChainRoots.Clear();fields.Clear();foreach(var player in players.Values)CancelBasic(player);foreach(var enemy in enemies){enemy.V5State=null;enemy.Warning=null;enemy.WarningUntil=0;enemy.ChargeUntil=0;}enemies.RemoveAll(item=>item.Summon);} }
        private void playerInputsClear() { foreach (var player in players.Values) { player.Input = default; player.BufferedAction = null; } }
        private static int Number(JToken token, string key, int fallback = 0) => (int?)token?[key] ?? fallback;
        private static float Distance(JToken token, string key) => Number(token, key) / 1000f;
        private float Radius(Enemy enemy) => enemy.Kind=="boss"?bossRadius:enemy.Kind=="elite"?eliteRadius:normalRadius;
        public static Vector2 Aim(ushort angle) { var radians = angle / 65535f * Mathf.PI * 2; return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)); }
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
