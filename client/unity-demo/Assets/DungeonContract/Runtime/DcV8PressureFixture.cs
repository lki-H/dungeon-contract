using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 // Explicit modified-state fixtures only. Never called by an admitted normal Run.
 public static class DcV8PressureFixture {
  public const string Version="v8_staged_towers_spin_battle_1";
  public static void Authority(JObject state,string kit){
   foreach(var actor in state["players"]){
    if(kit!="runesmith")continue;
    actor["BattleUntil"]=3000;
    actor["Upgrades"]=new JArray("runesmith_twin","battle_rune","runesmith_chain");
    actor["TurretCharges"]=0;actor["TurretGroup"]="pressure_group_"+actor["ID"];actor["AbilityAt"]=12000;
    actor["SpinRoot"]="pressure_spin_"+actor["ID"];actor["SpinRoom"]=actor["Room"].DeepClone();actor["SpinStart"]=0;actor["SpinUntil"]=2000;actor["SpinPulse"]=0;actor["SpinDamage"]=18;actor["SpinChain"]=true;actor["SpinFrost"]=false;actor["ReleaseRequired"]=true;
   }
   foreach(var tower in state["turrets"])tower["Group"]="pressure_group_"+tower["Owner"];
  }
  public static void Render(JObject snapshot,JObject parameters,long now,string scenario){
   var fields=new JArray();var turrets=new JArray();var kits=((JArray)parameters["loadouts"]).ToDictionary(v=>(string)v["id"],v=>v);
   foreach(var actor in snapshot["players"]){
    var id=(string)actor["player_id"];var slot=(int)actor["slot"];var kit=(string)actor["loadout_id"];var body=DcV7Geometry.Profile(parameters,kit,"down");
    actor["body_profile_id"]=body.ID;actor["body_facing"]=body.Facing;actor["body_center_x"]=(float)actor["x"]+body.Center.x;actor["body_center_y"]=(float)actor["y"]+body.Center.y;
    var secondary=kits[kit]["secondary"];var cooldown=(long)secondary["cooldown_ms"];var duration=(long)secondary["duration_ms"];var skillPhase=now%cooldown;var skillStart=now-skillPhase;actor["battle_until_ms"]=kit=="runesmith"?skillStart+3000:0;actor["death_hammer"]=JValue.CreateNull();
    if(kit=="ranger"){
     if(skillPhase<duration)fields.Add(new JObject{["id"]="render_wind_"+id,["owner"]=id,["owner_life"]=1,["cast_id"]="wind_"+skillStart,["room_id"]=1,["kind"]="wind_bind",["radius"]=(float)secondary["radius_milli_tiles"]/1000,["x"]=actor["body_center_x"].DeepClone(),["y"]=actor["body_center_y"].DeepClone(),["ends_ms"]=skillStart+duration});
     continue;
    }
    actor["upgrades"]=new JArray("runesmith_twin","battle_rune","runesmith_chain");
    actor["secondary_remaining_ms"]=16000-skillPhase;
    if(skillPhase<2000)actor["death_hammer"]=new JObject{["root"]="render_spin_"+id+"_"+skillStart,["active"]=true,["room_id"]=1,["started_ms"]=skillStart,["ends_ms"]=skillStart+2000,["next_pulse"]=(int)(skillPhase/250)+1,["damage"]=18,["frost"]=false,["chain"]=true,["end_reason"]=null};
    var phase=now%14000;var start=now-phase;var group="render_group_"+id+"_"+start;
    actor["turret_group"]=group;actor["turret_charges"]=phase<2000?1:0;actor["ability_remaining_ms"]=phase<2000?0:14000-phase;
    for(var i=0;i<2;i++){var born=start+i*2000;if(now<born||now>=born+8000)continue;turrets.Add(new JObject{["id"]=group+"_"+i,["room_id"]=1,["owner"]=id,["owner_life"]=1,["deployment_group"]=group,["born_game_ms"]=born,["expires_game_ms"]=born+8000,["x"]=8+slot*3+i,["y"]=6});}
    if(skillPhase>=2000){var at=now-now%3000;actor["last_slam"]=new JObject{["root"]="render_slam_"+id+"_"+at,["room_id"]=1,["at_ms"]=at,["x"]=(float)actor["body_center_x"]+1.2f,["y"]=actor["body_center_y"].DeepClone(),["radius"]=2.875f,["aim_x"]=1,["aim_y"]=0,["head_profile_id"]="rune_hammer_head.v8"};}
   }
   // The all-craft case exercises the eight-tower peak and the staged 1/0 charge UI.
   snapshot["turrets"]=turrets;snapshot["fields"]=fields;
   var index=0;foreach(var shot in snapshot["projectiles"]){
    var actor=snapshot["players"][index%4];var kit=(string)actor["loadout_id"];var ranged=kits[kit]["ranged"];
    shot["id"]="render_projectile_"+index+"_"+(now/6000);shot["owner"]=actor["player_id"].DeepClone();
    if(!(bool)shot["enemy"]){shot["radius"]=(float)ranged["projectile_radius_milli_tiles"]/1000;shot["speed"]=(float)ranged["projectile_speed_milli_tiles_per_second"]/1000;shot["rune_bolt"]=kit=="runesmith";shot["wind_volley"]=kit=="ranger"&&index%3==0;}
    index++;
   }
  }
 }
}
