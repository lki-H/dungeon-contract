using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcP3FactFrontier
    {
        private readonly Dictionary<string,(long Frame,long Life)> approved=new Dictionary<string,(long,long)>(StringComparer.Ordinal);
        private static readonly HashSet<string> lateKinds=new HashSet<string>(new[]{"damage","kill","interact","reach","protection","control_effect","projectile_cleared","growth_choice"},StringComparer.Ordinal);
        public void Clear()=>approved.Clear();
        public void Confirm(JArray facts,long frame,JObject receipt)
        {
            if((string)receipt["status"]!="applied"||!(receipt["reinforcements"] is JObject actions))return;
            foreach(var fact in facts.Where(f=>(string)f["kind"]=="reinforcement_spawned")){
                var actor=(string)fact["actor_player_id"];var action=(string)fact["reinforcement_action_id"];
                if(actor==null||action==null||!(actions[action] is JObject spawn)||(string)spawn["status"]!="spawned"||spawn["player_id"]?.ToString()!=actor)continue;
                var life=checked((long)fact["life_generation"]+1);
                if(approved.TryGetValue(actor,out var prior)&&prior.Frame>=frame)continue;
                if(!approved.ContainsKey(actor)&&approved.Count>=4)throw new InvalidOperationException("P3 life frontier actor budget");
                approved[actor]=(frame,life);
            }
        }
        public void Align(JArray facts,long frame)
        {
            foreach(var fact in facts){
                var actor=(string)fact["actor_player_id"];var life=(long?)fact["life_generation"]??0;
                // The confirmed frame prefix has already advanced this actor's life.
                // Only later non-lifecycle facts may follow that prefix; projectile
                // origin life, attributes, event IDs and already-bound retries stay intact.
                if(actor!=null&&lateKinds.Contains((string)fact["kind"])&&approved.TryGetValue(actor,out var boundary)&&frame>boundary.Frame&&life>0&&life<boundary.Life)fact["life_generation"]=boundary.Life;
            }
        }
    }
    public sealed partial class DcP0V3
    {
        private bool p3Stage;
        private sealed class P3Step {public object Before;public JObject Intents;public long Game;public long Wall;public long Tick;public double At;public JArray Facts;public JObject Summary;public JObject Terminal;}
        private sealed class P3Ack {public long Tick;public JObject Receipt;public string Player;public bool Reconnect;public bool? Waiting;public long Game;}
        private readonly List<P3Ack> p3Acks=new List<P3Ack>();
        private readonly List<P3Step> p3History=new List<P3Step>();
        private readonly Queue<P3Step> p3Pending=new Queue<P3Step>();
        private readonly DcP3FactFrontier p3FactFrontier=new DcP3FactFrontier();
        private readonly HashSet<string> p3Left=new HashSet<string>();
        private JObject p3LatestReceipt;
        private JObject p3Sending;
        private long p3LastQueue;
        private long p3FactCount;
        private long p3ConfirmedTick;
        private bool p3Rebase;
        private bool p3ControlDraining;
        private string p3Token;
        private bool p3ResultLoading;
        private readonly Dictionary<string,JObject> p3ClockAcks=new Dictionary<string,JObject>();
        private readonly Dictionary<string,JObject> p3Presence=new Dictionary<string,JObject>();
        private async Task SubmitP3Presence()
        {
            foreach(var item in p3Presence.ToArray()){try{await Internal("runs/"+assignment["run_id"]+"/presence",item.Value);}catch(RequestFailure failure)when(failure.Status==403&&!p3Presence.ContainsKey(item.Key)){continue;}if(p3Presence.TryGetValue(item.Key,out var current)&&object.ReferenceEquals(current,item.Value))p3Presence.Remove(item.Key);}
        }
        private bool p3ProgressLoading;
        private double p3ProgressAt;
        private async Task RefreshP3Progress()
        {
            if(p3ProgressLoading||Time.realtimeSinceStartupAsDouble<p3ProgressAt||string.IsNullOrEmpty(p3Token))return;p3ProgressLoading=true;p3ProgressAt=Time.realtimeSinceStartupAsDouble+1;
            try{var response=await Request(publicBase+"/api/v2/runs/"+runID,p3Token,null);var state=response["data"]??response;p1View.P3Progress(state,configuration,playerID);if((string)state["status"]=="closed"){p1ResultConfirmed=true;p1Controls.Suspend(false);_=LoadP3Result();}}catch(Exception){}finally{p3ProgressLoading=false;}
        }

        private void TickP3(long game,long wall=-1)
        {
            var step=new P3Step{Before=p3History.Count==0||p1World.WorldTick%8==0?(DcV6Profile.P5((string)configuration?["definition_id"])?(object)p1World.CaptureRuntimeCheckpoint():p1World.Capture()):null,Intents=p1World.CaptureP3Intents(),Game=game,Wall=wall>=0?wall:game+s1PauseTotal,At=Time.realtimeSinceStartupAsDouble};
            if(p3History.Count>=96||p3Pending.Count>=64||p3FactCount>=9700||appliedSequence>=4000){p1World.AdvanceTime(game);p1World.AbortP3();step.Facts=new JArray();}
            else{p1World.FlushFoundationInputs(game);step.Facts=new JArray(p1World.Tick(game));}step.Tick=p1World.WorldTick;
            if(p1World.Terminal){step.Summary=p1World.Summary();step.Terminal=p1World.TerminalRequest(step.Tick,appliedSequence+1);}
            p3History.Add(step);
            if(step.Facts.Count>0||game-p3LastQueue>=1000||step.Terminal!=null)
            {
                if(step.Facts.Count==0)step.Facts.Add(P3Phase(game));
                p3Pending.Enqueue(step);p3LastQueue=game;
            }
        }
        private static JObject P3Phase(long game)=>new JObject{["event_id"]=Guid.NewGuid().ToString(),["kind"]="phase",["actor_player_id"]=null,["target_id"]="p3_active",["action_id"]=null,["effective_amount"]=0,["life_generation"]=0,["downed_generation"]=0,["event_game_ms"]=game,["contributors"]=new JArray(),["proof"]=null,["phase"]="active"};
        private void RebaseP3()
        {
            if(v5Runtime)v5BaselineRooms.Clear();
            if(p3Pending.Count==0){p3Rebase=false;return;}
            if(p1World.V6)foreach(var item in v6Streams){item.Value.ResetRoom(item.Value.Room,true);p2BaselinePump.Remove(item.Key);}
            var first=p3Pending.Peek();var anchor=p3History.Last(item=>item.Tick<=first.Tick&&item.Before!=null);var suffix=p3History.Where(item=>item.Tick>=anchor.Tick).ToArray();
            if(anchor.Before is DcP1World.RuntimeCheckpoint checkpoint)p1World.Restore(checkpoint);else p1World.Restore((JObject)anchor.Before);
            p3Pending.Clear();p3History.Clear();p3Sending=null;p3LastQueue=0;
            var controlApplied=false;
            foreach(var saved in suffix){if(saved.Tick>p3ConfirmedTick&&!controlApplied){foreach(var id in p3Left)p1World.Leave(id);controlApplied=true;}p1World.RestoreP3Intents(saved.Intents);if(saved.Tick<=p3ConfirmedTick){p1World.FlushFoundationInputs(saved.Game);p1World.Tick(saved.Game);}else TickP3(saved.Game,saved.Wall);foreach(var ack in p3Acks.Where(item=>item.Tick==saved.Tick)){if(ack.Receipt!=null)p1World.Acknowledge(ack.Receipt);else if(ack.Waiting.HasValue)p1World.TechnicalWait(ack.Waiting.Value,ack.Game);else if(ack.Reconnect)p1World.Reconnect(ack.Player);else p1World.Presence(ack.Player,false);}if(p1World.Terminal)break;}
            p3Rebase=false;s1EntitySignature=null;s1NextState=0;
        }
        private async Task SubmitP3()
        {
            if(p3Rebase)RebaseP3();
            for(var index=0;index<8&&p3Pending.Count>0;index++)
            {
                var step=p3Pending.Peek();
                if(p3Sending==null)
                {
                    if(appliedSequence>=4096||p3FactCount+step.Facts.Count>10000)throw new InvalidOperationException("P3 reliable fact budget exhausted");
                    p3FactFrontier.Align(step.Facts,step.Tick);
                    if(step.Terminal!=null)step.Terminal["cutoff_sequence"]=appliedSequence+1;
                    p3Sending=new JObject{["schema_version"]=Protocol,["source"]="unity",["run_id"]=assignment["run_id"],["instance_id"]="dc_local_01",["boot_id"]=boot,["assignment_id"]=assignment["assignment_id"],["source_generation"]=sourceGeneration,["sequence"]=appliedSequence+1,["frame_id"]=Guid.NewGuid().ToString(),["world_frame_id"]=step.Tick,["elapsed_ms"]=step.Game,["wall_elapsed_ms"]=step.Wall,["manual_pause_ms"]=step.Wall-step.Game,["control_version"]=controlVersion,["facts"]=step.Facts.DeepClone(),["terminal"]=step.Terminal?.DeepClone(),["summary"]=step.Summary?.DeepClone()};
                    p3Sending["fingerprint"]=Hash(Encoding.UTF8.GetBytes(Canonical(p3Sending)));
                }
                s1RequestAt=Time.realtimeSinceStartupAsDouble;
                JObject receipt;
                try{receipt=await Internal("runs/"+assignment["run_id"]+"/frames",p3Sending);}
                catch(RequestFailure failure)when(failure.Code==40982){s1RequestAt=0;p3Rebase=true;return;}
                catch(RequestFailure failure)when(failure.Status>=500){return;}
                catch(System.Net.Http.HttpRequestException){return;}
                catch(TaskCanceledException)when(!stopping.IsCancellationRequested){return;}
                if((long)receipt["applied_sequence"]!=(long)p3Sending["sequence"])throw new InvalidOperationException("P3 receipt prefix mismatch");
                appliedSequence=(long)receipt["applied_sequence"];p3ConfirmedTick=step.Tick;p3FactCount+=step.Facts.Count;p3LatestReceipt=(JObject)receipt.DeepClone();p3FactFrontier.Confirm(step.Facts,step.Tick,receipt);p3Acks.Add(new P3Ack{Tick=p1World.WorldTick,Receipt=p3LatestReceipt});p1World.Acknowledge(receipt);p3Pending.Dequeue();var anchor=p3History.LastOrDefault(item=>item.Tick<=step.Tick+1&&item.Before!=null);if(anchor!=null){p3History.RemoveAll(item=>item.Tick<anchor.Tick);p3Acks.RemoveAll(item=>item.Tick<anchor.Tick-1);}p3Sending=null;s1RequestAt=0;s1HealthyAt=Time.realtimeSinceStartupAsDouble;
                if((string)receipt["run_status"]!="running")
                {
                    p1ResultConfirmed=true;running=false;var snapshot=p1World.Snapshot();snapshot["protocol_version"]=Protocol;snapshot["config_hash"]=configHash;snapshot["source_generation"]=sourceGeneration;snapshot["runtime_stage"]=RuntimeStage;snapshot["result_confirmed"]=true;snapshot["state_revision"]=++s1StateRevision;
                    foreach(var identity in identities)SendS1Baseline(identity.Key,snapshot,identity.Value);return;
                }
            }
        }
        private async Task LoadP3Result()
        {
            if(p3ResultLoading||string.IsNullOrEmpty(p3Token))return;p3ResultLoading=true;
            try
            {
                for(var attempt=0;attempt<30&&!stopping.IsCancellationRequested;attempt++)
                {
                    JObject response;try{response=await Request(publicBase+"/api/v2/runs/"+runID+"/results",p3Token,null);}catch(System.Net.Http.HttpRequestException){await Task.Delay(500,stopping.Token);continue;}catch(RequestFailure failure)when(failure.Status>=500){await Task.Delay(500,stopping.Token);continue;}
                    var state=response["data"]??response;
                    if((string)state["status"]=="closed")
                    {
                        p1View.P3Result(state,state["contribution_cards"],playerID);return;
                    }
                    await Task.Delay(250,stopping.Token);
                }
                p1View.Status("战斗结果已确认，个人结算仍在重试；可稍后查询记录");
            }
            catch(Exception){if(!stopping.IsCancellationRequested)p1View.Status("结果查询暂不可用，已确认奖励由服务可靠处理");}finally{p3ResultLoading=false;}
        }
        private void RememberP3Connection(string player,bool reconnect){if(p3Stage&&p1World!=null)p3Acks.Add(new P3Ack{Tick=p1World.WorldTick,Player=player,Reconnect=reconnect});}
        private bool p3RememberedWait;
        private void RememberP3Wait(bool waiting,long game){if(p3Stage&&p1World!=null&&p3RememberedWait!=waiting){p3RememberedWait=waiting;p3Acks.Add(new P3Ack{Tick=p1World.WorldTick,Waiting=waiting,Game=game});}}
    }
}
