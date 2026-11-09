using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public sealed partial class DcP1View {
  private long v6VisualGame;private float v6VisualAt;
  private readonly HashSet<string> v6ContactSeen=new HashSet<string>();private readonly Queue<string> v6ContactOrder=new Queue<string>();
  private double V6Game {
   get{var current=Math.Max((long?)snapshot["elapsed_ms"]??0,(long?)snapshot["dynamic_elapsed_ms"]??0);if(current!=v6VisualGame){v6VisualGame=current;v6VisualAt=ViewTime;}return current+Math.Min(100,Math.Max(0,(ViewTime-v6VisualAt)*1000));}
  }
  private void V6WeaponPose(JToken own,JObject attack,Vector2 aim,ref Vector2 anchor,ref float degrees){
   var at=V6Game;var hand=Position+Vector2.up*0.25f;
   if(attack!=null){
    var active=(double)attack["attack_started_ms"]+((long)attack["active_tick"]-(long)attack["started_tick"])*1000d/30;
    var recovery=(double)attack["attack_started_ms"]+((long)attack["recovery_tick"]-(long)attack["started_tick"])*1000d/30;
    var end=(double)attack["attack_started_ms"]+((long)attack["end_tick"]-(long)attack["started_tick"])*1000d/30;
    var facing=new Vector2((float)attack["aim_x"],(float)attack["aim_y"]);if(facing.sqrMagnitude<0.01f||at<active)facing=aim.normalized;
    var center=Mathf.Atan2(facing.y,facing.x)*Mathf.Rad2Deg;
    if((bool)attack["heavy"]){var q=at<active?Mathf.Clamp01((float)((at-(double)attack["attack_started_ms"])/(active-(double)attack["attack_started_ms"]))):at<recovery?1:1-Mathf.Clamp01((float)((at-recovery)/Math.Max(1,end-recovery)));degrees=center+85*(1-q);anchor=hand+new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad))*0.55f;}
    else{var arc=(float)attack["arc"];if(arc<=0){var def=((JArray)geometry["parameters"]["loadouts"]).First(k=>(string)k["id"]==(string)own["loadout_id"]);arc=(float)def["melee"]["arc_deg"];}
     var q=Mathf.Clamp01((float)((at-active)/Math.Max(1,recovery-active)));degrees=at<active?center-arc/2:at<recovery?center-arc/2+arc*q:Mathf.LerpAngle(center+arc/2,center,Mathf.Clamp01((float)((at-recovery)/Math.Max(1,end-recovery))));anchor=hand+new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad))*0.55f;}
   }else if((long?)own["reload_until_ms"]>at){
    var duration=Math.Max(1,(int?)own["reload_duration_ms"]??900);var q=Mathf.Clamp01((float)((at-(long)own["reload_started_ms"])/duration));var lift=q<0.2f?q/0.2f:q<0.75f?1:1-(q-0.75f)/0.25f;degrees-=35*lift;anchor=hand+aim.normalized*0.45f+Vector2.down*0.15f*lift;
   }else anchor=hand+aim.normalized*0.45f;
  }
  private void V6Grip(SpriteRenderer weapon,JToken own,ref Vector2 anchor,float degrees){
   var id=(string)own["active_weapon_id"];var hammer=id=="rune_hammer";var scale=hammer?0.8f:0.55f;var grip=id=="ruin_blade"?new Vector2(9,25):hammer?new Vector2(10,29):id=="repeating_crossbow"?new Vector2(12,24):new Vector2(16,20);
   var hand=Position+Vector2.up*0.425f;
   if(sprites.TryGetValue("player:"+ownID,out var actor)){
    var key="player:"+ownID;var state=p5ActorStates.TryGetValue(key,out var current)?current:"idle";var pose=p5ActorStateAt.TryGetValue(key,out var began)?Math.Min(3,(int)((ViewTime-began)*12)):0;var handX=(string)own["loadout_id"]=="runesmith"?24.5f:22.5f;var facing=PointerWorld(p5Controls?.Pointer??Vector2.zero)-Position;if(DcP5Art.Direction(facing)=="left")handX=31-handX;var handY=18.5f-(state=="prepare"||state=="charge"?pose*2:0);hand=(Vector2)actor.transform.TransformPoint(new Vector3((handX-16)/16,(32-handY-5)/16,0))-RoomOffset;
   }
   var rotation=Quaternion.Euler(0,0,degrees-(hammer?60.9454f:45));var localGrip=new Vector2((grip.x-16)/16,(16-grip.y)/16);anchor=hand-(Vector2)(rotation*(localGrip*scale));weapon.transform.rotation=rotation;weapon.transform.localScale=Vector3.one*scale;
  }
  private void DrawV6Presentation(){
   if(room==7&&(string)snapshot["rooms"][7]["status"]=="cleared"){var point=DcV6Profile.SupplyPoint(geometry["rooms"][7]);var used=(bool)snapshot["players"].First(p=>(string)p["player_id"]==ownID)["branch_supply_used"];var marker=Draw("v6:supply",point,used?Color.gray:Color.white,Vector2.one*0.5f,6);marker.sprite=DcP5Art.Get("icon.health");}
   foreach(var cover in (JArray)snapshot["covers"])if((int)cover["health"]<=0&&sprites.TryGetValue("covers:"+cover["id"],out var box))box.enabled=false;
   foreach(var f in (JArray)snapshot["fields"]){if((int)f["room_id"]!=room)continue;var owner=(string)f["owner"];if(!sprites.TryGetValue("fields:"+f["id"],out var circle))continue;if(owner==ownID)circle.transform.position=Position+RoomOffset;else if(sprites.TryGetValue("player:"+owner,out var actor))circle.transform.position=actor.transform.position;}
   // Only the current radial blade/head band is bright. Old decorative trails grant no protection.
   foreach(var p in (JArray)snapshot["players"]){if((int)p["room_id"]!=room)continue;var id="v6:sweep:"+p["player_id"];var a=p["melee_action"] as JObject;var active=a!=null&&(string)a["phase"]=="active"&&V6Game<(double)a["phase_end_ms"];if(!active){if(sprites.TryGetValue(id,out var old))old.enabled=false;continue;}
    var center=(string)p["player_id"]==ownID?Position:sprites.TryGetValue("player:"+p["player_id"],out var actor)?(Vector2)actor.transform.position-RoomOffset:Point(p);var dir=new Vector2((float)a["aim_x"],(float)a["aim_y"]);var length=(bool)a["heavy"]?1.5f:(float)a["range"];var q=Mathf.Clamp01((float)((V6Game-(double)a["phase_started_ms"])/Math.Max(1,(double)a["phase_end_ms"]-(double)a["phase_started_ms"])));var angle=Mathf.Atan2(dir.y,dir.x)*Mathf.Rad2Deg+((bool)a["heavy"]?0:Mathf.Lerp((float)a["sweep_start"],(float)a["sweep_end"],q));var radial=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));var line=Draw(id,center+radial*length/2,(string)p["loadout_id"]=="ranger"?new Color(0.65f,0.94f,0.8f,0.85f):new Color(1,0.74f,0.34f,0.85f),new Vector2(length,0.06f),11);line.enabled=true;line.transform.rotation=Quaternion.Euler(0,0,angle);
   }
   if(snapshot["v6_contacts"] is JArray contacts)foreach(var e in contacts){var id=(string)e["event_id"];if(!v6ContactSeen.Add(id))continue;v6ContactOrder.Enqueue(id);while(v6ContactOrder.Count>512)v6ContactSeen.Remove(v6ContactOrder.Dequeue());if((int)e["room_id"]!=room)continue;var state=e["state"];var point=Point(state);if(Visible(point)){if(e["confirmed_from_x"]!=null)V6ConfirmedTrace(new Vector2((float)e["confirmed_from_x"],(float)e["confirmed_from_y"]),point);var fx=new JObject{["kind"]="light",["x"]=point.x,["y"]=point.y,["visual_radius"]=0.18f,["visual_aim_x"]=(float)state["direction_x"],["visual_aim_y"]=(float)state["direction_y"]};P5AttackFx(fx);}}
  }
 }
}
