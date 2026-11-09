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
  private JToken V8Own()=>snapshot["players"].First(p=>(string)p["player_id"]==ownID);
  private IEnumerator V8CombatProbe(Keyboard keyboard,Mouse mouse){
   RequireP5(V8View&&(bool)snapshot["practice"],"v8 normal-input practice only");
   var kit=(string)V8Own()["loadout_id"];var result=new JObject{["profile"]="dc_ruins.v8.p5.1",["loadout"]=kit,["scope"]="rendered_single_player_practice_normal_input",["world_modified"]=false,["human"]=false,["real_udp"]=false};
   var pointer=new Vector2(Screen.width*.75f,Screen.height*.5f);
   InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});yield return new WaitForSecondsRealtime(.8f);
   if(kit=="runesmith"){
    var stress=new System.Collections.Generic.List<FxP5>();while(p5Fx.Count<32){var renderer=new GameObject("explicit zero-area view pool fixture").AddComponent<SpriteRenderer>();renderer.transform.SetParent(root.transform);var f=new FxP5{Renderer=renderer,Begin=ViewTime,Until=ViewTime+15,Radius=0};p5Fx.Add(f);stress.Add(f);}foreach(var f in p5Fx)f.Until=ViewTime+15;result["presentation_pool_fixture"]=true;
    var waves=0;var glow=false;var releaseWind=false;
    for(var attempt=0;attempt<3;attempt++){
     var previous=(string)(V8Own()["last_slam"] as JObject)?["root"];
     InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}.WithButton(MouseButton.Right));
     var chargeUntil=Time.realtimeSinceStartupAsDouble+1.05;
     while(Time.realtimeSinceStartupAsDouble<chargeUntil){yield return new WaitForEndOfFrame();if(v7Weapons.TryGetValue(ownID,out var weapon))glow|=weapon.Head.color.r>.9f&&weapon.Head.color.g<.5f;}
     InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});var until=Time.realtimeSinceStartupAsDouble+1.4;var seen=false;
     while(Time.realtimeSinceStartupAsDouble<until){
      yield return new WaitForEndOfFrame();
      if(sprites.TryGetValue("weapon:v7wind:"+ownID,out var wind))releaseWind|=wind.enabled&&wind.color.r>.9f&&wind.color.g<.4f;
      if(!seen&&(string)(V8Own()["last_slam"] as JObject)?["root"]!=previous&&v8Waves.TryGetValue(ownID,out var wave)&&wave.Root.activeSelf){seen=true;waves++;P5Capture("v8-slam-"+attempt);}
     }
     RequireP5(seen,"every legal slam has reserved wave "+attempt);
    }
    foreach(var f in p5Fx)f.Until=ViewTime;
    RequireP5(glow&&releaseWind,"charge glow and release red wind");result["three_charged_impacts_visible"]=waves;result["charge_glow"]=glow;result["red_release_wind"]=releaseWind;
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));yield return new WaitForSecondsRealtime(.1f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());
    RequireP5((bool?)(V8Own()["death_hammer"] as JObject)?["active"]==true,"normal F accepted");
    RequireP5(v8Spins.TryGetValue(ownID,out var spin)&&spin.Root.activeSelf&&spin.Mesh.vertexCount==130,"red spin continuously rendered");P5Capture("v8-death-hammer");
    var shots=(uint?)V8Own()["emission_index"]??0;
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));yield return new WaitForSecondsRealtime(.12f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(.4f);
    RequireP5((string)V8Own()["active_weapon_id"]=="rune_bolt"&&(bool)V8Own()["death_hammer"]["active"],"switch remembers ranged without stopping F");
    yield return new WaitForSecondsRealtime(1.8f);
    RequireP5(!(bool)V8Own()["death_hammer"]["active"]&&((uint?)V8Own()["emission_index"]??0)==shots,"held attack cannot leak through or restart after spin");
    InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});yield return new WaitForSecondsRealtime(.1f);
    InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(.15f);InputSystem.QueueStateEvent(mouse,new MouseState{position=pointer});
    RequireP5(((uint?)V8Own()["emission_index"]??0)>shots,"fresh press resumes current weapon");result["spin_switch_and_release_gate"]=true;
   }
   var corners=new JArray();
   foreach(var keys in new[]{new[]{Key.A,Key.W},new[]{Key.D,Key.W},new[]{Key.D,Key.S},new[]{Key.A,Key.S}}){
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));yield return new WaitForSecondsRealtime(4.5f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.2f);yield return new WaitForEndOfFrame();
    var center=V7Center(V8Own(),Position);var projected=cameraView.WorldToViewportPoint(center+RoomOffset);
    RequireP5(Math.Abs(projected.x-.5f)<.08f&&Math.Abs(projected.y-.5f)<.08f,"corner camera keeps body near center "+projected);
    var panel=UiRoot.Find("V5 resources").GetComponent<RectTransform>();var points=new Vector3[4];panel.GetWorldCorners(points);var low=RectTransformUtility.WorldToScreenPoint(null,points[0]);var high=RectTransformUtility.WorldToScreenPoint(null,points[2]);var screen=cameraView.WorldToScreenPoint(center+RoomOffset);
    RequireP5(!new Rect(low,high-low).Contains(screen),"corner body outside HUD panel");
    corners.Add(new JObject{["keys"]=string.Join("+",keys),["body_x"]=center.x,["body_y"]=center.y,["viewport_x"]=projected.x,["viewport_y"]=projected.y,["camera_x"]=cameraView.transform.position.x-RoomOffset.x,["camera_y"]=cameraView.transform.position.y-RoomOffset.y});P5Capture("v8-corner-"+kit+"-"+corners.Count);
   }
   var original=(JObject)snapshot.DeepClone();var originalPosition=Position;var originalCamera=cameraView.transform.position;var layouts=new JArray();
   p5Controls.SetMenu(true);p5Controls.MenuChanged?.Invoke(true);V5MenuVisible=false;
   foreach(var roomID in new[]{1,5,8})foreach(var corner in new[]{0,1,2,3}){
    var fixture=(JObject)original.DeepClone();var actor=fixture["players"].First(x=>(string)x["player_id"]==ownID);var layout=geometry["rooms"][roomID];var width=(float)layout["width"];var height=(float)layout["height"];var at=new Vector2(corner==0||corner==3?2:width-2,corner<2?height-2:2);actor["room_id"]=roomID;actor["x"]=at.x;actor["y"]=at.y;
    foreach(var enemy in fixture["enemies"]){enemy["room_id"]=roomID;if(roomID==5){enemy["kind"]="boss";enemy["body_facing"]="down";enemy["body_profile_id"]="boss:down";enemy["phase"]=1;}}
    Receive(fixture);VisualPosition(at);cameraView.transform.position=CameraTarget(at);yield return new WaitForEndOfFrame();
    var body=V7Center(actor,Position);var projected=cameraView.WorldToViewportPoint(body+RoomOffset);RequireP5(Math.Abs(projected.x-.5f)<.01f&&Math.Abs(projected.y-.5f)<.01f,"big/boss/corridor camera target clamped");
    if(roomID==5)RequireP5(v5BossLabel.enabled,"Boss HUD visible in layout fixture");
    layouts.Add(new JObject{["room_id"]=roomID,["corner"]=corner,["viewport_x"]=projected.x,["viewport_y"]=projected.y,["camera_target_only_fixture"]=true});if(corner==0)P5Capture("v8-layout-"+kit+"-"+roomID);
   }
   Receive(original);VisualPosition(originalPosition);cameraView.transform.position=originalCamera;p5Controls.SetMenu(false);p5Controls.MenuChanged?.Invoke(false);V5MenuVisible=false;result["isolated_layout_fixtures"]=layouts;result["layout_fixtures_do_not_modify_world"]=true;
   result["corners"]=corners;result["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();result["resolution"]=Screen.width+"x"+Screen.height;result["status"]="PASS";
   File.WriteAllText(Path.Combine(DcBootstrap.DemoEvidenceRoot,"v8-combat-visual-"+kit+".json"),result.ToString());
  }
 }
}
