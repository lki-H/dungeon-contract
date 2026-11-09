using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private bool p4Stage;
        private double p4NextChoice;
        private int p4ChoiceIndex;
        private double p4EvidenceAt;
        private bool p4PendingObserved;
        private bool p4ShieldTriggered;
        private bool p4ChoiceWhileMoving;
        private bool p4ProbeStarted;
        private bool p4ProbeDone;
        private readonly JObject p4Probe=new JObject();
        private int p4CaptureFrame=-1;
        private void ConfigureP4()
        {
            if(!p4Stage)return;
            p1View.ConfigureP4(index=>ChooseP4(index));
            p1Controls.ChoiceToggled=p1View.ToggleP4;
            p1Controls.ChoiceRequested=index=>{if(p1View.P4Open&&!p1Controls.Menu)ChooseP4(index);};
            p1View.P4VisibilityChanged=open=>
            {
                if(!open)return;p4CaptureFrame=UnityEngine.Time.frameCount;p1Controls.CaptureChoicePointer();
                if(p1Snapshot!=null&&!s1Paused&&!s1PausePending&&!s1Waiting)SendS2Action("cancel_input",p1Controls.Read(Math.Max(1,sentSequence),p1View.AimOrigin,p1View.PointerWorld(p1Controls.Pointer)));
            };
        }
        private void ChooseP4(int index)
        {
            if(!p4Stage||!ProbeReady||p1Snapshot==null||s1Paused||s1PausePending||s1Waiting||p1ResultConfirmed)return;
            var own=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);
            if(!(own["candidates"] is JArray candidates)||index<0||index>=candidates.Count)return;
            var input=p1Controls.Read(Math.Max(1,sentSequence),p1View.AimOrigin,p1View.PointerWorld(p1Controls.Pointer));
            p4ChoiceIndex=index;
            SendS2Action("growth_choice",input);
        }
        private void UpdateP4()
        {
            if(!p4Stage||p1Snapshot==null)return;
            RecordP4Evidence();
            if(DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"&&UnityEngine.Time.realtimeSinceStartupAsDouble>=p4NextChoice)
            {
                p4NextChoice=UnityEngine.Time.realtimeSinceStartupAsDouble+0.5;
                var own=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);
                if(((JArray)assignment["members"]).Count==1)
                {
                    if((int?)own["choice_node"]==0&&!p4ProbeStarted){p4ProbeStarted=true;_=ProbeP4Lifecycle();}
                    if(p4ProbeStarted&&!p4ProbeDone)return;
                    if((int?)own["choice_node"]==1)return;
                }
                if((int?)own["choice_node"]>=0)ChooseP4(v5Runtime?DcV5Pilot.Growth((JArray)own["candidates"]):0);
            }
        }
        private void SuppressP4Pointer(ref DcP1Input input,ref string[] actions)
        {
            if(!p1View.P4Open&&p4CaptureFrame!=UnityEngine.Time.frameCount)return;
            input.Attack=false;input.Melee=false;input.Interact=false;actions=actions.Where(action=>action=="cancel_input").ToArray();
        }
        private async Task ProbeP4Lifecycle()
        {
            try
            {
                var trial=(string)assignment["mode"]=="trial";
                var beforeFrame=(long)p1Snapshot["world_frame_id"];var beforeGame=(long)p1Snapshot["elapsed_ms"];
                await ProbePause(true);
                if(!trial)await WaitP4(()=>ProbeFrozenReady);
                if(!p1View.P4Open)p1View.ToggleP4();
                var capture=DcBootstrap.Argument("-dc-p4-evidence");
                if(!string.IsNullOrEmpty(capture)&&System.IO.Path.GetFullPath(capture).StartsWith(System.IO.Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))UnityEngine.ScreenCapture.CaptureScreenshot(capture+"-choice.png");
                var before=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID).DeepClone();
                await Task.Delay(1000,stopping.Token);
                var frozen=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);
                if(trial){p4Probe["trial_menu_not_paused"]=!s1Paused&&!s1PausePending&&UnityEngine.Time.timeScale==1;p4Probe["trial_choice_world_continued"]=(long)p1Snapshot["world_frame_id"]>beforeFrame&&(long)p1Snapshot["elapsed_ms"]>beforeGame;}
                else{p4Probe["pause_choice_frozen"]=JToken.DeepEquals(before["growth"],frozen["growth"])&&JToken.DeepEquals(before["upgrades"],frozen["upgrades"])&&UnityEngine.Time.timeScale==0;await ProbePause(false);await WaitP4(()=>!s1Paused);}
                var generation=connectionGeneration;var old=frozen["growth"].DeepClone();
                network.Shutdown();await WaitP4(()=>connectionGeneration>generation&&ProbeReady&&(long?)p1Snapshot?["connection_generation"]==connectionGeneration);
                var restored=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);
                var pending=restored["growth"][0];var original=old[0];
                p4Probe["reconnect_choice_preserved"]=JToken.DeepEquals(pending["candidates"],original["candidates"])&&JToken.DeepEquals(pending["candidate_hash"],original["candidate_hash"])&&JToken.DeepEquals(pending["deadline_game_ms"],original["deadline_game_ms"])&&pending["selected"].Type==JTokenType.Null;
                p4Probe["complete"]=true;
                if(p1View.P4Open)p1View.ToggleP4();
            }
            catch(Exception failure){p4Probe["complete"]=false;p4Probe["failure_type"]=failure.GetType().Name;}
            finally{p4ProbeDone=true;}
        }
        private async Task WaitP4(Func<bool> ready)
        {
            var until=UnityEngine.Time.realtimeSinceStartupAsDouble+10;
            while(!ready()){if(UnityEngine.Time.realtimeSinceStartupAsDouble>=until)throw new TimeoutException("P4 lifecycle probe");await Task.Delay(50,stopping.Token);}
        }
        private void RecordP4Evidence()
        {
            var own=p1Snapshot["players"].First(item=>(string)item["player_id"]==playerID);
            if((int?)own["pending_shield"]>0)p4PendingObserved=true;
            if(p4PendingObserved&&(int?)own["pending_shield"]==0&&(int?)own["shield"]>0)p4ShieldTriggered=true;
            if((int?)own["choice_node"]>=0&&s1Prediction.HistoryCount>0)p4ChoiceWhileMoving=true;
            var output=DcBootstrap.Argument("-dc-p4-evidence");
            if(string.IsNullOrEmpty(output)||UnityEngine.Time.realtimeSinceStartupAsDouble<p4EvidenceAt)return;
            var path=System.IO.Path.GetFullPath(output);
            var developerRoot=System.IO.Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+System.IO.Path.DirectorySeparatorChar;
            var playerRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DungeonContract","P4LAN"))+System.IO.Path.DirectorySeparatorChar;
            if(!path.StartsWith(developerRoot,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(playerRoot,StringComparison.OrdinalIgnoreCase))return;
            p4EvidenceAt=UnityEngine.Time.realtimeSinceStartupAsDouble+1;
            var evidence=new JObject{["profile"]=configuration["definition_id"],["run_id"]=runID,["real_udp"]=true,["human"]=DcBootstrap.Argument("-dc-p2-autoplay","false")!="true",["input_only"]=true,["world_frame_id"]=p1Snapshot["world_frame_id"],["elapsed_game_ms"]=p1Snapshot["elapsed_ms"],["growth"]=own["growth"].DeepClone(),["upgrades"]=own["upgrades"].DeepClone(),["rooms"]=p1Snapshot["rooms"].DeepClone(),["pending_shield_observed"]=p4PendingObserved,["shield_trigger_observed"]=p4ShieldTriggered,["supply_used"]=own["branch_supply_used"],["choice_with_prediction_history"]=p4ChoiceWhileMoving,["lifecycle_probe"]=p4Probe.DeepClone()};
            evidence["pending_shield_remaining"]=own["pending_shield"];evidence["loadout_id"]=own["loadout_id"];
            if(v5Runtime)evidence["reliable_json_sent_bytes"]=p1Snapshot["reliable_json_sent_bytes"];
            evidence["manual_input_mode"]=DcBootstrap.Argument("-dc-p2-autoplay","false")!="true";evidence["human"]=false;
            evidence["manifest_other_task_details_visible"]=assignment["task_requirements"] is JObject requirements&&requirements.Properties().Any(item=>item.Name!=playerID);
            evidence["max_baseline_bytes"]=ProbeBaselineBytes;evidence["max_dynamic_bytes"]=ProbeDynamicBytes;
            evidence["input_ack"]=own["last_input_sequence"];evidence["health"]=own["health"];evidence["life_state"]=own["life_state"];evidence["room_id"]=own["room_id"];
            foreach(var key in new[]{"service_tick_mean_ms","service_tick_max_ms","baseline_sent_bytes","dynamic_sent_bytes"})evidence[key]=p1Snapshot[key]?.DeepClone();
            System.IO.File.WriteAllText(path,evidence.ToString());
        }
    }
}
