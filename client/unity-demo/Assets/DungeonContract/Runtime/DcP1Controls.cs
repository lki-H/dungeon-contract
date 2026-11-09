using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System.Linq;

namespace DungeonContract
{
    public sealed class DcP1Controls : IDisposable
    {
        public readonly InputActionAsset Asset;
        private readonly InputActionMap gameplay;
        private readonly InputAction move;
        private readonly InputAction ranged;
        private readonly InputAction melee;
        private readonly InputAction interact;
        private readonly Queue<string> actions = new Queue<string>();
        private bool canceling;
        private bool enabled;
        private bool requireRelease;
        private bool weaponRelease;
        private readonly List<RaycastResult> pointerHits=new List<RaycastResult>();
        private PointerEventData pointerEvent;
        private EventSystem pointerSystem;
        private readonly bool weapons;
        private readonly bool v5;
        private readonly InputAction charge;
        public bool MeleeEquipped{get;private set;}
        public string ActiveWeapon{get;private set;}
        public uint WeaponRevision{get;private set;}=1;
        public bool Menu { get; private set; }
        public bool FreezeMenu;
        public Action<bool> MenuChanged;
        public Action ChoiceToggled;
        public Action<int> ChoiceRequested;
        public Action<Vector2> MarkerRequested;
        private InputActionRebindingExtensions.RebindingOperation rebind;
        public bool Rebinding=>rebind!=null;
        public string RebindStatus{get;private set;}="";
        public Vector2 Pointer => Mouse.current?.position.ReadValue()??Vector2.zero;

