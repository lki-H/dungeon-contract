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
    public sealed class DcV5ControlProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize(){if(DcBootstrap.Argument("-dc-v5-controls-check","false")=="true")new GameObject("V5 real Input System probe").AddComponent<DcV5ControlProbe>();}
        private static void Require(bool condition,string name){if(!condition)throw new InvalidOperationException(name);}
        private IEnumerator Start()
        {
            yield return null;
            var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();var mode=InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                using var controls=new DcP1Controls(true,true);controls.WeaponState("repeating_crossbow",1,false);controls.SetEnabled(true);controls.Read(1,Vector2.zero,Vector2.right);
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();Require(controls.Drain().SequenceEqual(new[]{"attack_press","attack_release"}),"left short click");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));InputSystem.Update();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();Require(controls.Drain().Length==0,"ranged charge");
                controls.WeaponState("rune_hammer",2,true);controls.Read(2,Vector2.zero,Vector2.right);
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));InputSystem.Update();Require(controls.Read(3,Vector2.zero,Vector2.right).Charge,"held right charge");InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();Require(controls.Drain().SequenceEqual(new[]{"charge_begin","charge_release"}),"right episode edges");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));InputSystem.Update();Require(controls.Read(4,Vector2.zero,Vector2.right).Melee,"held left melee");controls.Drain();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();controls.Drain();
                foreach(var binding in new[]{(Key.Q,"switch_weapon"),(Key.E,"ability_primary"),(Key.F,"ability_secondary"),(Key.R,"reload"),(Key.J,"interact_begin"),(Key.Space,"dodge")}){InputSystem.QueueStateEvent(keyboard,new KeyboardState(binding.Item1));InputSystem.Update();Require(controls.Drain().Contains(binding.Item2),"binding "+binding.Item1);InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();controls.Drain();}
                controls.SetEnabled(false);Require(!controls.Drain().Contains("charge_release"),"menu synthetic charge release");
                var output=Path.GetFullPath(DcBootstrap.Argument("-dc-v5-controls-output"));if(!output.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Control probe path");
                File.WriteAllText(output,new JObject{["protocol_version"]=DcV8Profile.Active?8:DcV7Profile.Active?7:DcV6Profile.Active?6:5,["real_player"]=true,["human"]=false,["input_system"]="1.19.0",["left_press_hold_release"]=true,["right_hammer_charge"]=true,["ranged_right_inactive"]=true,["keys"]=true,["disable_no_charge_release"]=true}.ToString());Application.Quit(0);
            }
            catch(Exception failure){Debug.LogError("V5 control probe failed: "+failure.Message);Application.Quit(2);}
            finally{InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings.updateMode=mode;}
        }
    }
}
