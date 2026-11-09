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
    public sealed class DcP1ControlProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize(){if(DcBootstrap.Argument("-dc-controls-check","false")=="true")new GameObject("P1 input probe").AddComponent<DcP1ControlProbe>();}
        private IEnumerator Start()
        {
            yield return null;
            var mouse=InputSystem.AddDevice<Mouse>();var prior=InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                using var controls=new DcP1Controls();controls.SetEnabled(true);controls.Read(1,Vector2.zero,Vector2.right);
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));InputSystem.Update();InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.Update();
                var actions=controls.Drain();if(!actions.SequenceEqual(new[]{"melee_begin","melee_release"}))throw new InvalidOperationException("Input edges missing");
                InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));InputSystem.Update();controls.Drain();controls.SetEnabled(false);var canceled=controls.Drain();if(canceled.Contains("melee_release")||!canceled.Contains("cancel_input"))throw new InvalidOperationException("Disabled input released attack");
                File.WriteAllText(DcBootstrap.Argument("-dc-controls-output"),new JObject { ["player_input_system"]="1.19.0",["short_tap_edges"]=new JArray(actions),["disable_cancels_without_attack"]=true,["contexts"]=6,["real_player"]=true }.ToString());Application.Quit(0);
            }
            catch(Exception){Debug.LogError("P1 runtime input probe failed");Application.Quit(2);}
            finally{InputSystem.RemoveDevice(mouse);InputSystem.settings.updateMode=prior;}
        }
    }
}
