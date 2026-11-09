using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace DungeonContract {
 public sealed partial class DcP1View {
  // Explicit local practice UI probe only; uses normal Input System events, no world mutation.
  private IEnumerator V7AttachmentProbe(Keyboard keyboard,Mouse mouse){
   RequireP5((bool)snapshot["practice"],"attachment probe is practice only");var kit=(string)snapshot["players"].First(p=>(string)p["player_id"]==ownID)["loadout_id"];var rows=new JArray();var captured=false;var measuredTip=false;var observedTrail=false;
   foreach(var key in new[]{Key.D,Key.A,Key.W,Key.S}){
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));var until=Time.realtimeSinceStartupAsDouble+.16;
    while(Time.realtimeSinceStartupAsDouble<until){
     var t=(float)Time.realtimeSinceStartupAsDouble;var pointer=new Vector2(Screen.width*(.5f+.3f*Mathf.Cos(t*4)),Screen.height*(.5f+.3f*Mathf.Sin(t*4)));InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}.WithButton(MouseButton.Left));yield return new WaitForEndOfFrame();
     var actor=snapshot["players"].First(p=>(string)p["player_id"]==ownID);var center=V7Center(actor,Position);RequireP5(v7Weapons.TryGetValue(ownID,out var model)&&model.Root.activeSelf,"attached weapon exists");RequireP5(sprites.TryGetValue("weapon:v7hand:"+ownID,out var hand)&&hand.enabled,"attached hand exists");
     var handPoint=(Vector2)hand.transform.position-RoomOffset;var tip=(Vector2)model.Root.transform.position-RoomOffset;var gripDistance=Vector2.Distance(center,handPoint);var tipDistance=Vector2.Distance(center,tip);RequireP5(Math.Abs(gripDistance-.4f)<.003f,"grip follows predicted body during movement "+gripDistance+" center="+center+" hand="+handPoint);RequireP5(Math.Abs(tipDistance-(kit=="ranger"?1.333f:V8View?1.2f:3.25f))<.005f,"tip length follows changed melee radius kit="+kit+" actual="+tipDistance+" weapon="+actor["active_weapon_id"]+" center="+center+" tip="+tip+" phase="+actor["melee_action"]);
     if(!measuredTip){var sprite=model.Head.sprite;var pixels=sprite.texture.GetPixels32();var axis=model.Root.transform.right;var maximum=float.MinValue;for(var y=0;y<32;y++)for(var x=0;x<32;x++)if(pixels[y*32+x].a>0)foreach(var corner in new[]{new Vector2(x,y),new Vector2(x+1,y),new Vector2(x,y+1),new Vector2(x+1,y+1)}){var local=(corner-sprite.pivot)/sprite.pixelsPerUnit;var world=model.Head.transform.TransformPoint(local);maximum=Math.Max(maximum,Vector3.Dot(world-model.Root.transform.position,axis));}RequireP5(Math.Abs(maximum)<.003f,"actual opaque weapon pixels overshoot or undershoot calibrated tip "+maximum);measuredTip=true;}
     rows.Add(new JObject{["move_key"]=key.ToString(),["center_x"]=center.x,["center_y"]=center.y,["grip_distance"]=gripDistance,["tip_distance"]=tipDistance,["body_facing"]=V7Body(actor).Facing});
     observedTrail|=model.TrailUntil.Any(t=>t>ViewTime);
     if(!captured&&sprites.TryGetValue("weapon:v7wind:"+ownID,out var wind)&&wind.enabled){P5Capture("attachment-"+kit);captured=true;}
    }
   }
   InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState());RequireP5(rows.Count>8&&captured&&observedTrail,"active moving attachment capture and bounded decorative trail");RequireP5(!p5Fx.Any(f=>f.Until>ViewTime&&!f.Heavy&&!f.Trace&&f.Radius>=1),"legacy full-sector foot effect duplicated the attached blade/head wind");
   File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(V8View?"v8-":"v7-")+"attachment-"+kit+".json",new JObject{["profile"]=V8View?"dc_ruins.v8.p5.1":"dc_ruins.v7.p5.2",["scope"]="rendered_local_practice_normal_input_four_movements_and_pointer_rotation",["human"]=false,["world_modified"]=false,["real_udp"]=false,["graphics_api"]=SystemInfo.graphicsDeviceType.ToString(),["status"]="PASS",["frames"]=rows}.ToString());
  }
 }
}
