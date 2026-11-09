using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonContract
{
    public sealed partial class DcBootstrap : MonoBehaviour
    {
        private sealed class ServiceFailure : Exception
        {
            public int Status;
            public int Code;
            public ServiceFailure(int status, int code) : base("Service rejected request") { Status = status; Code = code; }
        }

        private sealed class ActionIntent { public string Player; public string Kind; public string ID; public ulong Connection; public GrayboxInput Intent; }
        private readonly Dictionary<ulong, JObject> connected = new Dictionary<ulong, JObject>();
        private readonly HashSet<string> loaded = new HashSet<string>();
        private readonly Dictionary<ulong, uint> inputSequences = new Dictionary<ulong, uint>();
        private readonly Dictionary<string, GrayboxInput> inputs = new Dictionary<string, GrayboxInput>();
        private readonly Dictionary<string, double> receivedInputAt = new Dictionary<string, double>();
        private readonly Queue<ActionIntent> actions = new Queue<ActionIntent>();
        private readonly Dictionary<string, string> actionHashes = new Dictionary<string, string>();
        private readonly Queue<string> actionOrder = new Queue<string>();
        private readonly Dictionary<ulong, Queue<double>> actionRates = new Dictionary<ulong, Queue<double>>();
        private readonly SemaphoreSlim sender = new SemaphoreSlim(1, 1);
        private readonly Stopwatch elapsed = new Stopwatch();
        private CancellationTokenSource stopping;
        private HttpClient http;
        private NetworkManager network;
        private string role;
        private string token;
        private string boot;
        private long generation;
        private JObject assignment;
        private GrayboxWorld world;
        private DcGrayboxView view;
        private bool battle;
        private bool running;
        private bool draining;
        private bool committing;
        private bool phaseSent;
        private bool hadWait;
        private long appliedSequence;
        private long controlVersion;
        private long lastWorldFrame;
        private float snapshotAt;
        private JObject lastSnapshot;
        private string username = "";
        private string password = "";
        private string runID = "";
        private string playerToken;
        private string playerID;
        private string status = "登录并选择装备方案，进入单人关卡";
        private string loadout = "ranger";
        private string playMode = "normal";
        private string selectedTask = "";
        private JArray taskCatalogue = new JArray();
        private bool loadingTasks;
        private long clientGeneration;
        private uint inputSequence;
        private GrayboxInput lastIntent;
        private float inputAt;
        private bool busy;
        private bool menu;
        private bool autoplay;
        private bool autoConfirm;
        private bool defeatAutoplay;
        private PveControlClient controlClient;
        private JObject proposal;
        private string resultText;
        private bool joining;
        private bool recoveringResult;
        private readonly Dictionary<string, bool> actionResults = new Dictionary<string, bool>();
        private readonly HashSet<string> capturedStages = new HashSet<string>();
        private string captureDirectory;
        private bool capturing;
        private float resultQueryAt;
        private bool departing;
        private int readinessVersion;
        private int sentReadinessVersion = -1;
        private string resultRun;
        private bool lifecycleCheck;
        private bool lifecycleBusy;
        private bool lifecycleReconnected;
        private string lifecycleFirstRun;
        private bool confirming;
        private bool recoveringLobby;
        private bool lobbyQueued;
        private float lobbyQueryAt;
        private int lobbyStateVersion;
        private string proposalOperation;
        private Vector2 lobbyScroll;
        private bool lobbyVerification;
        private bool lobbyVerificationDelayed;
        private bool lobbyVerificationRejected;
        private bool lobbyVerificationRequeued;

        private static readonly string[] commandLine=Environment.GetCommandLineArgs();
        private static bool? bundledP5Default;
        public static string DemoCacheRoot
        {
            get
            {
                var configured=Environment.GetEnvironmentVariable("DC_CACHE_ROOT");
                if(!string.IsNullOrWhiteSpace(configured))return Path.GetFullPath(configured);
                var project=Environment.GetEnvironmentVariable("DC_PROJECT_ROOT");
                if(string.IsNullOrWhiteSpace(project)&&Application.isEditor)project=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..",".."));
                if(!string.IsNullOrWhiteSpace(project)&&string.Equals(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),Path.GetFullPath("E:/game-realtime-gm-demo"),StringComparison.OrdinalIgnoreCase)&&Directory.Exists("D:/dev-cache/dungeon-contract/private"))return Path.GetFullPath("D:/dev-cache/dungeon-contract");
                if(!string.IsNullOrWhiteSpace(project))
                {
                    var normalized=Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                    if(Path.DirectorySeparatorChar=='\\')normalized=normalized.ToLowerInvariant();
                    using var hasher=SHA256.Create();
                    var identity=string.Concat(hasher.ComputeHash(Encoding.UTF8.GetBytes(normalized)).Select(value=>value.ToString("x2"))).Substring(0,12);
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DungeonContract","projects",identity,"cache");
                }
                return Path.Combine(Application.persistentDataPath,"cache");
            }
        }
        public static string DemoEvidenceRoot
        {
            get
            {
                var evidence=Path.Combine(DemoCacheRoot,"evidence");
                Directory.CreateDirectory(evidence);
                return evidence;
            }
        }
        public static bool IsUnderDemoCache(string path)
        {
            var resolved=Path.GetFullPath(path);
            var root=DemoCacheRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            return resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase);
        }
        public static string Argument(string name, string fallback = null)
        {
            var values = commandLine;
            for (var index = 0; index < values.Length - 1; index++) if (values[index] == name) return values[index + 1];
            if(name=="-dc-runtime"||name=="-dc-p5")
            {
                if(!bundledP5Default.HasValue){var selectedRuntime=Array.IndexOf(values,"-dc-runtime");var compatible=selectedRuntime<0||selectedRuntime+1<values.Length&&(values[selectedRuntime+1]=="v5"||values[selectedRuntime+1]=="v6"||values[selectedRuntime+1]=="v7"||values[selectedRuntime+1]=="v8");bundledP5Default=compatible&&!Application.isEditor&&(File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v8-p5.json"))||File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v5-p5.json"))||File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v7-p5.json"))||File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v6-p5.json")));}
                if(bundledP5Default.Value)return name=="-dc-runtime"?(File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v8-p5.json"))?"v8":File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v7-p5.json"))?"v7":File.Exists(Path.Combine(Application.dataPath,"..","Content","demo-v6-p5.json"))?"v6":"v5"):"true";
            }
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Argument("-dc-runtime", "v2") == "v3" || Argument("-dc-runtime", "v2") == "v4" || Argument("-dc-runtime", "v2") == "v5" || DcV6Profile.Active) return;
            if (SceneManager.GetActiveScene().name != "Bootstrap" || FindFirstObjectByType<DcBootstrap>() != null) return;
            new GameObject("Dungeon Contract").AddComponent<DcBootstrap>();
        }

        private async void Start()
        {
            stopping = new CancellationTokenSource();
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 1048576 };
            role = Argument("-dc-role", "client");
            Application.runInBackground = true;
            battle = Argument("-dc-battle", "true") == "true";
            autoplay = Argument("-dc-graybox-autoplay", "false") == "true";
            defeatAutoplay = Argument("-dc-defeat-autoplay", "false") == "true";
            autoConfirm = autoplay;
            lobbyVerification = Argument("-dc-lobby-confirm-check", "false") == "true";
            autoConfirm = autoConfirm || lobbyVerification;
            lifecycleCheck = Argument("-dc-lifecycle-check", "false") == "true";
            if (role != "server" && role != "client") { Application.Quit(2); return; }
            network = gameObject.AddComponent<NetworkManager>();
            var transport = gameObject.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 12777, "127.0.0.1");
            network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, TickRate = 30, ConnectionApproval = true, EnableSceneManagement = false, ClientConnectionBufferTimeout = 5 };
            Time.fixedDeltaTime = 1f / 30f;
            network.OnClientConnectedCallback += OnConnected;
            network.OnClientDisconnectCallback += OnDisconnected;
            if (role == "server")
            {
                network.ConnectionApprovalCallback += Approve;
                try
                {
                    var path = Argument("-dc-config");
                    token = File.ReadAllText(Argument("-dc-service-credential")).Trim();
                    if (token.Length != 64) throw new InvalidDataException("Service credential missing");
                    var configuration = JObject.Parse(File.ReadAllText(path));
                    boot = Argument("-dc-boot-id", Guid.NewGuid().ToString());
                    var registered = await Internal("instances/register", new JObject { ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["protocol_version"] = (int)configuration["protocol_version"], ["config_versions"] = new JArray(new JObject { ["definition_id"] = configuration["operation"]["version"], ["hash"] = FileHash(path) }) });
                    generation = (long)registered["source_generation"];
                    StartNetworkServer();
                    _ = ServiceLoop();
                }
                catch (Exception error) { Fail(error); Application.Quit(3); }
            }
            else
            {
                view = gameObject.AddComponent<DcGrayboxView>();
                controlClient = gameObject.AddComponent<PveControlClient>();
                controlClient.ServerUrl = "http://127.0.0.1:12880";
                controlClient.Received += OnControl;
                controlClient.ConnectionStateChanged += OnControlConnection;
                var capturePath = Argument("-dc-capture-dir");
                if (capturePath != null)
                {
                    var resolved = Path.GetFullPath(capturePath);
                    if (!IsUnderDemoCache(resolved)) throw new InvalidDataException("Screenshot path must be in Demo cache");
                    captureDirectory = resolved; Directory.CreateDirectory(captureDirectory);
                }
                DontDestroyOnLoad(gameObject);
                SceneManager.LoadScene("Lobby");
                username = Argument("-dc-user", ""); runID = Argument("-dc-run", "");
                loadout = Argument("-dc-loadout", "ranger");
                var passwordPath = Argument("-dc-password-file");
                if (passwordPath != null) password = File.ReadAllText(passwordPath).Trim();
                if (username != "" && lobbyVerification) await Solo();
                else if (username != "" && runID != "") await JoinAssigned();
            }
        }

        private void StartNetworkServer()
        {
            if (!network.StartServer()) throw new InvalidOperationException("Server transport unavailable");
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.loaded", OnLoaded);
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.input", OnInput);
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.action", OnAction);
            UnityEngine.Debug.Log("DC standalone server ready");
        }

        private async Task<JObject> Internal(string path, JObject payload = null)
        {
            using var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Post, "http://127.0.0.1:12890/internal/dc/v1/" + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-DC-Service-ID", "dc_local_service");
            if (payload != null) request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, stopping.Token);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { var code = 0; try { code = (int?)JObject.Parse(text)["code"] ?? 0; } catch (JsonException) { } throw new ServiceFailure((int)response.StatusCode, code); }
            return JObject.Parse(text);
        }

        private async Task ServiceLoop()
        {
            var heartbeatAt = 0d; var failedAt = 0d;
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    if (!committing && !pumpingQuality)
                    {
                        var current = await Internal("instances/dc_local_01/claim", new JObject { ["boot_id"] = boot });
                        if (assignment != null && current["assignment"].Type == JTokenType.Null)
                        {
                            running = false; draining = true;
                            await StopNetwork();
                            connected.Clear(); loaded.Clear(); inputSequences.Clear(); inputs.Clear(); receivedInputAt.Clear(); actions.Clear(); actionHashes.Clear(); actionResults.Clear(); actionOrder.Clear(); actionRates.Clear();
                            assignment = null; world = null; running = false; draining = false; phaseSent = false; hadWait = false;
                            elapsed.Reset(); appliedSequence = 0; controlVersion = 0; lastWorldFrame = 0; heartbeatAt = 0; readinessVersion = 0; sentReadinessVersion = -1;
                            StartNetworkServer();
                        }
                        else if (assignment == null && current["assignment"] is JObject manifest)
                        {
                            assignment = manifest; if (battle) world = new GrayboxWorld(manifest);
                        }
                    }
                    if (Time.realtimeSinceStartupAsDouble >= heartbeatAt)
                    {
                        await Internal("instances/dc_local_01/heartbeat", new JObject { ["boot_id"] = boot, ["assignment_id"] = assignment?["assignment_id"], ["source_generation"] = assignment?["source_generation"] ?? generation, ["instance_status"] = draining ? "draining" : running ? "running" : assignment == null ? "idle" : "loading", ["last_applied_sequence"] = appliedSequence });
                        heartbeatAt = Time.realtimeSinceStartupAsDouble + 2;
                    }
                    if (assignment != null && !running && !draining)
                    {
                        if (sentReadinessVersion != readinessVersion)
                        {
                            var revision = readinessVersion;
                            await Internal("runs/" + assignment["run_id"] + "/start", new JObject { ["ready_only"] = true, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["config_hash"] = assignment["config_hash"], ["loaded_members"] = LoadedMembers() });
                            sentReadinessVersion = revision;
                        }
                        if (loaded.Count == ((JArray)assignment["members"]).Count)
                        {
                            await Internal("runs/" + assignment["run_id"] + "/start", new JObject { ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["config_hash"] = assignment["config_hash"], ["loaded_members"] = LoadedMembers() });
                            elapsed.Start(); running = true;
                        }
                    }
                    if (running && !committing && !pumpingQuality)
                    {
                        committing = true;
                        try
                        {
                            var commands = await ReadCommands();
                            if (!phaseSent || commands.Count > 0 || hadWait)
                            {
                                var phase = hadWait ? "technical_wait" : "active"; hadWait = false;
                                await Publish(new JArray(PhaseFact(phase)), null, null, null, null, 0);
                                phaseSent = true;
                            }
                        }
                        finally { committing = false; }
                    }
                    failedAt = 0;
                    await Task.Delay(100, stopping.Token);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    Fail(error); hadWait = running;
                    if (failedAt == 0) failedAt = Time.realtimeSinceStartupAsDouble;
                    if (Time.realtimeSinceStartupAsDouble - failedAt > 10) { network.Shutdown(); Application.Quit(4); return; }
                    await Task.Delay(1000, stopping.Token);
                }
            }
        }

        private async Task<JArray> ReadCommands()
        {
            var result = await Internal("runs/" + assignment["run_id"] + "/commands?after_control_version=" + controlVersion);
            var commands = (JArray)result["commands"];
            foreach (var command in commands)
            {
                var id = (string)command["payload"]["player_id"];
                world?.Leave(id);
                controlVersion = (long)command["control_version"];
                foreach (var connection in connected.Where(item => (string)item.Value["player_id"] == id).Select(item => item.Key).ToArray()) network.DisconnectClient(connection);
            }
            return commands;
        }

        private JArray LoadedMembers() => new JArray(connected.Values.Where(item => loaded.Contains((string)item["player_id"])).OrderBy(item => (int)item["slot"]).Select(item => new JObject { ["player_id"] = item["player_id"], ["slot"] = item["slot"], ["connection_generation"] = item["connection_generation"] }));

        private void FixedUpdate()
        {
            if (role == "server" && running && phaseSent && world != null && world.IsQuality)
            {
                if (hadWait && !qualityWait) return;
                TickQualityWorld(); return;
            }
            if (role != "server" || !running || !phaseSent || committing || sender.CurrentCount == 0 || hadWait || world == null) return;
            foreach (var input in inputs) world.Input(input.Key, receivedInputAt.TryGetValue(input.Key, out var at) && Time.realtimeSinceStartupAsDouble - at <= 0.25 ? input.Value : default);
            world.AdvanceTime(elapsed.ElapsedMilliseconds);
            var checkpoint = world.Capture();
            var intents = actions.ToArray(); actions.Clear();
            foreach (var intent in intents) SendActionReceipt(intent, connected.TryGetValue(intent.Connection, out var identity) && (string)identity["player_id"] == intent.Player && world.Action(intent.Player, intent.Kind, intent.Intent));
            var tickAt = elapsed.ElapsedMilliseconds;
            var facts = world.Tick(tickAt);
            if (facts.Count > 0 || world.Terminal)
            {
                committing = true;
                _ = CommitTick(new JArray(facts.Count == 0 ? new[] { PhaseFact("active") } : facts), checkpoint, intents, tickAt);
            }
            else BroadcastWorld();
        }

        private async Task CommitTick(JArray facts, JObject checkpoint, ActionIntent[] intents, long tickAt)
        {
            try
            {
                var terminal = world.Terminal ? world.TerminalRequest(Math.Max(lastWorldFrame + 1, world.WorldTick + 1), appliedSequence + 1) : null;
                await Publish(facts, terminal, terminal == null ? null : world.Summary(), checkpoint, intents, tickAt);
                BroadcastWorld(true);
            }
            catch (Exception error) { Fail(error); hadWait = true; await AbortTechnical(); }
            finally { committing = false; }
        }

        private async Task<JObject> Publish(JArray facts, JObject terminal, JObject summary, JObject checkpoint, ActionIntent[] intents, long tickAt)
        {
            await sender.WaitAsync(stopping.Token);
            try
            {
                if (facts.All(fact => (string)fact["kind"] == "presence") && (!running || facts.Any(fact => connected.Values.Any(identity => (string)identity["player_id"] == (string)fact["actor_player_id"])))) return null;
                var frame = new JObject { ["schema_version"] = (int)assignment["config"]["protocol_version"], ["source"] = "unity_game_server", ["run_id"] = assignment["run_id"], ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["sequence"] = appliedSequence + 1, ["frame_id"] = Guid.NewGuid().ToString(), ["world_frame_id"] = Math.Max(lastWorldFrame + 1, world == null ? elapsed.ElapsedMilliseconds * 30 / 1000 : world.WorldTick + 1), ["elapsed_ms"] = elapsed.ElapsedMilliseconds, ["control_version"] = controlVersion, ["facts"] = facts, ["terminal"] = terminal, ["summary"] = summary };
                if (terminal != null) { terminal["cutoff_frame"] = frame["world_frame_id"]; terminal["cutoff_sequence"] = frame["sequence"]; }
                if ((string)terminal?["reason"] == "timeout") frame["elapsed_ms"] = assignment["config"]["operation"]["hard_deadline_ms"];
                var waiting = Stopwatch.StartNew(); var attempts = 0;
                for (;;)
                {
                    frame.Remove("fingerprint"); frame["fingerprint"] = Hash(Encoding.UTF8.GetBytes(Canonical(frame)));
                    try
                    {
                        var receipt = await Internal("runs/" + assignment["run_id"] + "/frames", frame);
                        appliedSequence = (long)receipt["applied_sequence"]; lastWorldFrame = (long)frame["world_frame_id"];
                        world?.Acknowledge(receipt);
                        if ((string)receipt["run_status"] != "running") { running = false; draining = true; }
                        return receipt;
                    }
                    catch (ServiceFailure failure) when (failure.Code == 40982)
                    {
                        var commands = await ReadCommands();
                        if ((long)frame["control_version"] == controlVersion) throw;
                        if (checkpoint != null)
                        {
                            world.Restore(checkpoint);
                            foreach (var member in (JArray)assignment["members"]) { var id = (string)member["player_id"]; world.Presence(id, connected.Values.Any(identity => (string)identity["player_id"] == id)); }
                            foreach (var command in commands) world.Leave((string)command["payload"]["player_id"]);
                            foreach (var intent in intents) SendActionReceipt(intent, connected.TryGetValue(intent.Connection, out var identity) && (string)identity["player_id"] == intent.Player && world.Action(intent.Player, intent.Kind, intent.Intent));
                            var revised = world.Tick(tickAt);
                            frame["facts"] = revised.Count == 0 ? new JArray(PhaseFact("active")) : new JArray(revised);
                            frame["terminal"] = world.Terminal ? world.TerminalRequest((long)frame["world_frame_id"], (long)frame["sequence"]) : null;
                            frame["summary"] = world.Terminal ? world.Summary() : null;
                        }
                        else
                        {
                            var removed = new HashSet<string>(commands.Select(command => (string)command["payload"]["player_id"]));
                            frame["facts"] = new JArray(((JArray)frame["facts"]).Where(fact => fact["actor_player_id"].Type == JTokenType.Null || !removed.Contains((string)fact["actor_player_id"])));
                            if (((JArray)frame["facts"]).Count == 0) frame["facts"] = new JArray(PhaseFact("active"));
                        }
                        frame["control_version"] = controlVersion;
                    }
                    catch (Exception error) when ((error is HttpRequestException || error is TaskCanceledException || error is ServiceFailure service && service.Status == 503) && !stopping.IsCancellationRequested && attempts++ < 2 && waiting.ElapsedMilliseconds < 7000)
                    {
                        hadWait = true; await Task.Delay((1 << (attempts - 1)) * 1000, stopping.Token);
                    }
                    if (waiting.ElapsedMilliseconds >= 10000) throw new TimeoutException("Reliable frame deadline");
                }
            }
            finally { sender.Release(); }
        }

        private async Task AbortTechnical()
        {
            running = false; draining = true;
            try { await Internal("runs/" + assignment["run_id"] + "/abort", new JObject { ["operation_id"] = Guid.NewGuid().ToString(), ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["reason"] = "technical_failure" }); } catch (Exception error) { Fail(error); }
        }

        private async void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = true; response.CreatePlayerObject = false;
            if (request.Payload.Length > 1024) { response.Approved = false; response.Pending = false; return; }
            try
            {
                var deadline = Time.realtimeSinceStartupAsDouble + 4;
                while (assignment == null && Time.realtimeSinceStartupAsDouble < deadline) await Task.Delay(20, stopping.Token);
                if (assignment == null || draining) throw new InvalidDataException("Assignment not ready");
                var credential = JObject.Parse(Encoding.UTF8.GetString(request.Payload));
                var identity = await Internal("admissions/consume", new JObject { ["ticket"] = credential["ticket"], ["client_nonce"] = credential["client_nonce"], ["instance_id"] = "dc_local_01", ["boot_id"] = boot });
                if ((string)identity["run_id"] != (string)assignment["run_id"]) throw new InvalidDataException();
                var old = connected.Where(item => (string)item.Value["player_id"] == (string)identity["player_id"]).Select(item => item.Key).ToArray();
                if (old.Any(connection => (long)connected[connection]["connection_generation"] >= (long)identity["connection_generation"])) throw new InvalidDataException("Stale connection approval");
                if (loaded.Remove((string)identity["player_id"])) readinessVersion++;
                if (old.Length == 0 && connected.Count >= 4) throw new InvalidOperationException();
                foreach (var connection in old) network.DisconnectClient(connection);
                connected[request.ClientNetworkId] = identity; world?.Presence((string)identity["player_id"], true); response.Approved = true;
            }
            catch (Exception error) { response.Approved = false; Fail(error); }
            response.Pending = false;
        }

        private void OnConnected(ulong connection)
        {
            if (role != "server") return;
            using var writer = new FastBufferWriter(65536, Allocator.Temp);
            var publicManifest = (JObject)assignment.DeepClone(); publicManifest.Remove("task_requirements");
            writer.WriteValueSafe(publicManifest.ToString(Formatting.None));
            network.CustomMessagingManager.SendNamedMessage("dc.manifest", connection, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void OnManifest(ulong connection, FastBufferReader reader)
        {
            try
            {
                if (role != "client" || reader.Length > 65536) throw new InvalidDataException();
                reader.ReadValueSafe(out string serialized); assignment = JObject.Parse(serialized);
                receivingFrame = -1; snapshotParts.Clear(); lastSnapshot = null;
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content"));
                if ((string)assignment["config_hash"] != FileHash(Path.Combine(folder, (int)assignment["config"]["protocol_version"] == 2 ? "demo-v2.json" : "demo-v1.json")) || (string)assignment["run_id"] != runID) throw new InvalidDataException("Frozen content mismatch");
                if (assignment["p1_world_hash"] != null && (string)assignment["p1_world_hash"] != FileHash(Path.Combine(folder, (int)assignment["config"]["protocol_version"] == 2 ? "world-v2.json" : "p1-world.v1.json"))) throw new InvalidDataException("P1 world mismatch");
                view.Configure(assignment, playerID);
                if ((int)assignment["config"]["protocol_version"] == 2) QualityPilot.Configure((JObject)assignment["p1_world"]);
                view.SelectChoice = option => SendAction("choose:" + option);
                SceneManager.LoadScene("Dungeon");
                using var writer = new FastBufferWriter(256, Allocator.Temp); writer.WriteValueSafe((string)assignment["config_hash"]);
                network.CustomMessagingManager.SendNamedMessage("dc.loaded", NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                status = "关卡已加载";
            }
            catch (Exception error) { Fail(error); network.Shutdown(); }
        }

        private void OnLoaded(ulong connection, FastBufferReader reader)
        {
            if (!connected.TryGetValue(connection, out var identity) || reader.Length > 256) return;
            try { reader.ReadValueSafe(out string hash); if (hash == (string)assignment["config_hash"] && loaded.Add((string)identity["player_id"])) readinessVersion++; } catch (Exception error) { Fail(error); network.DisconnectClient(connection); }
        }

        private async void OnDisconnected(ulong connection)
        {
            if (role != "server") { status = "战斗连接已断开，可重连查看结果"; if (!string.IsNullOrEmpty(runID)) _ = RecoverResult(); return; }
            if (!connected.TryGetValue(connection, out var identity)) return;
            connected.Remove(connection); inputSequences.Remove(connection); actionRates.Remove(connection);
            if (connected.Values.Any(current => (string)current["player_id"] == (string)identity["player_id"])) return;
            inputs.Remove((string)identity["player_id"]);
            receivedInputAt.Remove((string)identity["player_id"]);
            world?.Presence((string)identity["player_id"], false);
            if (!running) { if (loaded.Remove((string)identity["player_id"])) readinessVersion++; return; }
            if (!running || connected.Values.Any(current => (string)current["player_id"] == (string)identity["player_id"])) return;
            try { await Publish(new JArray(new JObject { ["event_id"] = Guid.NewGuid().ToString(), ["kind"] = "presence", ["actor_player_id"] = identity["player_id"], ["target_id"] = "battle_connection", ["contributors"] = new JArray(), ["action_id"] = null, ["effective_amount"] = 0, ["connected"] = false, ["connection_generation"] = identity["connection_generation"] }), null, null, null, null, 0); } catch (Exception error) { Fail(error); }
        }

        private void OnInput(ulong connection, FastBufferReader reader)
        {
            if (!running || !connected.TryGetValue(connection, out var identity) || reader.Length - reader.Position != 15) return;
            reader.ReadValueSafe(out uint currentGeneration); reader.ReadValueSafe(out uint sequence); reader.ReadValueSafe(out short horizontal); reader.ReadValueSafe(out short vertical); reader.ReadValueSafe(out ushort aim); reader.ReadValueSafe(out byte flags);
            if (currentGeneration != (uint)identity["connection_generation"] || inputSequences.TryGetValue(connection, out var prior) && sequence <= prior || Math.Abs((int)horizontal) > 1000 || Math.Abs((int)vertical) > 1000 || flags > 3) return;
            inputSequences[connection] = sequence;
            receivedInputAt[(string)identity["player_id"]] = Time.realtimeSinceStartupAsDouble;
            inputs[(string)identity["player_id"]] = new GrayboxInput { Move = Vector2.ClampMagnitude(new Vector2(horizontal / 1000f, vertical / 1000f), 1), Aim = aim, Sequence = sequence, Attack = (flags & 1) != 0, Interact = (flags & 2) != 0 };
        }

        private void OnAction(ulong connection, FastBufferReader reader)
        {
            if (!running || !connected.TryGetValue(connection, out var identity) || reader.Length > 1024 || actions.Count >= 32) return;
            try
            {
                reader.ReadValueSafe(out string encoded); if (Encoding.UTF8.GetByteCount(encoded) > 512) return;
                var request = JObject.Parse(encoded); var id = (string)request["command_id"]; var kind = (string)request["action"];
                if (!Guid.TryParseExact(id, "D", out _) || (long?)request["connection_generation"] != (long)identity["connection_generation"] || kind != "ability" && kind != "dodge" && kind != "supply" && kind != "attack" && !kind.StartsWith("choose:", StringComparison.Ordinal)) return;
                if (!actionRates.TryGetValue(connection, out var rate)) actionRates[connection] = rate = new Queue<double>();
                while (rate.Count > 0 && Time.realtimeSinceStartupAsDouble - rate.Peek() >= 1) rate.Dequeue();
                if (rate.Count >= 10) return; rate.Enqueue(Time.realtimeSinceStartupAsDouble);
                var fingerprint = Canonical(request);
                var player = (string)identity["player_id"]; var actionKey = player + ":" + id;
                if (actionHashes.TryGetValue(actionKey, out var prior)) { if (prior != fingerprint) network.DisconnectClient(connection); else if (actionResults.TryGetValue(actionKey, out var accepted)) SendActionReceipt(new ActionIntent { Player = player, ID = id, Connection = connection }, accepted); return; }
                actionHashes[actionKey] = fingerprint; actionOrder.Enqueue(actionKey); if (actionOrder.Count > 256) { var expired = actionOrder.Dequeue(); actionHashes.Remove(expired); actionResults.Remove(expired); }
                var actionIntent = new GrayboxInput { Aim = (ushort?)request["aim"] ?? 0, Sequence = (uint?)request["input_sequence"] ?? 0, Move = new Vector2((float?)request["move_x"] ?? 0, (float?)request["move_y"] ?? 0), HasPlacement = (bool?)request["has_placement"] ?? false, Placement = new Vector2((float?)request["placement_x"] ?? 0, (float?)request["placement_y"] ?? 0) };
                if (actionIntent.Move.sqrMagnitude > 1.01f || !float.IsFinite(actionIntent.Placement.x) || !float.IsFinite(actionIntent.Placement.y) || Math.Abs(actionIntent.Placement.x) > 20 || Math.Abs(actionIntent.Placement.y) > 12) return;
                actions.Enqueue(new ActionIntent { Player = (string)identity["player_id"], Kind = kind, ID = id, Connection = connection, Intent = actionIntent });
            }
            catch (Exception error) { Fail(error); }
        }

        private void SendActionReceipt(ActionIntent intent, bool accepted)
        {
            actionResults[intent.Player + ":" + intent.ID] = accepted;
            if (!connected.ContainsKey(intent.Connection)) return;
            using var writer = new FastBufferWriter(1024, Allocator.Temp); writer.WriteValueSafe(new JObject { ["command_id"] = intent.ID, ["accepted"] = accepted, ["reason"] = world?.ActionReason ?? "inactive" }.ToString(Formatting.None));
            network.CustomMessagingManager.SendNamedMessage("dc.action_receipt", intent.Connection, writer, NetworkDelivery.ReliableSequenced);
        }

        private void BroadcastWorld(bool force = false)
        {
            if (world == null || !force && Time.unscaledTime < snapshotAt) return;
            snapshotAt = Time.unscaledTime + 1f / (int)assignment["p1_world"]["view"]["snapshot_hz"];
            var snapshot = world.Snapshot(); snapshot["run_status"] = running ? "running" : "ending";
            var encoded = snapshot.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(encoded) > 60000) throw new InvalidDataException("Snapshot budget");
            if (world.IsQuality && !force) { BroadcastQualitySnapshot(encoded, world.WorldTick); return; }
            foreach (var connection in connected.Keys.ToArray()) { using var writer = new FastBufferWriter(65536, Allocator.Temp); writer.WriteValueSafe(encoded); network.CustomMessagingManager.SendNamedMessage("dc.world", connection, writer, NetworkDelivery.ReliableFragmentedSequenced); }
        }

        private void OnWorld(ulong connection, FastBufferReader reader)
        {
            try { if (role != "client" || reader.Length > 65536) return; reader.ReadValueSafe(out string encoded); lastSnapshot = JObject.Parse(encoded); view.Receive(lastSnapshot); if ((string)lastSnapshot["run_status"] == "ending") _ = RecoverResult(); if (lobbyVerification && lobbyVerificationRejected && (string)lastSnapshot["run_status"] == "running") { var output = Path.GetFullPath(Argument("-dc-lobby-output")); if (!IsUnderDemoCache(output)) throw new InvalidDataException("Lobby evidence path"); File.WriteAllText(output, new JObject { ["run_id"] = runID, ["expired_confirmation_rejected"] = true, ["requeue_succeeded"] = true, ["world_snapshot_received"] = true }.ToString(Formatting.None)); lobbyVerification = false; } }
            catch (Exception error) { Fail(error); }
        }

        private void Update()
        {
            if (role == "client" && controlClient != null && controlClient.HasSession && assignment == null && !busy && !confirming && !joining)
            {
                if (proposal != null && ProposalSeconds(proposal) <= 0) { ClearProposal(); status = "确认已超时，请点击重新排队，并在倒计时内确认"; }
                if (Time.unscaledTime >= lobbyQueryAt) { lobbyQueryAt = Time.unscaledTime + 2; _ = RecoverLobby(); }
                if (lobbyVerification && lobbyVerificationRejected && !lobbyVerificationRequeued && !lobbyQueued && !recoveringLobby) { lobbyVerificationRequeued = true; _ = Solo(); }
            }
            if (role == "client" && lifecycleCheck && !lifecycleBusy) _ = CheckLifecycle();
            if (role == "client" && captureDirectory != null && !capturing && Time.unscaledTime > 4)
            {
                var stage = lastSnapshot == null ? "lobby" : (string)lastSnapshot["run_status"] == "ending" ? "result" : "room-" + lastSnapshot["room"];
                if (capturedStages.Add(stage)) StartCoroutine(CaptureClient(stage));
            }
            if (role == "client" && resultText != null && Time.unscaledTime >= resultQueryAt) { resultQueryAt = Time.unscaledTime + 2; _ = RecoverResult(); }
            if (role != "client" || network == null || !network.IsConnectedClient) return;
            if (Input.GetKeyDown(KeyCode.Escape)) menu = !menu;
            view.ReadLocalInput(menu || !Application.isFocused);
            if (!menu && Application.isFocused && !autoplay && Input.GetMouseButtonDown(0) && !view.PointerOverPanel()) { view.AttackStart(); SendAction("attack"); }
            if (Time.unscaledTime >= inputAt)
            {
                inputAt = Time.unscaledTime + 1f / 30f;
                var intent = autoplay && lastSnapshot != null ? defeatAutoplay ? GrayboxPilot.DefeatDecide(lastSnapshot, playerID) : GrayboxPilot.Decide(lastSnapshot, playerID) : new GrayboxInput { Move = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1), Aim = view.MouseAim(), Attack = Input.GetMouseButton(0) && !view.PointerOverPanel(), Interact = Input.GetKey(KeyCode.E) };
                if (!Application.isFocused && !autoplay || menu) intent = default;
                intent.Sequence = ++inputSequence; lastIntent = intent;
                view.RecordInput(intent, menu || !Application.isFocused);
                using var writer = new FastBufferWriter(32, Allocator.Temp); writer.WriteValueSafe((uint)clientGeneration); writer.WriteValueSafe(intent.Sequence); writer.WriteValueSafe((short)(intent.Move.x * 1000)); writer.WriteValueSafe((short)(intent.Move.y * 1000)); writer.WriteValueSafe(intent.Aim); writer.WriteValueSafe((byte)((intent.Attack ? 1 : 0) | (intent.Interact ? 2 : 0)));
                network.CustomMessagingManager.SendNamedMessage("dc.input", NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
                if (autoplay && !defeatAutoplay) { var action = GrayboxPilot.Action(lastSnapshot, playerID); if (action != null && Time.unscaledTime >= pilotActionAt) { pilotActionAt = Time.unscaledTime + 0.12f; SendAction(action); } }
            }
            if (!menu && Application.isFocused && !autoplay) { if (Input.GetKeyDown(KeyCode.Space)) SendAction("dodge"); if (Input.GetKeyDown(KeyCode.Q)) SendAction("ability"); if (Input.GetKeyDown(KeyCode.F)) SendAction("supply"); }
        }

        private void SendAction(string kind)
        {
            var placement = kind == "ability" ? autoplay && lastSnapshot != null ? QualityPilot.Placement(lastSnapshot, playerID) : view.MouseWorld() : Vector2.zero;
            using var writer = new FastBufferWriter(1024, Allocator.Temp); writer.WriteValueSafe(new JObject { ["command_id"] = Guid.NewGuid().ToString(), ["connection_generation"] = clientGeneration, ["input_sequence"] = lastIntent.Sequence, ["aim"] = autoplay ? lastIntent.Aim : view.MouseAim(), ["move_x"] = lastIntent.Move.x, ["move_y"] = lastIntent.Move.y, ["has_placement"] = kind == "ability", ["placement_x"] = placement.x, ["placement_y"] = placement.y, ["action"] = kind }.ToString(Formatting.None));
            network.CustomMessagingManager.SendNamedMessage("dc.action", NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            if (kind == "dodge") view.PredictDodge();
        }

        private float pilotActionAt;

        private async Task CheckLifecycle()
        {
            lifecycleBusy = true;
            try
            {
                if (lifecycleFirstRun == null && resultRun == runID && !string.IsNullOrEmpty(runID))
                {
                    lifecycleFirstRun = runID; ReturnToLobby(); await Solo();
                }
                else if (lifecycleFirstRun != null && runID != lifecycleFirstRun && lastSnapshot != null)
                {
                    if (!lifecycleReconnected && (long)lastSnapshot["elapsed_ms"] >= 2000)
                    {
                        var previousGeneration = clientGeneration;
                        network.Shutdown(); await Task.Delay(300, stopping.Token); await JoinWithControl();
                        lifecycleReconnected = clientGeneration > previousGeneration;
                    }
                    else if (lifecycleReconnected && network.IsConnectedClient && (long)lastSnapshot["elapsed_ms"] >= 5000)
                    {
                        var secondRun = runID;
                        await LeaveRun();
                        if (!string.IsNullOrEmpty(runID)) throw new InvalidOperationException("Lifecycle exit failed");
                        var output = Path.GetFullPath(Argument("-dc-lifecycle-output"));
                        if (!IsUnderDemoCache(output)) throw new InvalidDataException("Lifecycle output path");
                        File.WriteAllText(output, new JObject { ["first_run_id"] = lifecycleFirstRun, ["second_run_id"] = secondRun, ["same_client_session"] = true, ["reconnected"] = true, ["returned_to_lobby"] = true }.ToString(Formatting.None));
                        lifecycleCheck = false;
                    }
                }
            }
            catch (Exception error) { lifecycleCheck = false; Fail(error); }
            finally { lifecycleBusy = false; }
        }

        private IEnumerator CaptureClient(string stage)
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory, stage + ".png"));
            capturing = false;
        }

        private async Task Solo()
        {
            if (busy) return; busy = true;
            try { if (!controlClient.HasSession) { await controlClient.Login(username, password); password = ""; } else if (!controlClient.IsConnected) await controlClient.Connect(); playerID = controlClient.PlayerId.ToString(); var task = new JObject { ["loadout_id"] = loadout }; if (!string.IsNullOrEmpty(selectedTask)) { task["task_key"] = selectedTask; task["task_version"] = "dc_ruins.v2"; } var plan = new JObject { ["operation"] = "dc_ruins", ["difficulty"] = "standard", ["rule_version"] = "dc_ruins.v2", ["fill_policy"] = "no_fill", ["allow_partial"] = true, ["mode"] = playMode }; if (playMode == "trial") { plan["trial_id"] = "dc_ruins_trial"; plan["trial_version"] = "trial.v2"; } await controlClient.Request("v2.match.enqueue", new { plan, task }); lobbyQueued = true; lobbyStateVersion++; if (proposal == null && string.IsNullOrEmpty(runID)) status = "正在等待实例与匹配确认"; }
            catch (Exception error) { Fail(error); } finally { busy = false; }
        }

        private async void OnControl(JObject notification)
        {
            try
            {
                var kind = (string)notification["type"];
                if (kind == "v2.match.proposal") { OfferProposal((JObject)notification["data"]); if (captureDirectory != null && !capturing) StartCoroutine(CaptureClient("proposal")); if (autoConfirm) { var delay = lobbyVerification && !lobbyVerificationDelayed ? 16000 : 0; lobbyVerificationDelayed = lobbyVerificationDelayed || lobbyVerification; await Confirm(delay); } }
                if (kind == "v2.match.assigned") await JoinRun((string)notification["data"]["run_id"]);
                if ((kind == "v2.run.result" || kind == "v2.participant.result") && (string)notification["data"]?["run_id"] == runID) await RecoverResult();
            }
            catch (Exception error) { Fail(error); }
        }

        private async Task Confirm(int verificationDelayMs = 0)
        {
            if (proposal == null || confirming) return;
            confirming = true;
            var selected = proposal; var operation = proposalOperation;
            status = "正在确认并分配关卡，请稍候";
            try
            {
                if (verificationDelayMs > 0) await Task.Delay(verificationDelayMs, stopping.Token);
                if (!controlClient.IsConnected) await controlClient.Connect();
                var response = await controlClient.Request("v2.match.proposal_confirm", new { proposal_id = (string)selected["proposal_id"], revision = (long)selected["revision"] }, operation);
                if (proposal == selected) ClearProposal();
                var assigned = (string)response["data"]?["run_id"];
                if (assigned != null) await JoinRun(assigned);
                else status = "本人已确认，等待其余成员确认";
            }
            catch (PveControlClient.CommandRejectedException error)
            {
                if (lobbyVerification && error.Code == 40970) lobbyVerificationRejected = true;
                if (proposal == selected) ClearProposal();
                await RecoverLobby();
                if (assignment == null && string.IsNullOrEmpty(runID)) status = error.Code == 40970 ? "候选已过期或状态已变化，请重新排队并及时确认" : "确认被拒绝（" + error.Code + "），请恢复状态后重试";
            }
            catch (Exception)
            {
                await RecoverLobby();
                if (assignment == null && string.IsNullOrEmpty(runID)) status = "确认结果暂不可用，已尝试恢复当前状态；可重连大厅或重新排队";
            }
            finally { confirming = false; }
        }

        private static double ProposalSeconds(JObject candidate)
        {
            if (!DateTimeOffset.TryParse((string)candidate?["deadline_at"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var deadline)) return 0;
            return Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
        }

        private void ClearProposal() { proposal = null; proposalOperation = null; lobbyStateVersion++; }

        private void OfferProposal(JObject candidate)
        {
            var id = (string)candidate["proposal_id"] ?? (string)candidate["id"];
            if (string.IsNullOrEmpty(id) || (long?)candidate["revision"] == null) throw new InvalidDataException("Invalid proposal");
            if ((string)proposal?["proposal_id"] != id) proposalOperation = Guid.NewGuid().ToString();
            proposal = (JObject)candidate.DeepClone(); proposal["proposal_id"] = id;
            lobbyQueued = true; lobbyStateVersion++;
            status = "已找到关卡，请在倒计时内点击确认进入";
        }

        private async Task JoinRun(string assigned)
        {
            if (string.IsNullOrEmpty(assigned)) return;
            if (runID == assigned && (joining || network.IsListening || assignment != null)) return;
            runID = assigned; lobbyQueued = false; ClearProposal();
            status = "已分配关卡，正在连接战斗服务";
            await JoinWithControl();
        }

        private async Task RecoverLobby()
        {
            if (recoveringLobby || !controlClient.HasSession || assignment != null) return;
            recoveringLobby = true;
            var version = lobbyStateVersion;
            try
            {
                var response = await controlClient.Query("/api/v2/me/activity");
                if (version != lobbyStateVersion || assignment != null) return;
                var activity = (JObject)response["data"];
                var assigned = (string)activity["run"]?["id"] ?? (string)activity["settlement_run_id"];
                if (assigned != null) { await JoinRun(assigned); return; }
                if (activity["proposal"] is JObject candidate && ProposalSeconds(candidate) > 0) { OfferProposal(candidate); return; }
                var hadProposal = proposal != null;
                ClearProposal();
                lobbyQueued = (string)activity["activity_lock"]?["type"] == "ticket";
                if (lobbyQueued) status = "正在等待实例与新的匹配确认";
                else if (hadProposal) status = "确认已超时或候选已撤销，请重新排队";
                if (lobbyVerification && lobbyVerificationRejected && !lobbyQueued && captureDirectory != null && !capturing && capturedStages.Add("lobby-recovered")) StartCoroutine(CaptureClient("lobby-recovered"));
            }
            catch (Exception error) { Fail(error); }
            finally { recoveringLobby = false; }
        }

        private void OnControlConnection(string state)
        {
            if (state == "disconnected" && assignment == null) status = "大厅连接已断开，请点重新连接大厅恢复状态";
            if (state.StartsWith("Session expired", StringComparison.Ordinal)) { lobbyQueued = false; ClearProposal(); playerID = null; status = "登录已失效，请重新输入密码登录"; }
        }

        private async Task ResumeLobby()
        {
            if (busy || confirming) return; busy = true;
            try { await controlClient.Connect(); await RecoverLobby(); }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private async Task CancelQueue()
        {
            if (busy || confirming) return; busy = true;
            try { await controlClient.Request("v2.match.cancel", new { }); lobbyQueued = false; ClearProposal(); status = "已取消排队，可重新选择方案并开始"; }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private async Task JoinWithControl() { if (joining) return; joining = true; try { var credential = await controlClient.Post("/api/v2/runs/" + runID + "/admissions", new { operation_id = Guid.NewGuid().ToString() }); await Connect(credential); } finally { joining = false; } }

        private async Task JoinAssigned()
        {
            try
            {
                if (lifecycleCheck) { await controlClient.Login(username, password); playerID = controlClient.PlayerId.ToString(); password = ""; await JoinWithControl(); return; }
                using var response = await http.PostAsync("http://127.0.0.1:12880/api/login", new StringContent(new JObject { ["username"] = username, ["password"] = password }.ToString(Formatting.None), Encoding.UTF8, "application/json"), stopping.Token);
                response.EnsureSuccessStatusCode(); var login = JObject.Parse(await response.Content.ReadAsStringAsync()); playerToken = (string)login["data"]["token"]; playerID = (string)login["data"]["player"]["id"];
                using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:12880/api/v2/runs/" + runID + "/admissions"); request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", playerToken); request.Content = new StringContent(new JObject { ["operation_id"] = Guid.NewGuid().ToString() }.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using var admission = await http.SendAsync(request, stopping.Token); admission.EnsureSuccessStatusCode(); await Connect(JObject.Parse(await admission.Content.ReadAsStringAsync())); password = "";
            }
            catch (Exception error) { Fail(error); }
        }

        private async Task StopNetwork()
        {
            if (network.IsListening && !network.ShutdownInProgress) network.Shutdown();
            var deadline = Time.realtimeSinceStartupAsDouble + 6;
            while (network.IsListening || network.ShutdownInProgress)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("Network shutdown deadline");
                await Task.Delay(20, stopping.Token);
            }
        }

        private async Task Connect(JObject admission)
        {
            clientGeneration = (long)admission["connection_generation"]; inputSequence = 0;
            await StopNetwork();
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(new JObject { ["ticket"] = admission["ticket"], ["client_nonce"] = Guid.NewGuid().ToString() }.ToString(Formatting.None));
            if (!network.StartClient()) throw new InvalidOperationException();
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.manifest", OnManifest);
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.world", OnWorld);
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.world.part", OnQualityPart);
            network.CustomMessagingManager.RegisterNamedMessageHandler("dc.action_receipt", (connection, reader) => { try { if (connection != NetworkManager.ServerClientId || reader.Length > 1024) return; reader.ReadValueSafe(out string data); var receipt = JObject.Parse(data); view.ActionFeedback((bool)receipt["accepted"], (string)receipt["reason"]); } catch (Exception error) { Fail(error); } });
        }

        private async Task RecoverResult()
        {
            if (recoveringResult || string.IsNullOrEmpty(runID)) return; recoveringResult = true;
            var requestedRun = runID;
            try
            {
                JObject result;
                if (controlClient.HasSession) result = await controlClient.Query("/api/v2/runs/" + requestedRun + "/results");
                else { using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:12880/api/v2/runs/" + requestedRun + "/results"); request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", playerToken); using var response = await http.SendAsync(request, stopping.Token); response.EnsureSuccessStatusCode(); result = JObject.Parse(await response.Content.ReadAsStringAsync()); }
                if (runID != requestedRun) return;
                var data = result["data"];
                var reward = data?["reward_grants"] is JArray grants ? grants.Sum(item => (long)item["amount"]) : 0;
                resultText = data == null || string.IsNullOrEmpty((string)data["end_reason"]) ? null : "结果：" + data["end_reason"] + "\n结算状态：" + ((bool?)data["settled"] == true ? "已可靠结算" : "等待Worker确认") + "\n本人奖励：" + reward + "软货币";
                if (resultText != null && data["contribution_cards"] is JArray cards) resultText += "\n" + string.Join("\n", cards.Select(card => card["nickname"] + " 伤害 " + card["effective_damage"] + " 击杀 " + card["eligible_kills"] + " 助攻 " + card["assists"] + " 机关 " + card["interactions"] + " 死亡 " + card["deaths"]));
                if ((bool?)data?["settled"] == true) resultRun = requestedRun;
            }
            catch (Exception error) { Fail(error); }
            finally { recoveringResult = false; }
        }

        private async Task Register()
        {
            if (busy) return; busy = true;
            try { using var response = await http.PostAsync("http://127.0.0.1:12880/api/register", new StringContent(new JObject { ["username"] = username, ["password"] = password, ["nickname"] = username }.ToString(Formatting.None), Encoding.UTF8, "application/json"), stopping.Token); response.EnsureSuccessStatusCode(); status = "账号已创建，可开始单人关卡"; }
            catch (Exception error) { Fail(error); } finally { busy = false; }
        }

        private void OnGUI()
        {
            if (role != "client") return;
            view?.ApplySkin();
            if (assignment == null)
            {
                GUILayout.BeginArea(new Rect(30, 30, 440, Mathf.Min(600, Screen.height - 60)), GUI.skin.box);
                lobbyScroll = GUILayout.BeginScrollView(lobbyScroll);
                GUILayout.Label("Dungeon Contract 1.1 · 合作地牢"); GUILayout.Label(status);
                GUILayout.Label("账号"); username = GUILayout.TextField(username, 64); GUILayout.Label("密码"); password = GUILayout.PasswordField(password, '*', 72);
                loadout = GUILayout.SelectionGrid(loadout == "ranger" ? 0 : 1, new[] { "遗迹游侠 · 弩", "符文工匠 · 炮塔" }, 2) == 0 ? "ranger" : "runesmith";
                playMode = GUILayout.SelectionGrid(playMode == "normal" ? 0 : 1, new[] { "普通随机", "固定试炼" }, 2) == 0 ? "normal" : "trial";
                if (controlClient.HasSession && !busy && !lobbyQueued && GUILayout.Button(loadingTasks ? "读取合同中..." : "读取个人合同与进度")) _ = LoadTasks();
                if (GUILayout.Button(string.IsNullOrEmpty(selectedTask) ? "✓ 不激活个人合同" : "不激活个人合同")) selectedTask = "";
                foreach (var entry in taskCatalogue)
                {
                    var task = entry["task"]; var progress = entry["progress"]; var available = (bool?)progress["unlocked"] == true && (bool?)progress["completed"] != true;
                    GUI.enabled = available && !lobbyQueued;
                    if (GUILayout.Button(((string)task["key"] == selectedTask ? "✓ " : "") + TaskTitle((string)task["key"]) + " · " + (available ? "可选择" : (bool?)progress["completed"] == true ? "已完成" : "未解锁") + " · " + progress["counts"])) selectedTask = (string)task["key"];
                    GUI.enabled = true;
                }
                if (controlClient.HasSession) { GUILayout.Label("当前已登录：" + playerID); if (!busy && !confirming && !lobbyQueued && string.IsNullOrEmpty(runID) && GUILayout.Button("切换账号")) { _ = controlClient.Disconnect(); Destroy(controlClient); controlClient = gameObject.AddComponent<PveControlClient>(); controlClient.ServerUrl = "http://127.0.0.1:12880"; controlClient.Received += OnControl; controlClient.ConnectionStateChanged += OnControlConnection; ClearProposal(); playerID = null; username = ""; password = ""; } }
                GUI.enabled = !busy && !confirming && !lobbyQueued && string.IsNullOrEmpty(runID); if (GUILayout.Button("创建测试账号")) _ = Register(); if (GUILayout.Button(controlClient.HasSession ? "重新排队进入单人关卡" : "登录并进入单人关卡")) _ = Solo(); GUI.enabled = true;
                if (proposal != null) { GUILayout.Label("确认剩余 " + Mathf.CeilToInt((float)ProposalSeconds(proposal)) + " 秒；超时后需要重新排队"); GUI.enabled = !confirming && !busy; if (GUILayout.Button(confirming ? "正在确认..." : "确认进入")) _ = Confirm(); GUI.enabled = true; }
                if (lobbyQueued && !confirming && GUILayout.Button("取消排队")) _ = CancelQueue();
                if (controlClient.HasSession && !controlClient.IsConnected && !busy && !confirming && GUILayout.Button("重新连接大厅")) _ = ResumeLobby();
                GUILayout.Label("WASD移动 · 鼠标瞄准/左键射击\nSpace闪避 · Q技能 · E持续操作 · F补给\nEsc菜单，世界不会暂停"); GUILayout.EndScrollView(); GUILayout.EndArea();
            }
            else
            {
                view.DrawHUD(status, resultText);
                if (resultRun == runID && GUI.Button(new Rect(Screen.width / 2 - 120, 315, 240, 35), "返回大厅")) ReturnToLobby();
                if (!network.IsConnectedClient && resultText == null && !joining && GUI.Button(new Rect(30, 110, 240, 35), "重连本局")) _ = JoinWithControl();
                if (menu) { GUILayout.BeginArea(new Rect(30, 110, 350, 190), GUI.skin.box); GUILayout.Label("菜单 · 关卡继续运行"); if (GUILayout.Button("继续")) menu = false; GUI.enabled = !departing; if (GUILayout.Button("退出本局")) _ = LeaveRun(); GUI.enabled = true; GUILayout.EndArea(); }
            }
        }

        private void ReturnToLobby() { runID = ""; resultRun = null; network.Shutdown(); view.Clear(); assignment = null; lastSnapshot = null; resultText = null; lobbyQueued = false; ClearProposal(); menu = false; status = "选择方案后可以再次进入关卡"; SceneManager.LoadScene("Lobby"); }
        private async Task LoadTasks() { if (loadingTasks) return; loadingTasks = true; try { var response = await controlClient.Query("/api/v2/tasks?rule_version=dc_ruins.v2"); taskCatalogue = response["data"] as JArray ?? new JArray(); if (taskCatalogue.Any(entry => (string)entry["task"]["key"] == selectedTask && (bool?)entry["progress"]["completed"] == true)) selectedTask = ""; } catch (Exception error) { Fail(error); } finally { loadingTasks = false; } }
        private static string TaskTitle(string key) => key == "dc_hunter" ? "协作猎手" : key == "dc_precision" ? "精确打击（有效动作次数）" : key == "dc_scout" ? "遗迹勘察" : key == "dc_technician" ? "机关维护" : key == "dc_inspector" ? "枢纽巡检" : "稳健作战";
        private async Task LeaveRun() { if (departing) return; departing = true; try { await controlClient.Request("v2.run.leave", new { run_id = runID }); ReturnToLobby(); } catch (Exception error) { Fail(error); } finally { departing = false; } }
        private void Fail(Exception error) { status = "连接或服务不可用：" + error.GetType().Name; UnityEngine.Debug.LogWarning(status + (role == "server" && error is ServiceFailure failure ? " HTTP=" + failure.Status + " code=" + failure.Code : "")); }
        private void OnApplicationQuit() { stopping?.Cancel(); network?.Shutdown(); http?.Dispose(); }
        private static JObject PhaseFact(string phase) => new JObject { ["event_id"] = Guid.NewGuid().ToString(), ["kind"] = "phase", ["actor_player_id"] = null, ["target_id"] = "world_phase", ["contributors"] = new JArray(), ["action_id"] = null, ["effective_amount"] = 0, ["phase"] = phase };
        private static string FileHash(string path) => Hash(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n")));
        private static string Hash(byte[] data) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        private static string Canonical(JToken value) { if (value is JObject objectValue) return "{" + string.Join(",", objectValue.Properties().OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => JsonConvert.SerializeObject(item.Name) + ":" + Canonical(item.Value))) + "}"; if (value is JArray array) return "[" + string.Join(",", array.Select(Canonical)) + "]"; return value.ToString(Formatting.None); }
    }
}