        public DcP1Controls(bool weaponStage=false,bool version5=false)
        {
            weapons=weaponStage;v5=version5;
            Asset=ScriptableObject.CreateInstance<InputActionAsset>();
            foreach(var name in new[]{"Gameplay","ChoiceOverlay","Menu","Rebind","Spectator","Practice"})Asset.AddActionMap(new InputActionMap(name));
            gameplay=Asset.FindActionMap("Gameplay");move=gameplay.AddAction("Move",InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
            ranged=Button("Ranged","<Mouse>/leftButton",weapons?"attack_press":"ranged_press");
            if(weapons){melee=ranged;ranged.canceled+=context=>{if(!canceling&&enabled)Enqueue("attack_release");};Button("Switch","<Keyboard>/q","switch_weapon");var scroll=gameplay.AddAction("ScrollSwitch",InputActionType.Value,"<Mouse>/scroll/y");scroll.performed+=context=>{if(enabled&&Mathf.Abs(context.ReadValue<float>())>0.1f)Enqueue("switch_weapon");};}
            else{melee=Button("Melee","<Mouse>/rightButton","melee_begin");melee.canceled+=context=>{if(!canceling&&enabled&&!requireRelease)Enqueue("melee_release");};}
            if(v5){charge=Button("Charge","<Mouse>/rightButton","charge_begin");charge.canceled+=context=>{if(!canceling&&enabled&&!requireRelease&&ActiveWeapon=="rune_hammer")Enqueue("charge_release");};}
            interact=Button("Interact",weapons?"<Keyboard>/j":"<Keyboard>/e","interact_begin");interact.canceled+=context=>{if(!canceling&&enabled)Enqueue("interact_cancel");};
            Button("Dodge","<Keyboard>/space","dodge");Button("Primary",weapons?"<Keyboard>/e":"<Keyboard>/q","ability_primary");Button("Secondary","<Keyboard>/f","ability_secondary");Button("Reload","<Keyboard>/r","reload");
            if(v5&&DcP5Settings.Active)gameplay.AddAction("Mark",InputActionType.Button,"<Mouse>/middleButton").performed+=context=>{if(enabled&&!requireRelease&&!PointerCaptured())MarkerRequested?.Invoke(Pointer);};
            var menu=Asset.FindActionMap("Menu");var escape=menu.AddAction("Menu",InputActionType.Button,"<Keyboard>/escape");escape.performed+=context=>{Menu=!Menu;if(FreezeMenu)Suspend(!Menu);else SetEnabled(!Menu);MenuChanged?.Invoke(Menu);};
            var choice=Asset.FindActionMap("ChoiceOverlay");
            choice.AddAction("Toggle",InputActionType.Button,"<Keyboard>/tab").performed+=context=>{if(!Menu)ChoiceToggled?.Invoke();};
            for(var index=0;index<3;index++){var selected=index;choice.AddAction("Select"+(index+1),InputActionType.Button,"<Keyboard>/"+(index+1)).performed+=context=>{if(!Menu)ChoiceRequested?.Invoke(selected);};}
            if(v5&&DcP5Settings.Active)DcP5Settings.Current.Apply(Asset);
            menu.Enable();choice.Enable();
        }
        private InputAction Button(string name,string binding,string action)
        {
            var result=gameplay.AddAction(name,InputActionType.Button,binding);
            result.performed+=context=>{if(enabled&&!requireRelease&&(!v5||action!="charge_begin"||ActiveWeapon=="rune_hammer")&&(!weapons||action!="attack_press"||!weaponRelease)&&(!(context.control?.device is Mouse)||!PointerCaptured()))Enqueue(action);};return result;
        }
        private void Enqueue(string action){if(actions.Count<32)actions.Enqueue(action);}
        public void SetEnabled(bool value)
        {
            canceling=true;enabled=value;actions.Clear();requireRelease=true;
            if(value)gameplay.Enable();else {gameplay.Disable();Enqueue("cancel_input");}
            canceling=false;
        }
        public void Suspend(bool value)
        {
            canceling=true;enabled=value;actions.Clear();requireRelease=true;
            if(value)gameplay.Enable();else gameplay.Disable();canceling=false;
        }
        public void SetMenu(bool value){Menu=value;Suspend(!value);}
        public void FinishRun(){rebind?.Cancel();canceling=true;enabled=false;Menu=false;actions.Clear();Asset.Disable();canceling=false;requireRelease=true;weaponRelease=true;}
        public DcP1Input Read(uint sequence,Vector2 player,Vector2 pointer)
        {
            if(weaponRelease&&!Pressed(ranged))weaponRelease=false;
            if(requireRelease&&!Pressed(ranged)&&!Pressed(melee)&&!Pressed(charge)&&!Pressed(interact))requireRelease=false;
            var direction=pointer-player;var radians=Mathf.Atan2(direction.y,direction.x);if(radians<0)radians+=Mathf.PI*2;
            return new DcP1Input {Sequence=sequence,Move=enabled?Vector2.ClampMagnitude(move.ReadValue<Vector2>(),1):Vector2.zero,Aim=(ushort)Mathf.RoundToInt(radians/(Mathf.PI*2)*65535),Attack=enabled&&!requireRelease&&!weaponRelease&&(!weapons||!MeleeEquipped)&&ranged.IsPressed()&&!Captured(ranged),Melee=enabled&&!requireRelease&&!weaponRelease&&(!weapons||MeleeEquipped)&&melee.IsPressed()&&!Captured(melee),Charge=v5&&enabled&&!requireRelease&&ActiveWeapon=="rune_hammer"&&charge.IsPressed()&&!Captured(charge),Interact=enabled&&!requireRelease&&interact.IsPressed()&&!Captured(interact),Placement=pointer,HasPlacement=true,WeaponID=ActiveWeapon,WeaponRevision=WeaponRevision};
        }
        public void WeaponState(string id,uint revision,bool meleeEquipped)
        {
            if(revision<WeaponRevision)return;
            if(weapons&&revision!=WeaponRevision){weaponRelease=true;actions.Clear();}
            ActiveWeapon=id;WeaponRevision=revision;MeleeEquipped=meleeEquipped;
        }
        public string[] Drain(){var result=actions.ToArray();actions.Clear();return result;}
        private static bool Pressed(InputAction action)=>action!=null&&action.controls.OfType<ButtonControl>().Any(control=>control.isPressed);
        private bool Captured(InputAction action)=>action.activeControl?.device is Mouse&&PointerCaptured();
        public string Display(string action,int binding=0)=>Asset.FindAction("Gameplay/"+action).GetBindingDisplayString(binding);
        public void BeginRebind(string actionName,int index)
        {
            if(!Menu||Rebinding)return;
            var action=Asset.FindAction("Gameplay/"+actionName,false);
            if(action==null||index<0||index>=action.bindings.Count||action.bindings[index].isComposite)return;
            var old=action.bindings[index].overridePath;var menuMap=Asset.FindActionMap("Menu");menuMap.Disable();RebindStatus="请按新按键；Esc取消";
            rebind=action.PerformInteractiveRebinding(index).WithExpectedControlType("Button").WithCancelingThrough("<Keyboard>/escape").WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithControlsExcluding("<Mouse>/scroll").OnMatchWaitForAnother(0.12f)
                .OnCancel(operation=>{RebindStatus="已取消改键";FinishRebind();})
                .OnComplete(operation=>
                {
                    var path=action.bindings[index].overridePath;
                    if(!DcP5Settings.AllowedPath(path)||DcP5Settings.Conflict(Asset,action,index,path)){if(old==null)action.RemoveBindingOverride(index);else action.ApplyBindingOverride(index,old);RebindStatus="该按键已占用或保留，请选择其他按键";}
                    else{DcP5Settings.Current.StoreBinding(action,index,path);RebindStatus="已保存："+action.GetBindingDisplayString(index);}
                    FinishRebind();
                });
            rebind.Start();
        }
        private void FinishRebind(){var previous=rebind;rebind=null;previous?.Dispose();Asset.FindActionMap("Menu").Enable();requireRelease=true;weaponRelease=true;actions.Clear();}
        public void CaptureChoicePointer()
        {
            weaponRelease=true;requireRelease=true;var retained=actions.ToArray();actions.Clear();
            foreach(var action in retained)if(action!="attack_press"&&action!="attack_release"&&action!="ranged_press"&&action!="melee_begin"&&action!="melee_release"&&action!="charge_begin"&&action!="charge_release"&&action!="charge_cancel")actions.Enqueue(action);
        }
        public bool PointerCaptured()
        {
            if(EventSystem.current==null)return false;
            if(pointerEvent==null||pointerSystem!=EventSystem.current){pointerSystem=EventSystem.current;pointerEvent=new PointerEventData(pointerSystem);}pointerEvent.position=Pointer;pointerHits.Clear();EventSystem.current.RaycastAll(pointerEvent,pointerHits);return pointerHits.Count>0;
        }
        public void Dispose(){canceling=true;rebind?.Dispose();rebind=null;Asset.Disable();UnityEngine.Object.Destroy(Asset);}
    }
}
