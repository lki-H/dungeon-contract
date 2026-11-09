using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private bool syncFoundation;
        private bool weaponStage;
        private bool p2Stage;
        private bool multiplayerStage;
        private bool s1ClientBaselineReady;
        private readonly HashSet<ulong> s1LoadedConnections=new HashSet<ulong>();
        private bool S1ConnectionReady(ulong connection)=>!syncFoundation||s1LoadedConnections.Contains(connection);
        private readonly DcP2Baseline p2Baseline=new DcP2Baseline();
        private readonly DcP2BaselinePump p2BaselinePump=new DcP2BaselinePump();
        private uint p2Transfer;
        private bool p2Reconnecting;
        private bool p2ReconnectNotice;
        private double p2PilotSkill;
        private double p2PilotDodge;
        private JObject p2PilotGeometry;
        private bool p2ProbeInput;
        private Vector2 p2ProbeMovement;
        private readonly System.Collections.Generic.Queue<JObject> p2NetworkEvents=new System.Collections.Generic.Queue<JObject>();
        private void RecordProbeNetwork(string node,Exception error=null)
        {
            if(role!="server"&&DcBootstrap.Argument("-dc-p2-network-probe","false")!="true")return;
            var target=DcBootstrap.Argument(role=="server"?"-dc-p0-evidence":"-dc-p2-probe-output");if(string.IsNullOrEmpty(target))return;
            if(p2NetworkEvents.Count>=64)p2NetworkEvents.Dequeue();
            var detail=error is InvalidDataException||error is InvalidOperationException&&node.StartsWith("server.action:",StringComparison.Ordinal)?error.Message:null;if(detail!=null&&detail.Length>160)detail=detail.Substring(0,160);
            var reason=network?.DisconnectReason;if(reason!=null&&reason.Length>160)reason=reason.Substring(0,160);
            p2NetworkEvents.Enqueue(new JObject{["node"]=node,["role"]=role,["error_type"]=error?.GetType().Name,["detail"]=detail,["connection_generation"]=connectionGeneration,["world_frame_id"]=role=="server"?new JValue(p1World?.WorldTick??0):p1Snapshot?["world_frame_id"],["disconnect_reason"]=reason});
            try{var output=Path.GetFullPath(target);if(output.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){var label="server";if(role!="server"){if(!int.TryParse(DcBootstrap.Argument("-dc-p2-probe-slot","0"),out var slot)||slot<0||slot>=4)return;label=slot.ToString();}File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"probe-network-events-"+label+".json"),ProbeNetworkEvents.ToString());}}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        }
        private bool p2ProbeAttack;
        private bool p2ProbeCombat;
        private string RuntimeStage=>p4Stage?"personal_growth_content_stage":p3Stage?"cooperation_business_stage":multiplayerStage?"multiplayer_sync_stage":p2Stage?"room_camera_projectile_stage":weaponStage?"weapon_stage":syncFoundation?"sync_foundation":p1?"p1_single_player":"p0_control_only";
        private bool v5Runtime;
        private string v5ChargePress;
        private bool v5AutoHeld;
        private int Protocol=>DcV8Profile.Active?8:DcV7Profile.Active?7:DcV6Profile.Active?6:v5Runtime?5:syncFoundation?4:3;
        private string WireName(string name)=>DcV8Profile.Active?System.Text.RegularExpressions.Regex.Replace(name,@"\.v[34567]", ".v8"):DcV7Profile.Active?name.Replace(".v3",".v7").Replace(".v4",".v7").Replace(".v5",".v7").Replace(".v6",".v7"):DcV6Profile.Active?name.Replace(".v3",".v6").Replace(".v4",".v6").Replace(".v5",".v6"):v5Runtime?name.Replace(".v3",".v5").Replace(".v4",".v5"):!syncFoundation?name:name.StartsWith("dc.input")||name.StartsWith("dc.action")?name.Replace("dc.",weaponStage?"dc.s2.":"dc.s1.").Replace(".v3",".v4"):name.Replace(".v3",".v4");
        private ClientWebSocket s1Control;
        private readonly SemaphoreSlim s1ControlSend=new SemaphoreSlim(1,1);
        private readonly DcS1Replica s1Replica=new DcS1Replica();
        private DcS1Prediction s1Prediction;
        private bool s1Paused;
        private bool s1PausePending;
        private bool s1MenuCancelPending;
        private long s1MenuCancelSource,s1MenuCancelConnection;
        private long s1MenuPauseClockRevision;
        private long s1Revision=1;
        private string s1PauseID;
        private long s1PauseAt;
        private long s1PauseTotal;
        private long s1FreezeFrame;
        private long p3CommandCursor;
        private int s1StateRevision;
        private int s1MaximumPacket;
        private int s1Packets;
        private long s1ServiceTicks;
        private double s1TickMilliseconds;
        private double s1TickMaxMilliseconds;
        private long s1DynamicSentBytes;
        private long s1BaselineSentBytes;
        private double s1EvidenceAt;
        private double s1NextState;
        private double s1NextDynamic;
        private string s1EntitySignature;
        private long s1ReplicaFrame=-1;
        private JObject s1LastSnapshot;
        private double s1HealthyAt;
        private double s1RequestAt;
        private double s1PauseRequestedAt;
        private bool s1Waiting;
        private long S1Wall=>runClock.ElapsedMilliseconds;
        private long S1Game=>S1Wall-s1PauseTotal-(s1Paused?S1Wall-s1PauseAt:0);

        private void RegisterS1Client()
        {
            if(v5Runtime)network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.actor-state.v5"),V5ActorState);
            if(DcV6Profile.Active)network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.lifecycle.v6"),V6LifecycleMessage);
            network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.s1.baseline.v4"),S1Baseline);
            if(multiplayerStage)network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.p2.baseline.chunk.v4"),P2BaselineChunk);
            network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.s1.dynamic.v4"),S1Dynamic);
            network.CustomMessagingManager.RegisterNamedMessageHandler(WireName("dc.s1.clock.v4"),S1ClockMessage);
        }
        private void ConfigureS1Client()
        {
            s1Replica.ProtocolVersion=Protocol;p2Baseline.ProtocolVersion=Protocol;
            s1Replica.P5Presentation=DcP5Settings.Active;
            s1ClientBaselineReady=false;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            s1Prediction=new DcS1Prediction((JObject)assignment["p1_world"]);p1Controls.FreezeMenu=true;
            if(p2Stage)p2PilotGeometry=DcP1Definition.NormalizeGeometry((JObject)assignment["p1_world"]);
            p1Controls.MenuChanged=menu=>{_ = S1PauseRequest(menu,menu?"menu":"explicit_resume");};
            if(GetComponent<DcS1Metrics>()==null)gameObject.AddComponent<DcS1Metrics>();
            if(v5Runtime&&DcBootstrap.Argument("-dc-s2-probe","false")=="true"&&GetComponent<DcV5Probe>()==null)gameObject.AddComponent<DcV5Probe>().Runtime=this;
            else if(weaponStage&&DcBootstrap.Argument("-dc-s2-probe","false")=="true"&&GetComponent<DcS2Probe>()==null)gameObject.AddComponent<DcS2Probe>().Runtime=this;
            if(DcBootstrap.Argument("-dc-s1-probe","false")=="true"&&GetComponent<DcS1Probe>()==null)gameObject.AddComponent<DcS1Probe>().Runtime=this;
            if(multiplayerStage&&DcBootstrap.Argument("-dc-p2-network-probe","false")=="true"&&GetComponent<DcP2NetworkProbe>()==null)gameObject.AddComponent<DcP2NetworkProbe>().Runtime=this;
        }
        private async Task ConnectS1Control(string token)
        {
            if(p3Stage)p3Token=token;
            s1Control=new ClientWebSocket();s1Control.Options.Proxy=null;s1Control.Options.SetRequestHeader("Authorization","Bearer "+token);s1Control.Options.KeepAliveInterval=TimeSpan.FromSeconds(2);
            await s1Control.ConnectAsync(new Uri(DcLanAddress.WebSocketURL(publicHost)),stopping.Token);
            _=ReadS1Control();
        }
        private async Task ReadS1Control()
        {
            var socket=s1Control;
            var buffer=new byte[32768];
            try
            {
                while(socket.State==WebSocketState.Open&&!stopping.IsCancellationRequested)
                {
                    using var stream=new MemoryStream();WebSocketReceiveResult received;
                    do{received=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stopping.Token);if(received.MessageType==WebSocketMessageType.Close){S1ControlUnavailable(socket);return;}stream.Write(buffer,0,received.Count);if(stream.Length>65536)throw new InvalidDataException("Control receive budget");}while(!received.EndOfMessage);
                    if(stopping.IsCancellationRequested||!object.ReferenceEquals(socket,s1Control))return;
                    var value=StrictJSON(Encoding.UTF8.GetString(stream.ToArray()));
                    if(p3Stage&&((string)value["type"]=="v2.run.result"||(string)value["type"]=="v2.participant.result")&&(string)value["data"]?["run_id"]==runID){p1ResultConfirmed=true;loadedConfirmed=false;s1Prediction?.Clear();p1Controls?.Suspend(false);_=LoadP3Result();}
                    if((string)value["type"]=="server.error")S1PauseRejected(socket);
                }
            }catch(Exception){S1ControlUnavailable(socket);}
        }
        private void S1PauseRejected(ClientWebSocket socket){
            if(stopping.IsCancellationRequested||!object.ReferenceEquals(socket,s1Control))return;
            s1PausePending=false;QueueS1MenuCancel(p1Controls?.Menu==true);p1View?.Status("暂停请求被拒绝，请返回游戏");
        }
        private void S1ControlUnavailable(ClientWebSocket socket){
            if(stopping.IsCancellationRequested||!object.ReferenceEquals(socket,s1Control))return;
            s1PausePending=false;QueueS1MenuCancel(p1Controls?.Menu==true);p1View?.Status("控制连接中断，暂停不可用；战斗状态仍由服务裁决");
        }
        private async Task S1PauseRequest(bool pause,string reason)
        {
            if(assignment==null||p1Controls==null||p1View==null)return;
            if(((JArray)assignment["members"]).Count!=1||(string)assignment["mode"]!="normal"){QueueS1MenuCancel(pause);p1View.Status("多人或试炼菜单不暂停");return;}
            if(s1PausePending)return;
            if(s1Control==null||s1Control.State!=WebSocketState.Open){QueueS1MenuCancel(pause);return;}
            if(pause==s1Paused)return;
            BeginS1PauseRequest(pause);
            var socket=s1Control;var requestSource=sourceGeneration;var requestConnection=connectionGeneration;
            var operation=Guid.NewGuid().ToString();
            var payload=new JObject {["schema_version"]=2,["type"]=pause?"v2.run.pause":"v2.run.resume",["request_id"]=operation,["operation_id"]=operation,["data"]=new JObject{["run_id"]=runID,["reason"]=reason,["expected_clock_revision"]=s1Revision,["pause_id"]=pause?null:s1PauseID}};
            await s1ControlSend.WaitAsync(stopping.Token);
            try{var bytes=Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,stopping.Token);}
            catch(Exception){if(!stopping.IsCancellationRequested&&object.ReferenceEquals(socket,s1Control)&&requestSource==sourceGeneration&&requestConnection==connectionGeneration){s1PausePending=false;QueueS1MenuCancel(pause);p1View.Status("暂停控制发送失败，世界不会假暂停");}}
            finally{s1ControlSend.Release();}
        }
        private void BeginS1PauseRequest(bool pause){
            s1PausePending=true;s1PauseRequestedAt=Time.realtimeSinceStartupAsDouble;
            if(pause){QueueS1MenuCancel(true);s1MenuPauseClockRevision=s1Revision+1;}
            p1Controls.Suspend(false);p1View.Status(pause?"正在确认暂停……":"正在确认恢复……");
        }
        private void ConfirmS1Pause(bool paused,long revision,string pauseID){
            s1Paused=paused;
            if(paused&&s1MenuPauseClockRevision>0&&revision==s1MenuPauseClockRevision&&!string.IsNullOrEmpty(pauseID)&&s1MenuCancelSource==sourceGeneration&&s1MenuCancelConnection==connectionGeneration){s1MenuCancelPending=false;s1MenuPauseClockRevision=0;}
        }
        private void QueueS1MenuCancel(bool pause){
            if(!pause||!weaponStage||!ProbeReady||p1ResultConfirmed||s1Paused)return;
            s1MenuCancelPending=true;s1MenuCancelSource=sourceGeneration;s1MenuCancelConnection=connectionGeneration;
            if(DcV7Profile.Active)p1View.V7ClearPreviews();
        }
        private string[] S1MenuActions(string[] actions){
            if(!s1MenuCancelPending)return actions;s1MenuCancelPending=false;s1MenuPauseClockRevision=0;
            if(s1MenuCancelSource!=sourceGeneration||s1MenuCancelConnection!=connectionGeneration)return actions;
            // This intent survives a quick menu close and precedes new user
            // actions on the existing reliable battle connection.
            return new[]{"cancel_input"}.Concat(actions.Where(action=>action!="cancel_input")).ToArray();
        }
        private void S1Focus(bool focused)
        {
            if(role!="client"||p1Controls==null)return;
            if(DcBootstrap.Argument("-dc-p2-network-probe","false")=="true"||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true")return;
            if(!focused){p1Controls.SetMenu(true);_=S1PauseRequest(true,"focus_loss");}
        }
        private void S1Lost(string player,long connection)
        {
            RememberP3Connection(player,false);
            p1World?.Presence(player,false);
            if(p3Stage){p3Presence[player]=new JObject{["assignment_id"]=assignment["assignment_id"],["boot_id"]=boot,["source_generation"]=sourceGeneration,["player_id"]=player,["connection_generation"]=connection,["connected"]=false};return;}
            _=Internal("runs/"+assignment["run_id"]+"/presence",new JObject {["assignment_id"]=assignment["assignment_id"],["boot_id"]=boot,["source_generation"]=sourceGeneration,["player_id"]=player,["connection_generation"]=connection,["connected"]=false});
        }
        private async Task P2Reconnect()
        {
            if(p2Reconnecting||stopping.IsCancellationRequested||p1ResultConfirmed)return;p2Reconnecting=true;p2ReconnectNotice=true;loadedConfirmed=false;s1Prediction?.Clear();p1Controls?.Suspend(false);p1View?.PauseVisuals(false);
            try
            {
                while(network.ShutdownInProgress)await Task.Delay(50,stopping.Token);
                if(DcV7Profile.Active)p1View?.V7ClearPreviews();s1Control?.Dispose();s1Control=null;p1Snapshot=null;p2Baseline.Clear();if(DcV6Profile.Active)v6PreviousConnectionGaps+=s1Replica.V6Gaps;s1Replica.Clear();ClearV8ActorStates();s1MenuCancelPending=false;s1MenuPauseClockRevision=0;s1ReplicaFrame=-1;sentSequence=0;s2LastAction=0;s2SwitchPending=false;s2SwitchActionID=null;s2Press=null;v5ChargePress=null;v5AutoHeld=false;v5ActorFrame=-1;
                for(var attempt=0;attempt<3&&!stopping.IsCancellationRequested;attempt++)
                {
                    p1View?.Status("连接中断，正在恢复原关卡……");await Task.Delay(1000,stopping.Token);
                    try{await Join();p1Controls.SetEnabled(s1ClientBaselineReady&&!s1Paused&&!p1Controls.Menu);return;}catch(Exception){network.Shutdown();while(network.ShutdownInProgress)await Task.Delay(50,stopping.Token);}
                }
                p1View?.Status("重连未完成，请退出后重试；服务世界继续运行");
            }
            catch(OperationCanceledException){}finally{p2Reconnecting=false;}
        }
        private void SendS1Baseline(ulong client,JObject snapshot,JObject identity)
        {
            if(!S1ConnectionReady(client))return;
            if(DcV6Profile.Active&&v6Streams.TryGetValue(client,out var active)&&active.AwaitingBaseline)return;
            var complete=!v5Runtime||CompleteV5NetworkBaseline(client,snapshot,(string)identity["player_id"]);
            var visible=p2Stage?DcP21Interest.ForPlayer(snapshot,(string)identity["player_id"],!complete):snapshot;visible["connection_generation"]=identity["connection_generation"];
            if(DcV6Profile.Active){var stream=v6Streams[client];stream.Baseline(visible);DcS1Replica.PackV6Baseline(visible,complete,h=>stream.Births.TryGetValue(h,out var birth)?birth:0);if(complete)stream.BeginBaseline((int)visible["state_revision"]);}
            else if(v5Runtime)DcS1Replica.PackV5Baseline(visible,complete);
            if(multiplayerStage)p2BaselinePump.Enqueue(client,DcP2Baseline.Encode(visible,++p2Transfer,sourceGeneration,(long)identity["connection_generation"],Protocol));
            else Send("dc.s1.baseline.v4",client,visible,NetworkDelivery.ReliableFragmentedSequenced);
        }
        private void DrainP2Baselines()
        {
            foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)))p2BaselinePump.Drain(identity.Key,8,packet=>{s1BaselineSentBytes+=packet.Length;using var writer=new FastBufferWriter(packet.Length,Allocator.Temp);writer.WriteBytesSafe(packet);network.CustomMessagingManager.SendNamedMessage(WireName("dc.p2.baseline.chunk.v4"),identity.Key,writer,NetworkDelivery.ReliableSequenced);});
        }
        private async Task S1ServiceLoop()
        {
            var heartbeatAt=0d;var frameAt=0d;s1HealthyAt=Time.realtimeSinceStartupAsDouble;
            while(!stopping.IsCancellationRequested)
            {
                try {
                if(assignment==null){var claim=await Internal("instances/dc_local_01/claim",new JObject {["boot_id"]=boot,["versions"]=versions});if(claim["assignment"] is JObject manifest)assignment=manifest;}
                if(Time.realtimeSinceStartupAsDouble>=heartbeatAt){await Internal("instances/dc_local_01/heartbeat",new JObject {["boot_id"]=boot,["versions"]=versions,["assignment_id"]=assignment?["assignment_id"],["source_generation"]=assignment?["source_generation"]??sourceGeneration,["instance_status"]=running?"running":assignment==null?"idle":"loading",["last_applied_sequence"]=appliedSequence});heartbeatAt=Time.realtimeSinceStartupAsDouble+2;}
                if(assignment!=null&&!running&&loaded.Count==((JArray)assignment["members"]).Count&&identities.Count(item=>S1ConnectionReady(item.Key))==loaded.Count)
                {
                    var members=new JArray(identities.Values.Select(identity=>new JObject {["player_id"]=identity["player_id"],["slot"]=identity["slot"],["connection_generation"]=identity["connection_generation"]}));
                    await Internal("runs/"+assignment["run_id"]+"/start",new JObject {["versions"]=versions,["assignment_id"]=assignment["assignment_id"],["source_generation"]=assignment["source_generation"],["config_hash"]=configHash,["loaded_members"]=members});
                    runClock.Restart();s1HealthyAt=Time.realtimeSinceStartupAsDouble;running=true;PrepareP1World();
                }
                if(running)
                {
                    if(p3Stage)await SubmitP3Presence();
                    s1RequestAt=Time.realtimeSinceStartupAsDouble;
                    var commands=await Internal("runs/"+assignment["run_id"]+"/commands?after_control_version="+(p3Stage?p3CommandCursor:controlVersion));
                    foreach(var item in (JArray)commands["commands"])
                    {
                        var kind=(string)item["type"];var payload=item["payload"];
                        if(kind=="pause"||kind=="resume")
                        {
                            var operation=(string)item["operation_id"];
                            if(p3Stage&&p3ClockAcks.TryGetValue(operation,out var priorAck)){var priorReceipt=await Internal("runs/"+assignment["run_id"]+"/control-acks",priorAck);s1Revision=(long)priorReceipt["clock_revision"];controlVersion=(long)item["control_version"];p3CommandCursor=controlVersion;p3ControlDraining=false;BroadcastS1Clock();continue;}
                            if(p3Stage&&p3Pending.Count>0){p3ControlDraining=true;controlVersion=(long)item["control_version"];p3Sending=null;for(var drain=0;drain<8&&p3Pending.Count>0;drain++){var before=appliedSequence;await SubmitP3();if(!running)return;if(before==appliedSequence)throw new System.Net.Http.HttpRequestException("P3 clock fence still draining");}if(p3Pending.Count>0)throw new System.Net.Http.HttpRequestException("P3 clock drain budget");}
                            var wall=S1Wall;var game=wall-s1PauseTotal-(s1Paused?wall-s1PauseAt:0);
                            if(kind=="pause"){s1Paused=true;s1PauseAt=wall;s1PauseID=(string)payload["pause_id"];p1World.FreezeInputs();}
                            else{s1PauseTotal+=wall-s1PauseAt;s1Paused=false;p1World.ResumeInputs();p1NextTick=Time.realtimeSinceStartupAsDouble;}
                            var ack=new JObject{["protocol_version"]=Protocol,["assignment_id"]=assignment["assignment_id"],["boot_id"]=boot,["source_generation"]=sourceGeneration,["operation_id"]=item["operation_id"],["control_version"]=item["control_version"],["action"]=kind,["effective_frame"]=p1World.WorldTick,["game_elapsed_ms"]=game,["wall_elapsed_ms"]=wall,["manual_pause_ms"]=wall-game,["pause_id"]=payload["pause_id"],["clock_revision"]=s1Revision+1,["checkpoint_hash"]=Hash(Encoding.UTF8.GetBytes(Canonical(p1World.Capture())))};
                            if(p3Stage){if(p3ClockAcks.Count>=256)throw new InvalidOperationException("P3 clock operation budget");p3ClockAcks[operation]=ack;}
                            var receipt=await Internal("runs/"+assignment["run_id"]+"/control-acks",ack);s1Revision=(long)receipt["clock_revision"];controlVersion=(long)item["control_version"];
                            if(p3Stage)p3ControlDraining=false;
                            BroadcastS1Clock();
                        }
                        else {p1World.Leave((string)payload["player_id"]);if(p3Stage){p3Left.Add((string)payload["player_id"]);p3Rebase=p3Pending.Count>0;}controlVersion=(long)item["control_version"];if(s1Paused){s1PauseTotal+=S1Wall-s1PauseAt;s1Paused=false;p1World.ResumeInputs();}}
                        if(p3Stage)p3CommandCursor=controlVersion;
                    }
                    controlVersion=p3Stage?p3CommandCursor:(long)commands["control_version"];s1HealthyAt=Time.realtimeSinceStartupAsDouble;s1RequestAt=0;
                    if(p3Stage&&!s1Paused){await SubmitP3();if(!running)return;}
                    else if(!p3Stage&&!s1Paused&&Time.realtimeSinceStartupAsDouble>=frameAt)
                    {
                        var game=S1Game;var wall=S1Wall;
                        var frame=new JObject{["schema_version"]=Protocol,["source"]="unity",["run_id"]=assignment["run_id"],["instance_id"]="dc_local_01",["boot_id"]=boot,["assignment_id"]=assignment["assignment_id"],["source_generation"]=sourceGeneration,["sequence"]=appliedSequence+1,["frame_id"]=Guid.NewGuid().ToString(),["world_frame_id"]=Math.Max(++worldFrame,p1World.WorldTick),["elapsed_ms"]=game,["wall_elapsed_ms"]=wall,["manual_pause_ms"]=wall-game,["control_version"]=controlVersion,["facts"]=new JArray(new JObject {["event_id"]=Guid.NewGuid().ToString(),["kind"]="phase",["actor_player_id"]=null,["target_id"]="s1_active",["action_id"]=null,["effective_amount"]=0,["life_generation"]=0,["downed_generation"]=0,["event_game_ms"]=game,["contributors"]=new JArray(),["proof"]=null}),["terminal"]=null,["summary"]=null};
                        worldFrame=(long)frame["world_frame_id"];frame["fingerprint"]=Hash(Encoding.UTF8.GetBytes(Canonical(frame)));s1RequestAt=Time.realtimeSinceStartupAsDouble;
                        JObject receipt;
                        try{receipt=await Internal("runs/"+assignment["run_id"]+"/frames",frame);}catch(RequestFailure failure)when(failure.Code==40982){s1RequestAt=0;continue;}
                        appliedSequence=(long)receipt["applied_sequence"];s1RequestAt=0;s1HealthyAt=Time.realtimeSinceStartupAsDouble;
                        if((string)receipt["run_status"]!="running"){running=false;BroadcastS1Clock();return;}
                        if(p1World.Terminal){await ConfirmP1Result(game);running=false;return;}
                        frameAt=Time.realtimeSinceStartupAsDouble+1;
                    }
                }
                }catch(RequestFailure failure)when(p3Stage&&!running&&assignment==null&&failure.Status==403){var recovered=await Internal("instances/dc_local_01/claim",new JObject{["boot_id"]=boot,["versions"]=versions});if(!(recovered["assignment"] is JObject recoveredManifest))throw;assignment=recoveredManifest;heartbeatAt=0;}catch(RequestFailure failure)when(p3Stage&&failure.Status>=500){}catch(System.Net.Http.HttpRequestException)when(p3Stage){}catch(TaskCanceledException)when(p3Stage&&!stopping.IsCancellationRequested){}
                if(p3Stage&&running&&Time.realtimeSinceStartupAsDouble-s1HealthyAt>10)throw new InvalidOperationException("P3 reliable service recovery expired");
                if(p3Stage&&p3Pending.Count>0&&Time.realtimeSinceStartupAsDouble-p3Pending.Peek().At>10)throw new InvalidOperationException("P3 oldest unconfirmed fact expired");
                await Task.Delay(100,stopping.Token);
            }
        }
        private void BroadcastS1Clock()
        {
            foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)))Send("dc.s1.clock.v4",identity.Key,new JObject{["protocol_version"]=Protocol,["run_id"]=assignment["run_id"],["source_generation"]=sourceGeneration,["connection_generation"]=identity.Value["connection_generation"],["paused"]=s1Paused,["world_frame_id"]=p1World?.WorldTick??0,["clock_revision"]=s1Revision,["pause_id"]=s1Paused?s1PauseID:null,["game_elapsed_ms"]=S1Game,["wall_elapsed_ms"]=S1Wall,["manual_pause_ms"]=S1Wall-S1Game});
        }
        private void UpdateS1()
        {
            UpdateV6Acks();
            if(v5Runtime&&role=="client"&&p1View!=null&&p1Controls!=null)p1View.V5MenuVisible=p1Controls.Menu;
            if(role=="server")
            {
                if(multiplayerStage)DrainP2Baselines();
                if(!running||p1World==null)return;
                s1Waiting=(s1RequestAt>0&&Time.realtimeSinceStartupAsDouble-s1RequestAt>0.25)||Time.realtimeSinceStartupAsDouble-s1HealthyAt>0.35;
                if(p3Stage&&p3Pending.Count>0&&Time.realtimeSinceStartupAsDouble-p3Pending.Peek().At>0.25)s1Waiting=true;
                if(p2Stage&&!s1Paused){RememberP3Wait(s1Waiting,S1Game);p1World.TechnicalWait(s1Waiting,S1Game);}
                if(!s1Paused&&!s1Waiting&&!(p3Stage&&p3ControlDraining)){var steps=0;while(Time.realtimeSinceStartupAsDouble>=p1NextTick&&steps++<4&&!p1World.Terminal){if(DcV6Profile.Active&&p1World.NextV6GameTick>S1Game){p1NextTick=Time.realtimeSinceStartupAsDouble+(p1World.NextV6GameTick-S1Game)/1000d;break;}var tickAt=System.Diagnostics.Stopwatch.GetTimestamp();if(p3Stage)TickP3(DcV6Profile.Active?p1World.NextV6GameTick:S1Game);else{p1World.FlushFoundationInputs(S1Game);p1World.Tick(S1Game);}var tickMS=(System.Diagnostics.Stopwatch.GetTimestamp()-tickAt)*1000d/System.Diagnostics.Stopwatch.Frequency;s1TickMilliseconds+=tickMS;s1TickMaxMilliseconds=Math.Max(s1TickMaxMilliseconds,tickMS);s1ServiceTicks++;p1NextTick+=1d/30;}if(steps>4)p1NextTick=Time.realtimeSinceStartupAsDouble;}else p1NextTick=Time.realtimeSinceStartupAsDouble;
                foreach(var pending in p1Pending.ToArray()){
                    var reason=p1World.Receipt(pending.Key);if(reason=="pending")continue;
                    if(identities.ContainsKey(pending.Value))Send("dc.action.receipt.v3",pending.Value,new JObject{["protocol_version"]=Protocol,["run_id"]=assignment["run_id"],["connection_generation"]=identities[pending.Value]["connection_generation"],["action_id"]=pending.Key,["status"]=reason=="accepted"||reason=="buffered"?"accepted":"rejected",["reason"]=reason,["effective_frame"]=p1World.WorldTick});
                    p1Pending.Remove(pending.Key);
                }
                if(weaponStage)FlushS2Receipts();
                if(Time.realtimeSinceStartupAsDouble>=s1NextDynamic)
                {
                    s1NextDynamic+=(Math.Floor((Time.realtimeSinceStartupAsDouble-s1NextDynamic)*30)+1)/30;var snapshot=p2Stage?p1World.RuntimeSnapshot():p1World.Snapshot();snapshot["service_tick_mean_ms"]=s1ServiceTicks>0?s1TickMilliseconds/s1ServiceTicks:0;snapshot["service_tick_max_ms"]=s1TickMaxMilliseconds;snapshot["dynamic_sent_bytes"]=s1DynamicSentBytes;snapshot["baseline_sent_bytes"]=s1BaselineSentBytes;
                    if(DcV6Profile.Active)PrepareV6Network(snapshot);
                    var signature=string.Join("|",DcS1Replica.Groups.Where(group=>!v5Runtime||group!="projectiles").SelectMany(group=>((JArray)snapshot[group]).Select(item=>DcS1Replica.Key(group,item))));
                    if(p2Stage&&!v5Runtime)signature+="|"+string.Join("|",((JArray)snapshot["spawn_reservations"]).Select(item=>(string)item["id"]).Concat(((JArray)snapshot["enemies"]).Select(item=>item["id"]+":"+item["warning"])).Concat(((JArray)snapshot["players"]).Select(item=>item["player_id"]+":"+item["room_id"])));
                    if(v5Runtime)signature+="|"+string.Join("|",((JArray)snapshot["players"]).Select(item=>item["player_id"]+":"+item["room_id"]));
                    if(signature!=s1EntitySignature||Time.realtimeSinceStartupAsDouble>=s1NextState)
                    {
                        s1EntitySignature=signature;s1NextState=Time.realtimeSinceStartupAsDouble+(v5Runtime?1:0.2);s1StateRevision++;snapshot["protocol_version"]=Protocol;snapshot["config_hash"]=configHash;snapshot["source_generation"]=sourceGeneration;snapshot["state_revision"]=s1StateRevision;snapshot["result_confirmed"]=p1ResultConfirmed;
                        foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)))SendS1Baseline(identity.Key,snapshot,identity.Value);
                    }
                    if(DcV6Profile.Active)SendV6Lifecycle();
                    if(v5Runtime){snapshot["reliable_json_sent_bytes"]=v5ReliableJsonSentBytes;SendV5ActorState(snapshot);}
                    foreach(var identity in identities.Where(item=>S1ConnectionReady(item.Key)))foreach(var packet in DcV6Profile.Active?EncodeV6Network(snapshot,identity):DcS1Replica.Encode(v5Runtime?snapshot:p2Stage?DcP21Interest.ForPlayer(snapshot,(string)identity.Value["player_id"]):snapshot,s1StateRevision,sourceGeneration,(long)identity.Value["connection_generation"],Protocol,v5Runtime?(string)identity.Value["player_id"]:null)){s1DynamicSentBytes+=packet.Length;using var writer=new FastBufferWriter(packet.Length,Allocator.Temp);writer.WriteBytesSafe(packet);network.CustomMessagingManager.SendNamedMessage(WireName("dc.s1.dynamic.v4"),identity.Key,writer,NetworkDelivery.Unreliable);}
                }
                return;
            }
            if(!loadedConfirmed||p1View==null||!network.IsConnectedClient||p1ResultConfirmed)return;
            if(weaponStage&&p1Snapshot==null)return;
            if(s1PausePending&&Time.realtimeSinceStartupAsDouble-s1PauseRequestedAt>3){s1PausePending=false;QueueS1MenuCancel(p1Controls.Menu);p1View.Status("暂停确认超时，世界状态由服务决定；请重试Esc或退出");}
            var input=p1Controls.Read(sentSequence+1,p1View.AimOrigin,p1View.PointerWorld(p1Controls.Pointer));var actions=p1Controls.Drain();
            if(DcV8Profile.Active&&DcBootstrap.Argument("-dc-s2-probe","false")=="true")input.Charge=v8ProbeChargeHeld;
            UpdateP4();
            if(p2ProbeInput){input.Move=p2ProbeMovement;input.Attack=p2ProbeAttack;input.Aim=0;actions=Array.Empty<string>();}
            if(p2ProbeCombat&&p1Snapshot!=null)input=DcP21Pilot.Decide(p1Snapshot,p2PilotGeometry,sentSequence+1,playerID);
