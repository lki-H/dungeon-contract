using System;
using System.Collections.Generic;
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
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP0V3 : MonoBehaviour
    {
        private sealed class RequestFailure : Exception
        {
            public int Status;
            public int Code;
            public string Route;
            public long? Frame,Sequence;
            public RequestFailure(int status,int code) : base("P0 request rejected") { Status=status;Code=code; }
        }
        private readonly Dictionary<ulong, JObject> identities = new Dictionary<ulong, JObject>();
        private readonly HashSet<string> loaded = new HashSet<string>();
        private readonly Dictionary<ulong, uint> sequences = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, Queue<double>> inputRates = new Dictionary<ulong, Queue<double>>();
        private readonly Dictionary<ulong, Queue<double>> actionRates = new Dictionary<ulong, Queue<double>>();
        private CancellationTokenSource stopping;
        private HttpClient http;
        private NetworkManager network;
        private JObject configuration;
        private JObject versions;
        private JObject assignment;
        private string configHash; private string qaFailureReason;
        private string role;
        private string boot;
        private string serviceToken;
        private string publicHost="127.0.0.1";
        private string publicBase=>"http://"+publicHost+":12880";
        private string runID;
        private string playerID;
        private long connectionGeneration;
        private long sourceGeneration;
        private long appliedSequence;
        private long worldFrame;
        private long controlVersion;
        private readonly System.Diagnostics.Stopwatch runClock = new System.Diagnostics.Stopwatch();
        private bool running;
        private uint sentSequence;
        private float nextInput;
        private bool actionSent;
        private bool loadedConfirmed;
        private bool actionRejected;
        private long inputAck;
        private InputActionAsset inputAsset;
        private InputActionMap gameplayMap;
        private InputActionMap menuMap;
        private InputAction moveAction;
        private InputAction aimAction;
        private InputAction rangedAction;
        private InputAction meleeAction;
        private string status = "P0 protocol3 · loading";
        private Text statusLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if(DcBootstrap.Argument("-dc-p5-authority-probe","false")=="true")return;
            if(DcP5Lobby.ShouldStart)return;
            if ((DcBootstrap.Argument("-dc-runtime", "v2") != "v3" && DcBootstrap.Argument("-dc-runtime", "v2") != "v4" && DcBootstrap.Argument("-dc-runtime", "v2") != "v5" && !DcV6Profile.Active) || DcBootstrap.Argument("-dc-practice","false")=="true" || DcBootstrap.Argument("-dc-controls-check","false")=="true" || DcBootstrap.Argument("-dc-v5-controls-check","false")=="true" || DcBootstrap.Argument("-dc-s2-controls-check","false")=="true" || SceneManager.GetActiveScene().name != "Bootstrap" || FindFirstObjectByType<DcP0V3>() != null) return;
            new GameObject("Dungeon Contract P0 V3").AddComponent<DcP0V3>();
        }

        private async void Start()
        {
            stopping = new CancellationTokenSource();
            http = new HttpClient(new HttpClientHandler{UseProxy=false}) { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 1048576 };
            Application.runInBackground = true;
            try
            {
                role = DcBootstrap.Argument("-dc-role", "client");
                if (role != "client" && role != "server") throw new InvalidDataException("Invalid startup role");
                v5Runtime=DcV6Profile.Modern;
                if(v5Runtime&&DcP5Settings.Active&&role=="server"){QualitySettings.vSyncCount=0;Application.targetFrameRate=60;}
                p4Stage=v5Runtime||DcBootstrap.Argument("-dc-stage","p0")=="p4";
                p3Stage=p4Stage||DcBootstrap.Argument("-dc-stage","p0")=="p3";
                multiplayerStage=p3Stage||DcBootstrap.Argument("-dc-stage","p0")=="p2-mp";
                p2Stage=multiplayerStage||DcBootstrap.Argument("-dc-stage","p0")=="p2";
                weaponStage=p2Stage||DcBootstrap.Argument("-dc-stage","p0")=="s2";
                syncFoundation = v5Runtime||DcBootstrap.Argument("-dc-runtime","v2") == "v4";
                p1 = syncFoundation || DcBootstrap.Argument("-dc-stage","p0")=="p1";
                var path = role == "server" ? DcBootstrap.Argument("-dc-config") : Path.Combine(Application.dataPath, "..", "Content", DcV8Profile.Active?"demo-v8-p5.json":DcV7Profile.Active?"demo-v7-p5.json":DcV6Profile.Active?"demo-v6-p5.json":v5Runtime?(DcP5Settings.Active?"demo-v5-p5.json":"demo-v5-p4.json"):p4Stage?"demo-v4-p4.json":p3Stage?"demo-v4-p3.json":multiplayerStage?"demo-v4-p2-mp.json":p2Stage?"demo-v4-p2-1.json":weaponStage?"demo-v4-s2.json":syncFoundation?"demo-v4-s1.json":p1?"demo-v3-p1.json":"demo-v3-p0.json");
                publicHost=DcLanAddress.Validate(DcBootstrap.Argument("-dc-server-host","127.0.0.1"));
                if(!p4Stage&&publicHost!="127.0.0.1")throw new InvalidDataException("Custom network addresses require the P4 player entry");
                if(role=="client"&&publicHost!="127.0.0.1")http.Timeout=TimeSpan.FromSeconds(10);
                configuration = StrictJSON(File.ReadAllText(path)); configHash = FileHash(path); versions = BuildVersions(configuration);
                ValidateVersions(versions);
                if ((string)configuration["lifecycle_stage"] != RuntimeStage) throw new InvalidDataException("Runtime stage mismatch");
                if (role == "client") { CheckClientSecrets(); ConfigureInput(); CreateStatusUI(); }
                network = gameObject.AddComponent<NetworkManager>();
                var transport = gameObject.AddComponent<UnityTransport>(); transport.SetConnectionData(publicHost, DcV6Profile.TestTransportPort(role,publicHost,DcBootstrap.Argument("-dc-v6-test-udp-port","12777"),DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"||DcBootstrap.Argument("-dc-p2-network-probe","false")=="true"), DcLanAddress.Validate(DcBootstrap.Argument("-dc-listen-host","127.0.0.1")));
                if(DcV6Profile.Active){transport.MaxPacketQueueSize=512;transport.MaxSendQueueSize=1048576;}
                network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, TickRate = 30, ConnectionApproval = true, EnableSceneManagement = false, ClientConnectionBufferTimeout = 5 };
                network.OnClientConnectedCallback += Connected;
                network.OnClientDisconnectCallback += Disconnected;
                if (role == "server")
                {
                    serviceToken = File.ReadAllText(DcBootstrap.Argument("-dc-service-credential")).Trim();
                    if (serviceToken.Length != 64) throw new InvalidDataException("Service identity missing");
                    boot = p5ServiceBoot??DcBootstrap.Argument("-dc-boot-id", Guid.NewGuid().ToString());
                    var request = new JObject { ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["protocol_version"] = Protocol, ["versions"] = versions, ["config_versions"] = new JArray(new JObject { ["definition_id"] = configuration["control_rule_version"], ["hash"] = configHash }) };
                    var registered = await Internal("instances/register", request); sourceGeneration = (long)registered["source_generation"];
                    network.ConnectionApprovalCallback = Approve;
                    if (!network.StartServer()) throw new InvalidOperationException("UDP service unavailable");
                    network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.loaded.v3"), Loaded);
                    network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.input.v3"), InputMessage);
                    network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.action.v3"), ActionMessage);
                    if(v5Runtime)network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.baseline-ack.v5"),V5BaselineAck);
                    if(DcV6Profile.Active){network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.lifecycle-ack.v6"),V6LifecycleAck);network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.resync.v6"),V6RequestBaseline);}
                    if(DcV6Profile.P5((string)configuration["definition_id"]))network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.marker.v5"),ReceiveP5Marker);
                    foreach (var legacyName in new[] { "dc.loaded", "dc.input", "dc.action" }) network.CustomMessagingManager.RegisterNamedMessageHandler(legacyName, RejectLegacy);
                    Debug.Log("DC "+RuntimeStage+" protocol"+Protocol+" independent service ready");
                    SaveEvidence(true);
                    if(syncFoundation)await S1ServiceLoop();else await ServiceLoop();
                    if(DcV6Profile.P5((string)configuration["definition_id"]))await RotateP5Service();
                }
                else await Join();
            }
            catch (Exception failure) { if(stopping.IsCancellationRequested)return;if(DcV8Profile.Active&&failure is InvalidDataException dataFailure)qaFailureReason=dataFailure.Message.Substring(0,Math.Min(160,dataFailure.Message.Length));if(DcV8Profile.Active&&failure is RequestFailure serviceFailure){var detail="HTTP "+serviceFailure.Status+" code "+serviceFailure.Code+" route="+serviceFailure.Route+" sequence="+serviceFailure.Sequence+" frame="+serviceFailure.Frame;qaFailureReason=detail.Substring(0,Math.Min(160,detail.Length));}status = p1?"P1 connection or version rejected":"P0 connection or version rejected"; Debug.LogError(status+": "+failure.GetType().Name+(failure is RequestFailure request?" HTTP "+request.Status+" code "+request.Code+" route="+request.Route+" sequence="+request.Sequence+" frame="+request.Frame:""));SaveEvidence(false); Application.Quit(2); }
        }

        private async Task<JObject> Internal(string path, JObject payload = null)
        {
            return await Request("http://127.0.0.1:12890/internal/dc/v1/" + path, serviceToken, payload, true);
        }

        private async Task<JObject> Request(string url, string token, JObject payload, bool internalService = false)
        {
            using var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (internalService) request.Headers.Add("X-DC-Service-ID", "dc_local_service");
            if (payload != null) request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, stopping.Token);
            var responseText=await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) {var code=0;try{code=(int?)StrictJSON(responseText)["code"]??0;}catch(JsonException){}var route=new Uri(url).AbsolutePath;throw new RequestFailure((int)response.StatusCode,code){Route=route.Substring(0,Math.Min(160,route.Length)),Frame=(long?)payload?["world_frame_id"],Sequence=(long?)payload?["sequence"]};}
            return StrictJSON(responseText);
        }

        private async Task Join()
        {
            runID = p5LobbyRun??DcBootstrap.Argument("-dc-run");
            var token=p5LobbyToken;
            if(p5Lobby==null)
            {
                var password = File.ReadAllText(DcBootstrap.Argument("-dc-password-file")).Trim();
                var login = await Request(publicBase+"/api/login", null, new JObject { ["username"] = DcBootstrap.Argument("-dc-user"), ["password"] = password });
                playerID = ((long)login["data"]["player"]["id"]).ToString(CultureInfo.InvariantCulture);token=(string)login["data"]["token"];
            }
            else playerID=p5LobbyPlayer;
            if(syncFoundation)await ConnectS1Control(token);
            var admission = await Request(publicBase+"/api/v2/runs/" + runID + "/admissions", token, new JObject { ["operation_id"] = Guid.NewGuid().ToString(), ["versions"] = versions, ["config_hash"] = configHash });
            ValidateVersions((JObject)admission["versions"]);
            if ((string)admission["config_hash"] != configHash) throw new InvalidDataException("Admission content mismatch");
            DcLanAddress.ValidateAdmission(publicHost,(string)admission["endpoint"]);
            sourceGeneration = (long)admission["source_generation"]; connectionGeneration = (long)admission["connection_generation"];
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(new JObject { ["ticket"] = admission["ticket"], ["client_nonce"] = Guid.NewGuid().ToString(), ["versions"] = versions, ["config_hash"] = configHash }.ToString(Formatting.None));
            for (var attempt=0;attempt<3;attempt++)
            {
                if (!network.StartClient()) throw new InvalidOperationException("Client transport unavailable");
                network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.manifest.v3"), ManifestMessage);
                network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.p0.state.v3"), StateMessage);
            network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.action.receipt.v3"), ReceiptMessage);
            if(syncFoundation)RegisterS1Client();
            else if(p1)network.CustomMessagingManager.RegisterNamedMessageHandler("dc.p1.snapshot.v3",P1Snapshot);
                var deadline=Time.realtimeSinceStartupAsDouble+5.5;
                while (!loadedConfirmed && network.IsListening && Time.realtimeSinceStartupAsDouble<deadline) await Task.Delay(30,stopping.Token);
                if (loadedConfirmed) return;
                if (network.DisconnectReason.Contains("protocol3 admission rejected")) throw new InvalidDataException("Admission rejected");
                network.Shutdown();
                while (network.ShutdownInProgress) await Task.Delay(30,stopping.Token);
                await Task.Delay(100,stopping.Token);
            }
            throw new InvalidOperationException("Transport handshake retry budget exhausted");
        }

        private async void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = true; response.CreatePlayerObject = false;
            try
            {
                if (request.Payload.Length > 2048 || assignment == null) throw new InvalidDataException("Admission capacity or size");
                var credential = StrictJSON(Encoding.UTF8.GetString(request.Payload));
                RequireFields(credential, "ticket", "client_nonce", "versions", "config_hash"); ValidateVersions((JObject)credential["versions"]);
                if ((string)credential["config_hash"] != configHash) throw new InvalidDataException("Admission hash mismatch");
                credential["instance_id"] = "dc_local_01"; credential["boot_id"] = boot;
                var identity = await Internal("admissions/consume", credential);
                if ((string)identity["run_id"] != (string)assignment["run_id"] || (string)identity["config_hash"] != configHash) throw new InvalidDataException("Run identity mismatch");
                ValidateVersions((JObject)identity["versions"]);
                var old = identities.Where(item => (string)item.Value["player_id"] == (string)identity["player_id"]).Select(item => item.Key).ToArray();
                if (old.Length == 0 && identities.Count >= 4) throw new InvalidDataException("Admission capacity");
                if (old.Any(connection => (long)identities[connection]["connection_generation"] >= (long)identity["connection_generation"])) throw new InvalidDataException("Stale connection generation");
                foreach (var connection in old)
                {
                    identities.Remove(connection); sequences.Remove(connection); inputRates.Remove(connection); actionRates.Remove(connection);s1LoadedConnections.Remove(connection);s2ActionSequences.Remove(connection);
                    p2BaselinePump.Remove(connection);v5BaselineRooms.Remove(connection);v5AckRates.Remove(connection);
                    network.DisconnectClient(connection);
                }
                identities[request.ClientNetworkId] = identity; response.Approved = true;
            }
            catch (Exception error) { RecordProbeNetwork("server.admission",error);response.Approved = false; response.Reason = "protocol"+Protocol+" admission rejected"; }
            response.Pending = false;
        }

        private void Connected(ulong connection)
        {
            if (role == "server")
            {
                if(!identities.TryGetValue(connection,out var identity)){network.DisconnectClient(connection);return;}
                Send("dc.manifest.v3", connection, DcV6Profile.Compact(p3Stage?DcP21Interest.ManifestForPlayer(assignment,(string)identity["player_id"]):assignment),NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void Disconnected(ulong connection)
        {
            RecordProbeNetwork("disconnected:"+connection);
            s1LoadedConnections.Remove(connection);s2ActionSequences.Remove(connection);
            p2BaselinePump.Remove(connection);
            v5BaselineRooms.Remove(connection);
            v5AckRates.Remove(connection);
            if(syncFoundation&&role=="server"&&identities.TryGetValue(connection,out var lost))S1Lost((string)lost["player_id"],(long)lost["connection_generation"]);
            identities.Remove(connection); sequences.Remove(connection); inputRates.Remove(connection); actionRates.Remove(connection);
            if (role == "client") { gameplayMap?.Disable(); menuMap?.Enable(); status = "P0 connection ended"; }
            if(role=="client"&&p1&&!p1ResultConfirmed){p1Controls?.SetEnabled(false);p1View?.Status("战斗连接已断开，请关闭游戏后重新启动");}
            if(role=="client"&&p2Stage&&!p1ResultConfirmed&&!stopping.IsCancellationRequested)_=P2Reconnect();
        }

        private void RejectLegacy(ulong connection, FastBufferReader reader) { network.DisconnectClient(connection); }

        private void ManifestMessage(ulong connection, FastBufferReader reader)
        {
            try
            {
                assignment = Read(reader, p1?60000:16384); ValidateVersions((JObject)assignment["versions"]);
                if ((string)assignment["run_id"] != runID || (string)assignment["config_hash"] != configHash || (long)assignment["source_generation"] != sourceGeneration || (string)assignment["config"]["lifecycle_stage"] != RuntimeStage) throw new InvalidDataException("Frozen manifest mismatch");
                if(p1)ConfigureP1Client();
                if(!p1){menuMap.Disable(); gameplayMap.Enable();}
                Send("dc.loaded.v3", NetworkManager.ServerClientId, new JObject { ["protocol_version"] = Protocol, ["run_id"] = runID, ["config_hash"] = configHash, ["connection_generation"] = connectionGeneration });
                loadedConfirmed = true; status = p1?"P1 protocol3 connected":"P0 protocol3 connected · battle pending P1";
            }
            catch (Exception) { network.Shutdown(); SaveEvidence(false); }
        }

        private void Loaded(ulong connection, FastBufferReader reader)
        {
            try
            {
                var value = Read(reader, 2048); RequireFields(value,"protocol_version","run_id","config_hash","connection_generation");
                if (!identities.TryGetValue(connection, out var identity) || (int)value["protocol_version"] != Protocol || (string)value["run_id"] != (string)assignment["run_id"] || (string)value["config_hash"] != configHash || (long)value["connection_generation"] != (long)identity["connection_generation"]) throw new InvalidDataException();
                if(syncFoundation&&!s1LoadedConnections.Add(connection))return;
                loaded.Add((string)identity["player_id"]);
                if(p2Stage&&running){if(p3Stage)p3Presence.Remove((string)identity["player_id"]);RememberP3Connection((string)identity["player_id"],true);p1World?.Reconnect((string)identity["player_id"]);s1NextState=0;s1EntitySignature=null;}
            }
            catch (Exception error) { RecordProbeNetwork("server.manifest_or_loaded:"+connection,error);network.DisconnectClient(connection); }
        }

        private async Task ServiceLoop()
        {
            var heartbeatAt = 0d;
            var frameAt = 0d;
            while (!stopping.IsCancellationRequested)
            {
                if (assignment == null)
                {
                    var claim = await Internal("instances/dc_local_01/claim", new JObject { ["boot_id"] = boot, ["versions"] = versions });
                    if (claim["assignment"] is JObject manifest) assignment = manifest;
                }
                if (Time.realtimeSinceStartupAsDouble >= heartbeatAt)
                {
                    await Internal("instances/dc_local_01/heartbeat", new JObject { ["boot_id"] = boot, ["versions"] = versions, ["assignment_id"] = assignment?["assignment_id"], ["source_generation"] = assignment?["source_generation"] ?? sourceGeneration, ["instance_status"] = running ? "running" : assignment == null ? "idle" : "loading", ["last_applied_sequence"] = appliedSequence });
                    heartbeatAt = Time.realtimeSinceStartupAsDouble + 2;
                }
                if (assignment != null && !running && loaded.Count == ((JArray)assignment["members"]).Count)
                {
                    var members = new JArray(identities.Values.Select(identity => new JObject { ["player_id"] = identity["player_id"], ["slot"] = identity["slot"], ["connection_generation"] = identity["connection_generation"] }));
                    await Internal("runs/" + assignment["run_id"] + "/start", new JObject { ["versions"] = versions, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["config_hash"] = configHash, ["loaded_members"] = members });
                    runClock.Restart(); running = true;
                    if(p1){PrepareP1World();}
                }
                if (running && Time.realtimeSinceStartupAsDouble >= frameAt)
                {
                    var commands = await Internal("runs/" + assignment["run_id"] + "/commands?after_control_version=" + controlVersion);
                    controlVersion = (long)commands["control_version"];
                    var elapsedMS = runClock.ElapsedMilliseconds;
                    var frame = new JObject { ["schema_version"] = 3, ["source"] = "unity", ["run_id"] = assignment["run_id"], ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["sequence"] = appliedSequence + 1, ["frame_id"] = Guid.NewGuid().ToString(), ["world_frame_id"] = ++worldFrame, ["elapsed_ms"] = elapsedMS, ["wall_elapsed_ms"] = elapsedMS, ["manual_pause_ms"] = 0, ["control_version"] = controlVersion, ["facts"] = new JArray(new JObject { ["event_id"] = Guid.NewGuid().ToString(), ["kind"] = "phase", ["actor_player_id"] = null, ["target_id"] = "p0_active", ["action_id"] = null, ["effective_amount"] = 0, ["life_generation"] = 0, ["downed_generation"] = 0, ["event_game_ms"] = elapsedMS, ["contributors"] = new JArray(), ["proof"] = null }), ["terminal"] = null, ["summary"] = null };
                    JObject receipt=null;
                    for(var attempt=0;attempt<8;attempt++)
                    {
                        frame.Remove("fingerprint");frame["fingerprint"] = Hash(Encoding.UTF8.GetBytes(Canonical(frame)));
                        try {receipt=await Internal("runs/"+assignment["run_id"]+"/frames",frame);break;}
                        catch(RequestFailure failure) when(failure.Code==40982)
                        {
                            var updated=await Internal("runs/"+assignment["run_id"]+"/commands?after_control_version="+controlVersion);controlVersion=(long)updated["control_version"];frame["control_version"]=controlVersion;
                        }
                    }
                    if(receipt==null)throw new InvalidOperationException("P0 control fence retry budget exhausted");
                    appliedSequence=(long)receipt["applied_sequence"];
                    foreach (var identity in identities.ToArray()) Send("dc.p0.state.v3", identity.Key, new JObject { ["protocol_version"] = Protocol, ["message"] = "dc.p0.state.v3", ["run_id"] = assignment["run_id"], ["source_generation"] = assignment["source_generation"], ["connection_generation"] = identity.Value["connection_generation"], ["game_elapsed_ms"] = elapsedMS, ["wall_elapsed_ms"] = elapsedMS, ["manual_pause_ms"] = 0, ["applied_sequence"] = appliedSequence, ["input_ack"] = sequences.TryGetValue(identity.Key, out var input) ? input : 0, ["run_status"] = receipt["run_status"], ["runtime_stage"] = "p0_control_only" });
                    if ((string)receipt["run_status"] != "running") { running = false; return; }
                    if(p1&&p1World.Terminal){await ConfirmP1Result(elapsedMS);running=false;return;}
                    frameAt = Time.realtimeSinceStartupAsDouble + 1;
                }
                await Task.Delay(30, stopping.Token);
            }
        }

        private void Update()
        {
            if(syncFoundation){UpdateS1();return;}
            if(p1){UpdateP1();return;}
            if (statusLabel != null) statusLabel.text = status;
            if (role != "client" || !loadedConfirmed || !network.IsConnectedClient || Time.unscaledTime < nextInput) return;
            nextInput = Time.unscaledTime + 1f / 30f;
            var movement = moveAction.ReadValue<Vector2>(); movement = Vector2.ClampMagnitude(movement, 1);
            var intent = new JObject { ["protocol_version"] = Protocol, ["message"] = "dc.input.v3", ["run_id"] = runID, ["source_generation"] = sourceGeneration, ["connection_generation"] = connectionGeneration, ["input_sequence"] = ++sentSequence, ["move_x"] = Mathf.RoundToInt(movement.x * 1000), ["move_y"] = Mathf.RoundToInt(movement.y * 1000), ["aim_u16"] = 0, ["ranged_held"] = rangedAction.IsPressed(), ["melee_held"] = meleeAction.IsPressed(), ["interact_held"] = false, ["rescue_held"] = false };
            var pointer = aimAction.ReadValue<Vector2>();
            intent["aim_u16"] = Mathf.RoundToInt((Mathf.Atan2(pointer.y - Screen.height / 2f, pointer.x - Screen.width / 2f) + Mathf.PI) / (Mathf.PI * 2f) * 65535);
            Send("dc.input.v3", NetworkManager.ServerClientId, intent, NetworkDelivery.UnreliableSequenced);
            if (!actionSent)
            {
                actionSent = true;
                Send("dc.action.v3", NetworkManager.ServerClientId, new JObject { ["protocol_version"] = Protocol, ["message"] = "dc.action.v3", ["run_id"] = runID, ["source_generation"] = sourceGeneration, ["connection_generation"] = connectionGeneration, ["input_sequence"] = sentSequence, ["operation_id"] = "action_p0", ["action_id"] = "action_p0", ["action"] = "reload", ["aim_u16"] = 0, ["target_id"] = null, ["target_life_generation"] = null, ["target_downed_generation"] = null, ["placement_x_milli"] = null, ["placement_y_milli"] = null, ["choice_id"] = null, ["choice_revision"] = null, ["option_id"] = null });
            }
        }

        private void InputMessage(ulong connection, FastBufferReader reader)
        {
            try
            {
                if (!Budget(inputRates, connection, 60)) throw new InvalidDataException("Input budget");
                if(syncFoundation&&s1Paused)return;
                if(weaponStage){S2InputMessage(connection,Read(reader,900));return;}
                var value = Read(reader, 900); RequireFields(value,"protocol_version","message","run_id","source_generation","connection_generation","input_sequence","move_x","move_y","aim_u16","ranged_held","melee_held","interact_held","rescue_held"); ValidateMessage(connection, value, WireName("dc.input.v3"));
                var sequence = Integer(value,"input_sequence",0,uint.MaxValue);
                if (sequences.TryGetValue(connection,out var previous) && sequence <= previous) return;
                var horizontal = Integer(value,"move_x",-1000,1000); var vertical = Integer(value,"move_y",-1000,1000); Integer(value,"aim_u16",0,65535);
                if (horizontal * horizontal + vertical * vertical > 1002001) throw new InvalidDataException("Unnormalized movement");
                foreach (var name in new[] {"ranged_held","melee_held","interact_held","rescue_held"}) if (value[name].Type != JTokenType.Boolean) throw new InvalidDataException("Invalid held state");
                sequences[connection] = (uint)sequence;
                if(p1){var intent=new DcP1Input {Sequence=(uint)sequence,Move=new Vector2(horizontal/1000f,vertical/1000f),Aim=(ushort)Integer(value,"aim_u16",0,65535),Attack=(bool)value["ranged_held"],Melee=(bool)value["melee_held"],Interact=(bool)value["interact_held"]};if(syncFoundation)p1World?.QueueFoundationInput((string)identities[connection]["player_id"],intent);else p1World?.Input((string)identities[connection]["player_id"],intent);}
            }
            catch (Exception error) { RecordProbeNetwork("server.input:"+connection,error);network.DisconnectClient(connection); }
        }

        private void ActionMessage(ulong connection, FastBufferReader reader)
        {
            try
            {
                if (!Budget(actionRates,connection,20)) throw new InvalidDataException("Action budget");
                if(weaponStage){S2ActionMessage(connection,Read(reader,2048));return;}
                if(syncFoundation&&s1Paused)return;
                if(p1){P1Action(connection,Read(reader,2048));return;}
                var value = Read(reader,2048); RequireFields(value,"protocol_version","message","run_id","source_generation","connection_generation","input_sequence","operation_id","action_id","action","aim_u16","target_id","target_life_generation","target_downed_generation","placement_x_milli","placement_y_milli","choice_id","choice_revision","option_id"); ValidateMessage(connection,value,WireName("dc.action.v3"));
                Integer(value,"input_sequence",0,uint.MaxValue); Integer(value,"aim_u16",0,65535);
                var actionID=(string)value["action_id"]; if (string.IsNullOrEmpty(actionID) || actionID.Length>64) throw new InvalidDataException("Action identity");
                Send("dc.action.receipt.v3",connection,new JObject { ["protocol_version"]=Protocol,["message"]="dc.action.receipt.v3",["run_id"]=assignment["run_id"],["connection_generation"]=identities[connection]["connection_generation"],["action_id"]=actionID,["operation_id"]=value["operation_id"],["status"]="rejected",["reason"]="invalid_phase",["effective_frame"]=worldFrame,["state_revision"]=appliedSequence });
            }
            catch (Exception error) { RecordProbeNetwork("server.action:"+connection,error);network.DisconnectClient(connection); }
        }

        private void StateMessage(ulong connection, FastBufferReader reader)
        {
            try
            {
                var value=Read(reader,2048);
                if ((int)value["protocol_version"]!=Protocol || (string)value["run_id"]!=runID || (long)value["connection_generation"]!=connectionGeneration || (long)value["source_generation"]!=sourceGeneration || (long)value["game_elapsed_ms"]+(long)value["manual_pause_ms"]!=(long)value["wall_elapsed_ms"]) throw new InvalidDataException("State binding mismatch");
                inputAck=(long)value["input_ack"]; status="P0 protocol3 · input ACK "+inputAck+" · gameplay pending";
                SaveEvidence(true);
            }
            catch(Exception) { network.Shutdown(); SaveEvidence(false); }
        }

        private void ReceiptMessage(ulong connection,FastBufferReader reader)
        {
            if(weaponStage){S2Receipt(Read(reader,2048));return;}
            if(p1){var value=Read(reader,2048);if((long)value["connection_generation"]!=connectionGeneration)return;p1View?.Feedback((string)value["reason"]);return;}
            try { var value=Read(reader,2048); if((int)value["protocol_version"]!=Protocol || (string)value["run_id"]!=runID || (long)value["connection_generation"]!=connectionGeneration) throw new InvalidDataException(); actionRejected=(string)value["status"]=="rejected" && (string)value["reason"]=="invalid_phase"; SaveEvidence(true); }
            catch(Exception) { network.Shutdown(); }
        }

        private void ValidateMessage(ulong connection,JObject value,string message)
        {
            if (!identities.TryGetValue(connection,out var identity) || !S1ConnectionReady(connection) || !loaded.Contains((string)identity["player_id"]) || (int)value["protocol_version"]!=Protocol || (string)value["message"]!=message || (string)value["run_id"]!=(string)assignment["run_id"] || (long)value["source_generation"]!=(long)assignment["source_generation"] || (long)value["connection_generation"]!=(long)identity["connection_generation"]) throw new InvalidDataException("Input binding mismatch");
        }

        private bool Budget(Dictionary<ulong,Queue<double>> rates,ulong connection,int maximum)
        {
            if(!rates.TryGetValue(connection,out var queue)) {queue=new Queue<double>();rates[connection]=queue;}
            var now=Time.realtimeSinceStartupAsDouble; while(queue.Count>0 && now-queue.Peek()>=1)queue.Dequeue(); if(queue.Count>=maximum)return false;queue.Enqueue(now);return true;
        }

        private void ConfigureInput()
        {
            if(p1){ConfigureP1Input();return;}
            inputAsset=ScriptableObject.CreateInstance<InputActionAsset>();
            foreach(var name in new[]{"Gameplay","ChoiceOverlay","Menu","Rebind","Spectator","Practice"})inputAsset.AddActionMap(new InputActionMap(name));
            gameplayMap=inputAsset.FindActionMap("Gameplay");menuMap=inputAsset.FindActionMap("Menu");
            moveAction=gameplayMap.AddAction("Move",InputActionType.Value);moveAction.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
            aimAction=gameplayMap.AddAction("Aim",InputActionType.Value,"<Mouse>/position");rangedAction=gameplayMap.AddAction("Ranged",InputActionType.Button,"<Mouse>/leftButton");meleeAction=gameplayMap.AddAction("Melee",InputActionType.Button,"<Mouse>/rightButton");
            menuMap.AddAction("Cancel",InputActionType.Button,"<Keyboard>/escape");menuMap.Enable();
        }

        private static void CheckClientSecrets()
        {
            foreach(var name in new[]{"DB_PASSWORD","REDIS_PASSWORD","JWT_SECRET","DC_SERVICE_TOKEN","DC_ADMISSION_KEY","PVE_METRICS_TOKEN","OPENAI_API_KEY","OPENAI_IMAGE_API_KEY"})if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))throw new InvalidDataException("Client inherited a service secret");
        }

        private void CreateStatusUI()
        {
            var canvasObject=new GameObject("P0 client status",typeof(Canvas),typeof(CanvasScaler));canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var label=new GameObject("Status",typeof(RectTransform),typeof(Text));label.transform.SetParent(canvasObject.transform,false);statusLabel=label.GetComponent<Text>();statusLabel.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");statusLabel.fontSize=20;statusLabel.color=Color.white;var rect=label.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(24,24);rect.offsetMax=new Vector2(-24,-24);
        }

        private void SaveEvidence(bool valid)
        {
            var output=DcBootstrap.Argument("-dc-p0-evidence");if(string.IsNullOrEmpty(output))return;
            var full=Path.GetFullPath(output);if(!full.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
            var report=new JObject { ["role"]=role,["protocol_version"]=Protocol,["run_id"]=runID,["valid_binding"]=valid,["loaded"]=loadedConfirmed,["input_ack"]=inputAck,["input_system"]="1.19.0",["contexts"]=inputAsset==null?0:inputAsset.actionMaps.Count,["unimplemented_action_rejected"]=actionRejected,["no_service_secrets"]=role=="client",["no_ui"]=FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length==0,["combat_implemented"]=false };if(DcV8Profile.Active){report["failure_reason"]=qaFailureReason;report["binding_versions"]=versions?.DeepClone();report["definition_id"]=configuration?["definition_id"];report["configuration_hash"]=configHash;}File.WriteAllText(full,report.ToString());
        }

        private void Send(string name,ulong connection,JObject value,NetworkDelivery delivery=NetworkDelivery.ReliableSequenced)
        {
            name=WireName(name);if(syncFoundation){if(value["protocol_version"]!=null)value["protocol_version"]=Protocol;if(value["message"]!=null)value["message"]=WireName((string)value["message"]);}
            var data=value.ToString(Formatting.None);var bytes=Encoding.UTF8.GetBytes(data);if(bytes.Length>60000)throw new InvalidDataException("P0 message size limit");using var writer=new FastBufferWriter(v5Runtime?bytes.Length+4:65536,Allocator.Temp);if(v5Runtime){writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);}else writer.WriteValueSafe(data);network.CustomMessagingManager.SendNamedMessage(name,connection,writer,delivery);if(v5Runtime&&role=="server"&&delivery!=NetworkDelivery.Unreliable&&delivery!=NetworkDelivery.UnreliableSequenced)v5ReliableJsonSentBytes+=bytes.Length+4;
        }

        private JObject Read(FastBufferReader reader,int maximum) {if(reader.Length>(v5Runtime?maximum+4:maximum))throw new InvalidDataException("Message size limit");if(v5Runtime){reader.ReadValueSafe(out int length);if(length<2||length>maximum||length!=reader.Length-reader.Position)throw new InvalidDataException("V5 JSON length");var bytes=new byte[length];reader.ReadBytesSafe(ref bytes,length);return StrictJSON(new UTF8Encoding(false,true).GetString(bytes));}reader.ReadValueSafe(out string data);return StrictJSON(data);}
        private static JObject StrictJSON(string data)=>JObject.Parse(data,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
        private static void RequireFields(JObject value,params string[] names) {if(value.Properties().Count()!=names.Length || names.Any(name=>value[name]==null))throw new InvalidDataException("Unexpected message fields");}
        private static long Integer(JObject value,string name,long minimum,long maximum) {if(value[name].Type!=JTokenType.Integer)throw new InvalidDataException("Integer field required");var number=(long)value[name];if(number<minimum || number>maximum)throw new InvalidDataException("Integer bounds");return number;}
        private static JObject BuildVersions(JObject value)=>new JObject { ["protocol_version"]=value["protocol_version"],["operation_version"]=value["versions"]["operation"],["content_version"]=value["versions"]["content"],["world_version"]=value["versions"]["world"],["parameters_hash"]=value["parameters_sha256"] };
        private void ValidateVersions(JObject binding) {if(binding==null || !JToken.DeepEquals(binding,versions) || (int)binding["protocol_version"]!=Protocol || (string)binding["operation_version"]!=(DcV8Profile.Active?"dc_ruins.v8":DcV7Profile.Active?"dc_ruins.v7":DcV6Profile.Active?"dc_ruins.v6":v5Runtime?"dc_ruins.v5":syncFoundation?"dc_ruins.v4":"dc_ruins.v3"))throw new InvalidDataException("Protocol version mismatch");}
        private static string FileHash(string path)=>Hash(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n","\n")));
        private static string Hash(byte[] data) {using var algorithm=SHA256.Create();return string.Concat(algorithm.ComputeHash(data).Select(value=>value.ToString("x2")));}
        private static string Canonical(JToken value) {if(value is JObject objectValue)return "{"+string.Join(",",objectValue.Properties().OrderBy(item=>item.Name,StringComparer.Ordinal).Select(item=>JsonConvert.SerializeObject(item.Name)+":"+Canonical(item.Value)))+"}";if(value is JArray array)return "["+string.Join(",",array.Select(Canonical))+"]";return value.ToString(Formatting.None);}

        private void OnApplicationFocus(bool focused) {if(syncFoundation){S1Focus(focused);return;}if(p1){P1Focus(focused);return;}if(role!="client" || gameplayMap==null)return;if(!focused)gameplayMap.Disable();else if(loadedConfirmed)gameplayMap.Enable();}
        private void OnDestroy() {stopping?.Cancel();network?.Shutdown();p1Controls?.Dispose();if(p1Controls==null){inputAsset?.Disable();if(inputAsset!=null)Destroy(inputAsset);}http?.Dispose();stopping?.Dispose();}
    }
}
