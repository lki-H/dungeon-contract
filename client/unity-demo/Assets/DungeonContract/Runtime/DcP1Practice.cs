using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcP1Practice : MonoBehaviour
    {
        private DcP1World world;
        private DcP1View view;
        private DcP1Controls controls;
        private double nextTick;
        private uint sequence;
        private bool menuWasOpen;
        private bool foundation;
        private bool weaponStage;
        private bool p2Stage;
        private bool v5;
        private string leftEpisode;
        private string rightEpisode;
        private uint actionSequence;
        private string chargeAction;
        private bool automated;
        private long automatedDuration;
        private long nextSkill;
        private bool proofWritten;
        private DcP5RenderPressure p5Pressure;
        private JObject localSnapshot;
        private DcS1Replica localReplica;
        private DcS1Prediction localPrediction;
        private long localFrame=-1;
        private bool paused;
        private double pausedAt;
        private double pausedTotal;
        private long GameMilliseconds=>elapsed.ElapsedMilliseconds-(long)pausedTotal-(paused?elapsed.ElapsedMilliseconds-(long)pausedAt:0);
        private readonly System.Diagnostics.Stopwatch elapsed=new System.Diagnostics.Stopwatch();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if(DcBootstrap.Argument("-dc-p5-authority-probe","false")=="true")return;
            if((DcBootstrap.Argument("-dc-runtime","v2")=="v3"||DcBootstrap.Argument("-dc-runtime","v2")=="v4"||DcV6Profile.Modern)&&DcBootstrap.Argument("-dc-practice","false")=="true")new GameObject("Isolated P1 practice").AddComponent<DcP1Practice>();
        }
        private void Start()
        {
            v5=DcV6Profile.Modern;Application.runInBackground=true;foundation=v5||DcBootstrap.Argument("-dc-runtime","v2")=="v4";p2Stage=v5||DcBootstrap.Argument("-dc-stage","p0")=="p2";weaponStage=p2Stage||DcBootstrap.Argument("-dc-stage","p0")=="s2";if(foundation){QualitySettings.vSyncCount=0;Application.targetFrameRate=60;}
            var parameters=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"..","Content",DcV8Profile.Active?"parameters-v8-p5.json":DcV7Profile.Active?"parameters-v7-p5.json":DcV6Profile.Active?"parameters-v6-p5.json":v5?"parameters-v5-p4.json":p2Stage?"parameters-v4-p2-1.json":weaponStage?"parameters-v4-s1.json":"parameters-v3-p1.json")));var geometry=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"..","Content",DcV8Profile.Active?"world-v8-p5.json":DcV7Profile.Active?"world-v7-p5.json":DcV6Profile.Active?"world-v6-p5.json":v5?"world-v5-p4.json":p2Stage?"world-v4-p2-1.json":"world-v3-p1.json")));
            if(v5)geometry["parameters"]=parameters.DeepClone();
            var practice=DcBootstrap.Argument("-dc-p2-graybox","false")!="true";
            world=new DcP1World(new JObject { ["parameters"]=parameters,["config"]=new JObject{["definition_id"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":DcV6Profile.Active?"dc_ruins.v6.p5.2":DcP5Settings.Active?"dc_ruins.v5.p5.1":"isolated.practice",["lifecycle_stage"]=p2Stage?"room_camera_projectile_stage":weaponStage?"weapon_stage":"p1_single_player"},["p1_world"]=geometry,["practice"]=practice,["weapon_stage"]=weaponStage,["p2_stage"]=p2Stage,["seed"]=20261012,["mode"]=practice?"practice":"normal",["run_id"]="isolated_graybox",["members"]=new JArray(new JObject { ["player_id"]="1",["slot"]=0,["loadout_id"]=DcBootstrap.Argument("-dc-loadout","ranger") }) });
            view=gameObject.AddComponent<DcP1View>();view.Configure(geometry,"1");view.Exit=()=>Application.Quit();controls=new DcP1Controls(weaponStage,v5);view.Weapons=weaponStage;view.RoomCameraStage=p2Stage;controls.SetEnabled(true);view.Foundation=foundation;if(foundation){controls.FreezeMenu=true;controls.MenuChanged=SetPaused;gameObject.AddComponent<DcS1Metrics>();}elapsed.Start();nextTick=Time.realtimeSinceStartupAsDouble;
            view.AttachP5Controls(controls);
            if(DcP5Settings.Active){localSnapshot=world.RuntimeSnapshot();view.Receive(localSnapshot);view.VisualPosition(new Vector2((float)localSnapshot["players"][0]["x"],(float)localSnapshot["players"][0]["y"]));}
            automated=p2Stage&&DcBootstrap.Argument("-dc-p21-local-probe","false")=="true";automatedDuration=long.Parse(DcBootstrap.Argument("-dc-p21-duration-ms","90000"));
            if(!string.IsNullOrEmpty(DcBootstrap.Argument("-dc-p1-capture")))StartCoroutine(Capture());
            if(DcP5Settings.Active&&DcBootstrap.Argument("-dc-p5-pressure","false")=="true"){p5Pressure=gameObject.AddComponent<DcP5RenderPressure>();p5Pressure.Configure(view,world.RuntimeSnapshot());}
        }
        private void Update()
        {
            if(p2Stage&&elapsed.ElapsedMilliseconds>=3600000){Application.Quit(0);return;}
            if(p5Pressure!=null)return;
            if(world==null)return;if(v5)view.V5MenuVisible=controls.Menu;if(foundation&&paused)return;if(weaponStage)controls.WeaponState(world.ActiveWeapon("1"),world.WeaponRevision("1"),world.MeleeEquipped("1"));var intent=controls.Read(++sequence,view.AimOrigin,view.PointerWorld(controls.Pointer));if(foundation)world.QueueFoundationInput("1",intent);else world.Input("1",intent);
            if(automated){intent=DcP21Pilot.Decide(localSnapshot??world.Snapshot(),world.Definition,sequence);world.QueueFoundationInput("1",intent);if(((JArray)(localSnapshot??world.Snapshot())["enemies"]).Count>0&&GameMilliseconds>=nextSkill){nextSkill=GameMilliseconds+700;world.QueueAction("1","ability_primary",intent,Guid.NewGuid().ToString());world.QueueAction("1","ability_secondary",intent,Guid.NewGuid().ToString());}if(DcP21Pilot.Dodge(localSnapshot??world.Snapshot()))world.QueueAction("1","dodge",intent,Guid.NewGuid().ToString());}
            var actions=controls.Drain();foreach(var action in actions){view.InputStart(action);var id=Guid.NewGuid().ToString();if(weaponStage){intent.ActionSequence=++actionSequence;if(action=="attack_press")chargeAction=id;intent.PressActionID=action=="attack_release"?chargeAction:null;}if(v5){if(action=="attack_press")leftEpisode=id;if(action=="charge_begin")rightEpisode=id;intent.PressActionID=action=="attack_release"?leftEpisode:action=="charge_release"||action=="charge_cancel"?rightEpisode:null;}world.QueueAction("1",action,intent,id);}
            if(weaponStage){if(v5){intent.AttackEpisode=leftEpisode;intent.ChargeEpisode=rightEpisode;}intent.ActionWatermark=actionSequence;world.QueueFoundationInput("1",intent);}
            var steps=0;while(Time.realtimeSinceStartupAsDouble>=nextTick&&steps++<4){if(world.V6&&world.NextV6GameTick>GameMilliseconds){nextTick=Time.realtimeSinceStartupAsDouble+(world.NextV6GameTick-GameMilliseconds)/1000d;break;}var gameTick=world.V6?world.NextV6GameTick:foundation?GameMilliseconds:elapsed.ElapsedMilliseconds;if(foundation)world.FlushFoundationInputs(gameTick);world.Tick(gameTick);if(world.V6)world.DrainV6LifecycleRecords();nextTick+=1d/30;}
            if(steps>4)nextTick=Time.realtimeSinceStartupAsDouble;if(localSnapshot==null||localFrame!=world.WorldTick){localSnapshot=p2Stage?world.RuntimeSnapshot():world.Snapshot();view.Receive(localSnapshot);}var snapshot=localSnapshot;
            if(foundation){if(localReplica==null){localReplica=new DcS1Replica();localPrediction=new DcS1Prediction(world.Definition);}if(localFrame!=world.WorldTick){localReplica.Baseline(snapshot,"1",(int)world.WorldTick+1);localPrediction.Correct(localReplica,"1");localFrame=world.WorldTick;}localPrediction.Move(intent,Time.unscaledDeltaTime,actions.Contains("dodge")&&(long)snapshot["players"][0]["dodge_remaining_ms"]==0);view.VisualPosition(localPrediction.RenderPosition);if(DcV7Profile.Active)view.VisualBodyFacing(localPrediction.BodyFacing);}
            if(controls.Menu&&!foundation)view.Status("练习菜单：世界仍继续，Esc返回\n练习无合同/奖励/成绩");else if(menuWasOpen)view.ClearStatus();menuWasOpen=controls.Menu;
            if(automated&&!proofWritten&&elapsed.ElapsedMilliseconds>=automatedDuration){proofWritten=true;StartCoroutine(ProbeCapture());}
        }
        private void SetPaused(bool value)
        {
            if(paused==value)return;
            if(value){pausedAt=elapsed.ElapsedMilliseconds;world.FreezeInputs();}else{pausedTotal+=elapsed.ElapsedMilliseconds-pausedAt;world.ResumeInputs();nextTick=Time.realtimeSinceStartupAsDouble;}
            paused=value;controls.SetMenu(value);localPrediction?.Clear();view.PauseVisuals(value);if(value)view.Status("练习已暂停：全部游戏状态与音频冻结，Esc明确恢复");else view.ClearStatus();
        }
        private void OnApplicationFocus(bool value){if(automated||p5Pressure!=null||DcBootstrap.Argument("-dc-p5-ui-probe","false")=="true"||!string.IsNullOrEmpty(DcBootstrap.Argument("-dc-p1-capture")))return;if(foundation){if(!value&&controls!=null)SetPaused(true);}else controls?.SetEnabled(value&&!controls.Menu);}
        private IEnumerator Capture()
        {
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            var target=Path.GetFullPath(DcBootstrap.Argument("-dc-p1-capture"));
            if(!target.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Capture target outside Demo evidence");
            var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(target,texture.EncodeToPNG());Destroy(texture);Application.Quit(0);
        }
        private IEnumerator ProbeCapture()
        {
            yield return new WaitForEndOfFrame();var loadout=DcBootstrap.Argument("-dc-loadout","ranger");var directory=(DcBootstrap.DemoEvidenceRoot+"/");var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(directory+"p21-probe-"+loadout+".png",texture.EncodeToPNG());Destroy(texture);
            var snapshot=world.Snapshot();File.WriteAllText(directory+"p21-local-probe-"+loadout+".json",new JObject{["profile"]="dc_ruins.v4.p2.1",["scope"]="real_player_input_only_local_graybox",["human"]=false,["duration_wall_ms"]=elapsed.ElapsedMilliseconds,["world_frame_id"]=world.WorldTick,["current_room"]=snapshot["players"][0]["room_id"],["outcome"]=world.Outcome,["health"]=snapshot["players"][0]["health"],["damage"]=snapshot["players"][0]["damage"],["enemies"]=((JArray)snapshot["enemies"]).Count,["reservations"]=((JArray)snapshot["spawn_reservations"]).Count,["projectiles"]=((JArray)snapshot["projectiles"]).Count,["complete"]=snapshot["completed"]}.ToString());Application.Quit(0);
        }
        private void OnDestroy(){controls?.Dispose();}
    }
}