if(p2Stage&&DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&p1Snapshot!=null){input=DcP21Pilot.Decide(p1Snapshot,p2PilotGeometry,sentSequence+1,playerID);var automated=new List<string>();if(((JArray)p1Snapshot["enemies"]).Count>0&&Time.realtimeSinceStartupAsDouble>=p2PilotSkill){p2PilotSkill=Time.realtimeSinceStartupAsDouble+0.3;if(v5Runtime)automated.AddRange(DcV5Pilot.Skills(p1Snapshot,playerID,p2PilotGeometry));else{automated.Add("ability_primary");automated.Add("ability_secondary");}}if(Time.realtimeSinceStartupAsDouble>=p2PilotDodge&&DcP21Pilot.Dodge(p1Snapshot,playerID,input.Move)){automated.Add("dodge");p2PilotDodge=Time.realtimeSinceStartupAsDouble+0.3;}actions=automated.ToArray();}
            if(!weaponStage&&DcBootstrap.Argument("-dc-p1-autoplay","false")=="true"&&p1Snapshot!=null)input=DcP1Pilot.Decide(p1Snapshot,playerID,input.Sequence);
            if(p4Stage&&DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&p1Snapshot!=null)DcP4Pilot.Route(p1Snapshot,p2PilotGeometry,playerID,ref input);
            var p3RescueApproach=p3Stage&&DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&p1Snapshot!=null&&DcP3Pilot.Rescue(p1Snapshot,p2PilotGeometry,playerID,ref input,DcBootstrap.Argument("-dc-p3-rescue","false")=="true");
            if(p3RescueApproach)actions=Array.Empty<string>();
            if(DcV6Profile.Active&&(p2ProbeCombat||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true")&&p1Snapshot!=null)DcV6Pilot.Route(p1Snapshot,p2PilotGeometry,playerID,ref input,ref actions,p3RescueApproach);
            if(DcV7Profile.Active&&(p2ProbeCombat||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true")&&p1Snapshot!=null)DcV7Pilot.Adapt(p1Snapshot,p2PilotGeometry,playerID,ref input,ref actions,p3RescueApproach);
            if(DcV8Profile.Active&&(p2ProbeCombat||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true")&&p1Snapshot!=null)DcV8CoopPilot.FilterCombatActions(p1Snapshot,p2PilotGeometry,playerID,input,ref actions);
            if(DcV8Profile.Active&&p3RescueApproach&&p1Snapshot!=null)DcV8CoopPilot.PrepareRescue(p1Snapshot,playerID,ref input,ref actions,p2PilotGeometry);
            if(DcV7Profile.Active&&(p2ProbeCombat||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"))DcV7Pilot.InteractionGuard(input,ref actions);
            SuppressP4Pointer(ref input,ref actions);
            // Apply the willing-defeat probe after every movement/combat router.
            // It supplies ordinary idle input; the authority still owns all damage.
            if(DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&DcBootstrap.Argument("-dc-p2-wipe","false")=="true"&&p1Snapshot!=null){var own=p1Snapshot["players"].First(member=>(string)member["player_id"]==playerID);var probeRoom=(int)own["room_id"];if(probeRoom>0&&probeRoom<6&&(string)p1Snapshot["rooms"][probeRoom]["status"]=="active"){input.Move=Vector2.zero;input.Attack=input.Melee=input.Charge=input.Interact=false;actions=Array.Empty<string>();}}

            if(!s1Paused&&!s1PausePending)
            {
                actions=S1MenuActions(actions);
                if(DcV7Profile.Active&&DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&p1Snapshot!=null)DcV6Pilot.Evidence(p1Snapshot,p2PilotGeometry,playerID,input,actions);
                if(weaponStage){UpdateS2Input(input,actions);if(DcV7Profile.Active)p1View.V7HeldPreview(input);}
                else if(Time.unscaledTime>=nextInput||actions.Length>0){nextInput=Time.unscaledTime+1f/30;input.Sequence=++sentSequence;Send("dc.input.v3",NetworkManager.ServerClientId,new JObject {["protocol_version"]=Protocol,["message"]=WireName("dc.input.v3"),["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["input_sequence"]=input.Sequence,["move_x"]=Mathf.RoundToInt(input.Move.x*1000),["move_y"]=Mathf.RoundToInt(input.Move.y*1000),["aim_u16"]=input.Aim,["ranged_held"]=input.Attack,["melee_held"]=input.Melee,["interact_held"]=input.Interact,["rescue_held"]=false},NetworkDelivery.UnreliableSequenced);}
                input.Sequence=sentSequence;if(!weaponStage)foreach(var action in actions){p1View.InputStart(action);SendP1Action(action,input);}
                if(s1Replica.Snapshot!=null){if(s1ReplicaFrame!=s1Replica.LatestFrame){s1Prediction.Correct(s1Replica,playerID);s1ReplicaFrame=s1Replica.LatestFrame;}var own=((JArray)s1Replica.Snapshot["players"]).First(item=>(string)item["player_id"]==playerID);s1Prediction.Move(input,Time.unscaledDeltaTime,actions.Contains("dodge")&&(long)own["dodge_remaining_ms"]==0);p1View.VisualPosition(s1Prediction.RenderPosition);if(DcV7Profile.Active)p1View.VisualBodyFacing(s1Prediction.BodyFacing);}
                if(!weaponStage&&DcBootstrap.Argument("-dc-p1-autoplay","false")=="true"&&p1Snapshot!=null&&Time.realtimeSinceStartupAsDouble>=p1NextPilotAction){p1NextPilotAction=Time.realtimeSinceStartupAsDouble+0.25;var kind=DcP1Pilot.Action(p1Snapshot,playerID);if(kind!=null)SendP1Action(kind,input);}
            }
            foreach(var position in s1Replica.Visuals())if(position.Key!="players:"+playerID)p1View.VisualEntity(position.Key,position.Value);
        }
        private void S1Baseline(ulong connection,FastBufferReader reader)
        {
            try{ApplyS1Baseline(Read(reader,60000));}catch(Exception error){RecordProbeNetwork("baseline",error);network.Shutdown();p1View?.Status("基线校验失败，已停止战斗连接");}
        }
        private void P2BaselineChunk(ulong connection,FastBufferReader reader)
        {
            try{var length=reader.Length-reader.Position;if(length>900||length<66)throw new InvalidDataException("P2 baseline packet");var packet=new byte[length];reader.ReadBytesSafe(ref packet,length);var value=p2Baseline.Receive(packet,sourceGeneration,connectionGeneration,Time.realtimeSinceStartupAsDouble,s1Replica.Revision);if(value!=null)ApplyS1Baseline(value);}catch(Exception error){RecordProbeNetwork("baseline_chunk",error);network.Shutdown();p1View?.Status("恢复基线校验失败，已停止战斗连接");}
        }
        private void ApplyS1Baseline(JObject value)
        {
            if(p2ReconnectNotice&&value["outcome"]?.Type==JTokenType.Null){p1View.ClearStatus();p2ReconnectNotice=false;}
            if((int)value["protocol_version"]!=Protocol||(string)value["run_id"]!=runID||(string)value["config_hash"]!=configHash||(long)value["source_generation"]!=sourceGeneration||(long)value["connection_generation"]!=connectionGeneration)throw new InvalidDataException("S1 baseline binding");var revision=(int)value["state_revision"];if(revision<=s1Replica.Revision)return;if(weaponStage)PreserveLatestWeapon((JObject)((JArray)value["players"]).First(item=>(string)item["player_id"]==playerID));s1Replica.Baseline(value,playerID,revision);if(v5Runtime)v5ActorFrame=(long)value["world_frame_id"];s1Prediction.Correct(s1Replica,playerID);p1Snapshot=value;if(DcV8Profile.Active){if(!s1ClientBaselineReady)p1View.RestoreV8Presentation();FlushV8ActorStates();}p1View.Receive(value);if(weaponStage){var member=((JArray)value["players"]).First(item=>(string)item["player_id"]==playerID);p1Controls.WeaponState((string)member["active_weapon_id"],(uint)member["weapon_revision"],(bool)member["melee_equipped"]);s2LastAction=Math.Max(s2LastAction,(uint)member["last_action_sequence"]);}
            if(!s1ClientBaselineReady){s1ClientBaselineReady=true;p1Controls.SetEnabled(!s1Paused&&!p1Controls.Menu&&!p1ResultConfirmed&&value["outcome"]?.Type==JTokenType.Null);}
            if(v5Runtime&&((bool?)value[DcV6Profile.Active?"v6_projectiles_complete":"v5_projectiles_complete"]==true))Send(WireName("dc.baseline-ack.v5"),NetworkManager.ServerClientId,new JObject{["protocol_version"]=Protocol,["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["state_revision"]=revision},NetworkDelivery.ReliableSequenced);
            if(p3Stage&&!p1ResultConfirmed)_=RefreshP3Progress();
            if(p3Stage&&(bool?)value["result_confirmed"]==true){p1ResultConfirmed=true;_=LoadP3Result();}
            WriteS1Evidence(value);
        }
        private void WriteS1Evidence(JObject value,bool force=false)
        {
            if(value==null)return;
            var output=DcBootstrap.Argument("-dc-p1-evidence");
                            if(!string.IsNullOrEmpty(output)&&(force||Time.realtimeSinceStartupAsDouble>=s1EvidenceAt)){s1EvidenceAt=Time.realtimeSinceStartupAsDouble+1;var path=Path.GetFullPath(output);if(path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){var own=((JArray)value["players"]).First(item=>(string)item["player_id"]==playerID);File.WriteAllText(path,new JObject{["run_id"]=runID,["protocol_version"]=Protocol,["input_ack"]=own["last_input_sequence"],["world_frame_id"]=value["world_frame_id"],["damage"]=own["damage"],["contexts"]=6,["real_udp_world"]=true,["lifecycle_spawns"]=s1Replica.V6Spawned,["lifecycle_terminations"]=s1Replica.V6Terminated,["lifecycle_bounces"]=s1Replica.V6Bounced,["lifecycle_watermark"]=s1Replica.V6Watermark,["lifecycle_gaps"]=v6PreviousConnectionGaps+s1Replica.V6Gaps,["lifecycle_pending_unknown"]=s1Replica.V6UnknownCount,["max_dynamic_bytes"]=s1MaximumPacket,["dynamic_packets"]=s1Packets,["prediction_history"]=s1Prediction.HistoryCount,["corrections"]=s1Prediction.Corrections,["active_weapon"]=own["active_weapon_id"],["weapon_revision"]=own["weapon_revision"],["light_attacks"]=own["light_attacks"],["heavy_attacks"]=own["heavy_attacks"],["room_id"]=own["room_id"],["health"]=own["health"],["outcome"]=value["outcome"],["completed"]=value["completed"],["no_rewards"]=!p3Stage,["life_state"]=own["life_state"],["knockdowns"]=own["knockdowns"],["rescues"]=own["rescues"]}.ToString());}}
        }
        public int ProbeLifecycleTerminations=>s1Replica.V6Terminated;
        public void ProbeFlushWorldEvidence()=>WriteS1Evidence(p1Snapshot,true);
        private void S1Dynamic(ulong connection,FastBufferReader reader)
        {
            try{var remaining=reader.Length-reader.Position;if(remaining>900)return;s1MaximumPacket=Math.Max(s1MaximumPacket,remaining);s1Packets++;var bytes=new byte[remaining];reader.ReadBytesSafe(ref bytes,bytes.Length);if(s1Replica.Dynamic(bytes,sourceGeneration,connectionGeneration)&&v5Runtime&&p1Snapshot!=null)p1Snapshot["dynamic_elapsed_ms"]=s1Replica.GameMS;}
            catch(Exception error){RecordProbeNetwork("dynamic",error);network.Shutdown();}
        }
        private void S1ClockMessage(ulong connection,FastBufferReader reader)
        {
            var value=Read(reader,2048);if((string)value["run_id"]!=runID||(long)value["source_generation"]!=sourceGeneration||(long)value["connection_generation"]!=connectionGeneration)return;
            ConfirmS1Pause((bool)value["paused"],(long)value["clock_revision"],(string)value["pause_id"]);s1FreezeFrame=(long)value["world_frame_id"];s1PauseID=(string)value["pause_id"];s1Revision=(long)value["clock_revision"];s1PausePending=false;s1Prediction?.Clear();p1Controls.SetMenu(s1Paused);p1View.PauseVisuals(s1Paused);if(s1Paused)p1View.Status("已暂停：战斗、装填、技能、计时和场景音频全部冻结\nEsc明确恢复；后台连接与墙钟继续");else p1View.ClearStatus();
        }
        public bool ProbeReady=>loadedConfirmed&&p1Snapshot!=null&&s1ClientBaselineReady;
        public bool ProbePaused=>s1Paused;
        public bool ProbePauseAllowed=>assignment!=null&&(string)assignment["mode"]=="normal"&&((JArray)assignment["members"]).Count==1;
        public Vector2 ProbeRightDoorMove(){var p=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);return DcV7Pilot.EastDoorMove((JObject)p2PilotGeometry["rooms"][(int)p["room_id"]],p);}
        public bool ProbeFrozenReady=>s1Paused&&p1Snapshot!=null&&(long)p1Snapshot["world_frame_id"]==s1FreezeFrame;
        public JObject ProbeSnapshot=>p1Snapshot;
        public JArray ProbeNetworkEvents=>new JArray(p2NetworkEvents.Select(e=>e.DeepClone()));
        public async Task ProbePause(bool value)=>await S1PauseRequest(value,value?"menu":"explicit_resume");
        public bool ProbeMenuOpen=>p1Controls.Menu;
        public async Task ProbeMenu(bool value){p1Controls.SetMenu(value);p1View.V5MenuVisible=value;await S1PauseRequest(value,value?"menu":"explicit_resume");}
        public string ProbePlayerID=>playerID;
        public long ProbeConnection=>connectionGeneration;
        public int ProbeDynamicPackets=>s1Packets;
        public int ProbeDynamicBytes=>s1MaximumPacket;
        public int ProbeBaselineBytes=>p2Baseline.MaximumPacket;
        public void ProbeIntent(Vector2 movement,bool attack){p2ProbeInput=true;p2ProbeMovement=Vector2.ClampMagnitude(movement,1);p2ProbeAttack=attack;}
        public void ProbeCombat(){p2ProbeInput=true;p2ProbeCombat=true;}
        public void ProbeDisconnect(){network.Shutdown();}
    }
}
