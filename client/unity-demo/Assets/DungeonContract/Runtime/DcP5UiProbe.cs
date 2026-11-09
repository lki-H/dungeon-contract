using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private bool p5ProbeStarted;
        private void StartP5UiProbe()
        {
            if(p5ProbeStarted||DcBootstrap.Argument("-dc-p5-ui-probe","false")!="true")return;
            if(DcBootstrap.Argument("-dc-settings-profile","")!="validation")throw new InvalidOperationException("P5 UI probe requires isolated validation settings");
            p5ProbeStarted=true;StartCoroutine(P5UiProbe());
        }
        private IEnumerator P5UiProbe()
        {
            var passed=false;var background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
            try
            {
                yield return new WaitForSecondsRealtime(0.5f);
                p5Controls.SetMenu(true);p5Controls.MenuChanged?.Invoke(true);V5MenuVisible=true;
                yield return new WaitForSecondsRealtime(0.2f);
                RequireP5(visualPaused,"practice real pause");RequireP5(p5Music!=null&&!p5Music.isPlaying&&p5Voices.All(voice=>!voice.isPlaying),"paused scene audio");var paused=(long)snapshot["elapsed_ms"];
                yield return new WaitForSecondsRealtime(0.3f);RequireP5((long)snapshot["elapsed_ms"]==paused,"paused game clock");
                ShowP5Page(0);yield return new WaitForEndOfFrame();P5Capture("help");
                ShowP5Page(1);var settings=DcP5Settings.Current;settings.TextScale=1.5f;settings.WarningScale=2;settings.ReducedFlash=true;settings.EffectsVolume=0;settings.MusicVolume=0;settings.Save();
                yield return new WaitForEndOfFrame();P5Capture("settings-large");
                RequireP5(p5Pages[1].GetComponentsInChildren<Text>().All(label=>label.fontSize>=24),"large setting text");
                ShowP5Page(0);yield return new WaitForEndOfFrame();var helpScroll=p5Help.GetComponentInParent<ScrollRect>();var helpPoint=RectTransformUtility.WorldToScreenPoint(null,p5Pages[0].transform.TransformPoint(new Vector3(200,-80,0)));InputSystem.QueueStateEvent(mouse,new MouseState{position=helpPoint,scroll=new Vector2(0,-360)});yield return new WaitForSecondsRealtime(0.15f);RequireP5(helpScroll.verticalNormalizedPosition<0.99f,"real mouse wheel scrolls enlarged help");InputSystem.QueueStateEvent(mouse,new MouseState());
                ShowP5Page(2);p5Controls.BeginRebind("Primary",0);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.K));
                yield return new WaitForSecondsRealtime(0.3f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                RequireP5(!p5Controls.Rebinding&&p5Controls.Asset.FindAction("Gameplay/Primary").bindings[0].effectivePath=="<Keyboard>/k","real Input System rebind");
                p5Controls.BeginRebind("Primary",0);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.J));
                yield return new WaitForSecondsRealtime(0.3f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                RequireP5(p5Controls.Asset.FindAction("Gameplay/Primary").bindings[0].effectivePath=="<Keyboard>/k","conflicting binding rejected");
                yield return new WaitForEndOfFrame();P5Capture("bindings-large");
                settings.ResetBindings(p5Controls.Asset);settings.TextScale=1;settings.WarningScale=1;settings.ReducedFlash=false;settings.EffectsVolume=0.65f;settings.MusicVolume=0.35f;settings.Save();
                p5Controls.SetMenu(false);p5Controls.MenuChanged?.Invoke(false);V5MenuVisible=false;yield return new WaitForSecondsRealtime(0.2f);RequireP5(!visualPaused&&(long)snapshot["elapsed_ms"]>paused,"resume world");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));yield return new WaitForSecondsRealtime(0.15f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());var equipDeadline=Time.realtimeSinceStartupAsDouble+1;while(!p5Controls.MeleeEquipped&&Time.realtimeSinceStartupAsDouble<equipDeadline)yield return null;
                RequireP5(p5Controls.MeleeEquipped,"real melee equip");InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));var observedSweep=false;var observeUntil=Time.realtimeSinceStartupAsDouble+0.4;while(Time.realtimeSinceStartupAsDouble<observeUntil){yield return null;if(sprites.TryGetValue(V7View?"weapon:v7wind:"+ownID:"v6:sweep:"+ownID,out var sweep)&&sweep.enabled)observedSweep=true;}InputSystem.QueueStateEvent(mouse,new MouseState());yield return new WaitForSecondsRealtime(0.35f);
                RequireP5(((bool?)snapshot["v6_stage"]==true?observedSweep:p5Fx.Count>0)&&p5Numbers.Count==0,"empty melee has real current-sweep feedback but no fabricated damage number");
                if(V7View)yield return V7AttachmentProbe(keyboard,mouse);if(V8View)yield return V8CombatProbe(keyboard,mouse);
                yield return new WaitForEndOfFrame();P5Capture("resumed");
                var teamFixture=new JObject{["players"]=new JArray(new JObject{["player_id"]="1",["health"]=100},new JObject{["player_id"]="2",["health"]=80}),["team_rooms"]=new JArray(new JObject{["player_id"]="2"},new JObject{["player_id"]="3"})};var teamRows=P5TeamEntries(teamFixture);RequireP5(teamRows.Length==3&&(int)teamRows.Single(item=>(string)item["player_id"]=="2")["health"]==80,"teammates appear once and retain authorized local data");
                File.WriteAllText(Path.Combine(DcBootstrap.DemoEvidenceRoot,(DcV8Profile.Active?"v8-":DcV7Profile.Active?"v7-":DcV6Profile.Active?"v6-":"")+"p5-ui-probe.json"),new JObject{["presentation_version"]=DcP5Settings.Version,["human"]=false,["real_player"]=true,["graphics_api"]=SystemInfo.graphicsDeviceType.ToString(),["resolution"]=Screen.width+"x"+Screen.height,["pause_clock"]=true,["pause_audio"]=true,["large_settings"]=true,["interactive_rebind"]=true,["conflict_rejected"]=true,["resume"]=true,["empty_melee_no_fake_damage"]=true,["teammate_rows_deduplicated"]=true,["isolated_preferences"]=true,["status"]="PASS"}.ToString());passed=true;
            }
            finally{InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;PlayerPrefs.DeleteKey("DungeonContract.P5.Settings.1.Validation");Application.Quit(passed?0:2);}
        }
        private static void RequireP5(bool condition,string name){if(!condition){Application.Quit(2);throw new InvalidOperationException("P5 UI: "+name);}}
        private static void P5Capture(string name){var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(DcBootstrap.DemoEvidenceRoot,(DcV8Profile.Active?"v8-":DcV7Profile.Active?"v7-":DcV6Profile.Active?"v6-":"")+"p5-ui-"+name+".png"),image.EncodeToPNG());Destroy(image);}
    }
}
