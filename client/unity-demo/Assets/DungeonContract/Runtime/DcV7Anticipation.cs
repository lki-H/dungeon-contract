using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 public sealed class DcV7PreviewLedger {
  public sealed class Item {public string Episode;public uint Emission;public double At;public string[] Visuals;}
  private readonly List<Item> values=new List<Item>();
  public int Count=>values.Count;
  public bool Add(Item item){if(values.Count>=64||values.Any(v=>v.Episode==item.Episode&&v.Emission==item.Emission))return false;values.Add(item);return true;}
  public Item Take(string episode,uint emission){var i=values.FindIndex(v=>v.Episode==episode&&v.Emission==emission);if(i<0)return null;var item=values[i];values.RemoveAt(i);return item;}
  public Item[] Remove(Func<Item,bool> predicate){var removed=values.Where(predicate).ToArray();values.RemoveAll(v=>predicate(v));return removed;}
  public Item[] Clear()=>Remove(_=>true);
 }
 public sealed partial class DcP1View {
  private sealed class V7PreviewFlight {public string Key;public Vector2 Position,Direction;public float Speed,Radius,Started;public SpriteRenderer Renderer;}
  private readonly DcV7PreviewLedger v7PreviewLedger=new DcV7PreviewLedger();
  private readonly List<V7PreviewFlight> v7PreviewFlights=new List<V7PreviewFlight>();
  private readonly HashSet<string> v7ConfirmedBirths=new HashSet<string>();private readonly Queue<string> v7ConfirmedBirthOrder=new Queue<string>();
  private readonly Queue<(SpriteRenderer renderer,Vector2 offset,float until)> v7MergeOffsets=new Queue<(SpriteRenderer,Vector2,float)>();
  private string v7PreviewEpisode;private uint v7PreviewEmission;private float v7PreviewNext;private float v7PreviewMeleeUntil;private float v7PreviewMeleeStart;
  private JToken V7PreviewKit(JToken own)=>geometry["parameters"]["loadouts"].First(k=>(string)k["id"]==(string)own["loadout_id"]);
  private void V7PreviewMeleeTiming(JToken own,out float wind,out float active,out float duration,out float arc){
   var upgrades=(JArray)own["upgrades"];var definition=V7PreviewKit(own)["melee"];
   var phases=DcP1World.MeleePhaseTicks(definition,upgrades.Any(u=>(string)u=="ranger_tension"),upgrades.Any(u=>(string)u=="runesmith_twin"));
   wind=phases[0]/30f;active=phases[1]/30f;duration=phases.Sum()/30f;arc=(int)definition["arc_deg"]+(upgrades.Any(u=>(string)u=="ranger_fan")?30:0);
  }
  public void V7ClearPreviews(){foreach(var item in v7PreviewLedger.Clear())V7RemovePreview(item);v7PreviewEpisode=null;v7PreviewMeleeUntil=0;v7MergeOffsets.Clear();}
  private void V7RemovePreview(DcV7PreviewLedger.Item item){foreach(var id in item.Visuals??Array.Empty<string>()){var f=v7PreviewFlights.FirstOrDefault(f=>f.Key==id);if(f!=null){f.Renderer.enabled=false;f.Renderer.gameObject.SetActive(false);sprites.Remove(id);spritePool.Enqueue(f.Renderer);v7PreviewFlights.Remove(f);}}}
  public void V7ReceiptPreview(string action,bool rejected){if(!V7View)return;if(rejected){foreach(var item in v7PreviewLedger.Remove(x=>x.Episode==action))V7RemovePreview(item);if(v7PreviewEpisode==action)v7PreviewEpisode=null;v7PreviewMeleeUntil=0;}}
  public void V7ActionPreview(string kind,string actionID,DcP1Input input){
   if(!V7View||snapshot==null)return;var own=snapshot["players"].First(p=>(string)p["player_id"]==ownID);
   if(V8View&&(bool?)(own["death_hammer"] as JObject)?["active"]==true&&(kind=="attack_press"||kind=="charge_begin")){V7ClearPreviews();return;}
   if(kind=="cancel_input"||kind=="switch_weapon"||kind=="attack_cancel"||kind=="charge_begin"){V7ClearPreviews();return;}
   if(kind=="attack_release"){v7PreviewEpisode=null;return;}
   if(kind=="attack_press"){v7PreviewEpisode=actionID;v7PreviewEmission=(uint?)own["emission_index"]??0;v7PreviewNext=ViewTime;if((bool)own["melee_equipped"]&&(long)own["melee_ready_ms"]<=V6Game){V7PreviewMeleeTiming(own,out _,out _,out var duration,out _);v7PreviewMeleeStart=ViewTime;v7PreviewMeleeUntil=ViewTime+duration;}else if(!(bool)own["melee_equipped"]&&V6Game>=(long)own["ranged_ready_ms"]&&(long)own["reload_remaining_ms"]==0&&((int)own["magazine_max"]==0||(int)own["magazine"]>0)){V7SpawnPreview(own,actionID,++v7PreviewEmission,input.Aim,false);v7PreviewNext=ViewTime+(int)V7PreviewKit(own)["ranged"]["interval_ms"]/1000f;}}
   if(kind=="ability_primary"&&(string)own["loadout_id"]=="ranger"&&(long)own["ability_remaining_ms"]==0){var emission=++v7PreviewEmission;V7SpawnPreview(own,actionID,emission,input.Aim,true);}
  }
  public void V7HeldPreview(DcP1Input input){
   if(!V7View||snapshot==null||v7PreviewEpisode==null||!input.Attack&&!input.Melee)return;var own=snapshot["players"].First(p=>(string)p["player_id"]==ownID);
   if((int)own["health"]<=0||p4Open||V5MenuVisible||V8View&&(bool?)(own["death_hammer"] as JObject)?["active"]==true){V7ClearPreviews();return;}
   if((bool)own["melee_equipped"]){if(V6Game>=(long)own["melee_ready_ms"]&&ViewTime>=v7PreviewMeleeUntil&&ViewTime>=v7PreviewNext){V7PreviewMeleeTiming(own,out _,out _,out var duration,out _);v7PreviewMeleeStart=ViewTime;v7PreviewMeleeUntil=ViewTime+duration;v7PreviewNext=v7PreviewMeleeUntil;}return;}
   if(ViewTime<v7PreviewNext||V6Game<(long)own["ranged_ready_ms"]||(long)own["reload_remaining_ms"]>0||(int)own["magazine_max"]>0&&(int)own["magazine"]<=0)return;
   v7PreviewEmission=Math.Max(v7PreviewEmission,(uint?)own["emission_index"]??0)+1;V7SpawnPreview(own,v7PreviewEpisode,v7PreviewEmission,input.Aim,false);v7PreviewNext=ViewTime+(int)V7PreviewKit(own)["ranged"]["interval_ms"]/1000f;
  }
  private void V7SpawnPreview(JToken own,string episode,uint emission,ushort aim,bool volley){
   var ranger=(string)own["loadout_id"]=="ranger";var angles=volley?new[]{-15f,0f,15f}:new[]{0f};var fan=((JArray)own["upgrades"]).Any(v=>(string)v=="ranger_fan");var offsets=fan?new[]{-12f,0f,12f}:new[]{0f};var keys=new List<string>();var flies=new List<V7PreviewFlight>();
   foreach(var angle in angles)foreach(var offset in offsets){var degrees=aim*360f/65536+angle+offset;var direction=new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad));var center=AimOrigin;var radius=ranger?.1f:.3f;var layout=(JObject)geometry["rooms"][room];var end=center+direction*.9f;
    var hit=DcV6Geometry.Terrain(layout,center,end,radius,true);foreach(var c in ((JArray)snapshot["covers"]).Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0)){var contact=DcV6Geometry.Box(center,end,(float)c["x"]-.4375f,(float)c["y"]-.4375f,.875f,.875f,radius,true);if(contact.HasValue&&(!hit.HasValue||contact.Value.Time<hit.Value.Time))hit=contact;}
    var key="weapon:v7preview:"+episode+":"+emission+":"+keys.Count;keys.Add(key);var at=hit.HasValue?Vector2.Lerp(center,end,Math.Max(0,hit.Value.Time-.0001f)):end;var renderer=Draw(key,at,ranger?new Color(.8f,.8f,.5f,.8f):new Color(.4f,.85f,.75f,.8f),Vector2.one*radius*2,10);renderer.sprite=ranger?square:disk;flies.Add(new V7PreviewFlight{Key=key,Position=at,Direction=direction,Speed=hit.HasValue?0:ranger?20:10,Radius=radius,Started=ViewTime,Renderer=renderer});
   }
   if(!v7PreviewLedger.Add(new DcV7PreviewLedger.Item{Episode=episode,Emission=emission,At=ViewTime,Visuals=keys.ToArray()})){foreach(var f in flies){f.Renderer.enabled=false;f.Renderer.gameObject.SetActive(false);sprites.Remove(f.Key);spritePool.Enqueue(f.Renderer);}return;}v7PreviewFlights.AddRange(flies);
  }
  private void UpdateV7Previews(){
   foreach(var expired in v7PreviewLedger.Remove(x=>ViewTime-x.At>=1))V7RemovePreview(expired);
   if(snapshot["v6_contacts"] is JArray contacts)foreach(var e in contacts){var state=e["state"];if(state==null||(string)state["owner"]!=ownID)continue;var item=v7PreviewLedger.Take((string)state["attack_episode_id"],(uint?)state["emission_index"]??0);if(item!=null)V7RemovePreview(item);}
   foreach(var p in ((JArray)snapshot["projectiles"]).Where(p=>(string)p["owner"]==ownID)){
    var id=(string)p["id"];if(!v7ConfirmedBirths.Add(id))continue;v7ConfirmedBirthOrder.Enqueue(id);while(v7ConfirmedBirthOrder.Count>4096)v7ConfirmedBirths.Remove(v7ConfirmedBirthOrder.Dequeue());var episode=(string)p["attack_episode_id"];var emission=(uint?)p["emission_index"]??0;var item=v7PreviewLedger.Take(episode,emission);if(item==null)continue;var preview=v7PreviewFlights.FirstOrDefault(f=>item.Visuals.Contains(f.Key));if(preview!=null&&sprites.TryGetValue("projectiles:"+id,out var renderer)){var offset=preview.Position-Point(p);if(offset.magnitude<=1.5f)v7MergeOffsets.Enqueue((renderer,offset,ViewTime+.08f));}V7RemovePreview(item);
   }
   foreach(var f in v7PreviewFlights){if(f.Speed>0){var end=f.Position+f.Direction*f.Speed*Math.Min(Time.unscaledDeltaTime,.05f);var h=DcV6Geometry.Terrain((JObject)geometry["rooms"][room],f.Position,end,f.Radius,true);foreach(var c in ((JArray)snapshot["covers"]).Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0)){var hit=DcV6Geometry.Box(f.Position,end,(float)c["x"]-.4375f,(float)c["y"]-.4375f,.875f,.875f,f.Radius,true);if(hit.HasValue&&(!h.HasValue||hit.Value.Time<h.Value.Time))h=hit;}if(h.HasValue){f.Position=Vector2.Lerp(f.Position,end,Math.Max(0,h.Value.Time-.0001f));f.Speed=0;}else f.Position=end;}f.Renderer.transform.position=f.Position+RoomOffset;}
   var count=v7MergeOffsets.Count;for(var i=0;i<count;i++){var merge=v7MergeOffsets.Dequeue();if(merge.until<=ViewTime||merge.renderer==null||!merge.renderer.enabled)continue;merge.renderer.transform.position+=(Vector3)(merge.offset*Mathf.Clamp01((merge.until-ViewTime)/.08f));v7MergeOffsets.Enqueue(merge);}
  }
 }
}
