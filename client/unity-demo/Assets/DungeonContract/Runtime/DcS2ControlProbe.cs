using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DungeonContract
{
    public sealed class DcS2ControlProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if(DcBootstrap.Argument("-dc-s2-controls-check","false")=="true")new GameObject("S2 Input System probe").AddComponent<DcS2ControlProbe>();
        }
        private IEnumerator Start()
        {
            yield return null;
            var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();var mode=InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                using var controls=new DcP1Controls(true);controls.WeaponState("repeating_crossbow",1,false);controls.SetEnabled(true);controls.Read(1,Vector2.zero,Vector2.right);
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
                Require(controls.Drain().SequenceEqual(new[]{"attack_press","attack_release"}),"left short tap");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));InputSystem.Update();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();Require(controls.Drain().Length==0,"right attacks");
                foreach(var binding in new[]{(Key.Q,"switch_weapon"),(Key.E,"ability_primary"),(Key.F,"ability_secondary"),(Key.R,"reload"),(Key.J,"interact_begin"),(Key.Space,"dodge")})
                {
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(binding.Item1));InputSystem.Update();var actions=controls.Drain();Require(actions.Contains(binding.Item2),"key "+binding.Item1);InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();controls.Drain();
                }
                InputSystem.QueueStateEvent(mouse,new MouseState{scroll=new Vector2(0,120)});InputSystem.Update();Require(controls.Drain().Contains("switch_weapon"),"scroll");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();controls.Drain();controls.WeaponState("ruin_blade",2,true);Require(!controls.Read(2,Vector2.zero,Vector2.right).Melee,"switch held latch");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));InputSystem.Update();Require(controls.Drain().Contains("ability_primary"),"switch latch blocked skill");
                controls.SetEnabled(false);Require(!controls.Drain().Contains("attack_release"),"disable released attack");
                var path=Path.GetFullPath(DcBootstrap.Argument("-dc-s2-controls-output"));if(!path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Probe evidence path");
                File.WriteAllText(path,new JObject{["real_player"]=true,["human"]=false,["input_system"]="1.19.0",["left_short_tap"]=true,["right_unbound"]=true,["q_scroll_switch"]=true,["e_f_r_j_space"]=true,["held_switch_latch"]=true,["disable_no_release_attack"]=true}.ToString());Application.Quit(0);
            }
            catch(Exception exception){Debug.LogError("S2 control probe failed: "+exception.Message);Application.Quit(2);}
            finally{InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings.updateMode=mode;}
        }
        private static void Require(bool value,string name){if(!value)throw new InvalidOperationException(name);}
    }
}
