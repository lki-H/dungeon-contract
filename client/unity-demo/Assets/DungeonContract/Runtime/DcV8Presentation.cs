using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 // Decode current solid covers once for the reserved visual channels. Terrain
 // and cover contacts still use the exact existing geometry routines.
 public sealed class DcV8RingOcclusion {
  private JObject layout;private int room=-1;
  private readonly List<Vector4> covers=new List<Vector4>();
  public ulong Revision {get;private set;}
  public void Prepare(JObject next,JArray state,int roomID){
   var changed=!ReferenceEquals(layout,next)||room!=roomID;layout=next;room=roomID;var count=0;
   foreach(var cover in state){
    if((int)cover["room_id"]!=roomID||(int)cover["health"]<=0)continue;
    var rect=new Vector4((float)cover["x"]-.4375f,(float)cover["y"]-.4375f,.875f,.875f);
    if(count==covers.Count){covers.Add(rect);changed=true;}else if(!covers[count].Equals(rect)){covers[count]=rect;changed=true;}
    count++;
   }
   if(count<covers.Count){covers.RemoveRange(count,covers.Count-count);changed=true;}
   if(changed)Revision++;
  }
  public float VisibleRadius(Vector2 at,Vector2 direction,float radius){
   var end=at+direction*radius;var hit=DcV6Geometry.Terrain(layout,at,end,0);var t=hit?.Time??1;
   foreach(var rect in covers){var contact=DcV6Geometry.Box(at,end,rect.x,rect.y,rect.z,rect.w,0);if(contact.HasValue)t=Math.Min(t,contact.Value.Time);}
   return radius*Math.Max(0,t-.001f);
  }
 }
 public sealed partial class DcP1View {
  private sealed class V8CriticalVisual {
   public Mesh Mesh;public GameObject Root;public string Seen;public float Started,Until,Radius;public Vector2 Point;
   public bool ClipValid;public Vector2 ClipPoint;public float ClipRadius;public ulong ClipRevision;public readonly float[] Reaches=new float[65];
   public readonly Vector3[] Vertices=new Vector3[130];public readonly Color[] Colors=new Color[130];
  }
  private readonly Dictionary<string,V8CriticalVisual> v8Waves=new Dictionary<string,V8CriticalVisual>(),v8Spins=new Dictionary<string,V8CriticalVisual>();
  private readonly DcV8RingOcclusion v8RingOcclusion=new DcV8RingOcclusion();
  private static readonly Vector2[] v8RingDirections=Enumerable.Range(0,65).Select(i=>new Vector2(Mathf.Cos(i*Mathf.PI*2/64),Mathf.Sin(i*Mathf.PI*2/64))).ToArray();
  private int v8VisualRoom=-1;
  private bool v8RestoringVisuals;
  public void RestoreV8Presentation(){v8RestoringVisuals=true;}
  private bool V8View=>(bool?)snapshot?["v8_stage"]==true;
  private V8CriticalVisual V8Visual(Dictionary<string,V8CriticalVisual> pool,string id){
   if(pool.TryGetValue(id,out var item))return item;
   if(pool.Count>=4)return null;
   var go=new GameObject("V8 reserved "+id);go.transform.SetParent(root.transform);var mesh=new Mesh();mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=sprites["weapon:own"].sharedMaterial;renderer.sortingOrder=5;
   var triangles=new int[64*6];for(var i=0;i<64;i++){var n=i*2;var t=i*6;triangles[t]=n;triangles[t+1]=n+1;triangles[t+2]=n+3;triangles[t+3]=n;triangles[t+4]=n+3;triangles[t+5]=n+2;}
   item=new V8CriticalVisual{Root=go,Mesh=mesh};mesh.vertices=item.Vertices;mesh.colors=item.Colors;mesh.triangles=triangles;pool[id]=item;return item;
  }
  private void V8Ring(V8CriticalVisual item,Vector2 at,float radius,float alpha,float rotation,bool wind){
   item.Root.SetActive(true);item.Root.transform.position=at+RoomOffset;
   var refresh=!item.ClipValid||!item.ClipPoint.Equals(at)||item.ClipRadius!=radius||item.ClipRevision!=v8RingOcclusion.Revision;
   for(var i=0;i<=64;i++){
    var angle=i*Mathf.PI*2/64;var direction=v8RingDirections[i];if(refresh)item.Reaches[i]=v8RingOcclusion.VisibleRadius(at,direction,radius);var reach=item.Reaches[i];
    item.Vertices[i*2]=direction*reach*(wind?.35f:.88f);item.Vertices[i*2+1]=direction*reach;
    var energy=wind?(DcP5Settings.Current.ReducedFlash?.65f:.35f+.65f*Mathf.Pow(Mathf.Max(0,Mathf.Cos(angle-rotation)),4)):1;
    item.Colors[i*2]=new Color(1,.23f,.18f,alpha*energy*.2f);item.Colors[i*2+1]=new Color(1,.38f,.24f,alpha*energy);
   }
   item.ClipValid=true;item.ClipPoint=at;item.ClipRadius=radius;item.ClipRevision=v8RingOcclusion.Revision;
   item.Mesh.vertices=item.Vertices;item.Mesh.colors=item.Colors;item.Mesh.RecalculateBounds();
  }
  private void DrawV8Critical(){
   if(!V8View)return;
   v8RingOcclusion.Prepare((JObject)geometry["rooms"][room],(JArray)snapshot["covers"],room);
   // Recovery primes completed actions from the new baseline. Live skills still
   // resume from their authoritative remaining duration below.
   var reset=v8RestoringVisuals||v8VisualRoom!=room;v8RestoringVisuals=false;v8VisualRoom=room;var present=new HashSet<string>();
   foreach(var p in ((JArray)snapshot["players"]).Where(p=>(int)p["room_id"]==room)){
    var id=(string)p["player_id"];present.Add(id);
    var wave=V8Visual(v8Waves,id);var spin=V8Visual(v8Spins,id);if(wave==null||spin==null)continue;
    var slam=p["last_slam"] as JObject;var key=(string)slam?["root"];
    if(reset){wave.Seen=key;wave.Until=0;}
    if(key!=null&&key!=wave.Seen){
     wave.Seen=key;
     if((int)slam["room_id"]==room&&V6Game-(long)slam["at_ms"]<=1000){wave.Point=new Vector2((float)slam["x"],(float)slam["y"]);wave.Radius=(float)slam["radius"];wave.Started=ViewTime;wave.Until=ViewTime+.3f;}
    }
    wave.Root.SetActive(ViewTime<wave.Until);
    if(ViewTime<wave.Until){var q=Mathf.Clamp01((ViewTime-wave.Started)/.3f);V8Ring(wave,wave.Point,wave.Radius*Mathf.Lerp(.2f,1,q),(.7f-q*.5f),0,false);}
    var state=p["death_hammer"] as JObject;var active=(bool?)state?["active"]==true&&V6Game<(long)state["ends_ms"];
    spin.Root.SetActive(active);
    if(active){var ground=id==ownID?Position:sprites.TryGetValue("player:"+id,out var actor)?(Vector2)actor.transform.position-RoomOffset:Point(p);var radius=(float)geometry["parameters"]["loadouts"][1]["secondary"]["radius_milli_tiles"]/1000;V8Ring(spin,V7Center(p,ground),radius,.65f,(float)((V6Game-(long)state["started_ms"])/1000d*Math.PI*4),true);}
   }
   foreach(var item in v8Waves)if(!present.Contains(item.Key))item.Value.Root.SetActive(false);
   foreach(var item in v8Spins)if(!present.Contains(item.Key))item.Value.Root.SetActive(false);
  }
  private void DrawV8Hud(JToken own){
   if(!V8View)return;
   if((string)own["loadout_id"]=="runesmith"){
    var cooldown=(long)own["ability_remaining_ms"];var charges=(int?)own["turret_charges"]??0;
    v5CooldownLabels[1].text=(p5Controls?.Display("Primary")??"E")+" · "+(cooldown>0?Math.Ceiling(cooldown/1000d)+"s":charges+"/"+(((JArray)own["upgrades"]).Any(x=>(string)x=="runesmith_twin")?2:1));
    if(own["death_hammer"] is JObject spin&&(bool)spin["active"]&&V6Game<(long)spin["ends_ms"]){v5CooldownLabels[2].text=(p5Controls?.Display("Secondary")??"F")+" · 旋转"+(((long)spin["ends_ms"]-V6Game)/1000d).ToString("0.0")+"s";v5Cooldowns[2].fillAmount=Mathf.Clamp01((float)(((long)spin["ends_ms"]-V6Game)/Math.Max(1,(long)spin["ends_ms"]-(long)spin["started_ms"])));v5Cooldowns[2].color=new Color(.45f,.08f,.05f,.7f);}
    UiRoot.Find("Cooldown base 2").GetComponent<UnityEngine.UI.Image>().sprite=DcP5Art.Get("icon.rune_hammer");
   }
   if((long?)own["battle_until_ms"]>V6Game)resources.text+=" · 战意+15%";
  }
  private void CleanupV8Critical(){foreach(var item in v8Waves.Values.Concat(v8Spins.Values)){Destroy(item.Mesh);Destroy(item.Root);}v8Waves.Clear();v8Spins.Clear();}
 }
}
