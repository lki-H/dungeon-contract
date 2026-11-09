using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 // Runtime geometry reuses the original atlas; no source texture or importer is changed.
 public static class DcV7Sprites {
  private static readonly Dictionary<string,Sprite> cache=new Dictionary<string,Sprite>();
  private static Color32[] atlasPixels;private static Texture2D atlasSource;
  private static Color32[] Crop(Sprite source){
   var output=new Color32[32*32];
   if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return output;
   if(atlasSource!=source.texture){
    var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(source.texture.width,source.texture.height,0,RenderTextureFormat.ARGB32);Texture2D readable=null;
    try{Graphics.Blit(source.texture,target);RenderTexture.active=target;readable=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);readable.ReadPixels(new Rect(0,0,target.width,target.height),0,0);readable.Apply();atlasPixels=readable.GetPixels32();atlasSource=source.texture;}finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(readable!=null)UnityEngine.Object.Destroy(readable);}
   }
   var rect=source.rect;for(var y=0;y<32;y++)Array.Copy(atlasPixels,((int)rect.y+y)*atlasSource.width+(int)rect.x,output,y*32,32);return output;
  }
  public static Sprite Mask(Sprite source,string key,Func<int,int,bool> allowed,Vector2? customPivot=null){
   if(cache.TryGetValue(key,out var found))return found;var pivot=customPivot??new Vector2(source.pivot.x/32,source.pivot.y/32);var pixels=Crop(source);
   for(var y=0;y<32;y++)for(var x=0;x<32;x++)if(!allowed(x,31-y))pixels[y*32+x]=Color.clear;
   var texture=new Texture2D(32,32,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,name=key};texture.SetPixels32(pixels);texture.Apply();var sprite=Sprite.Create(texture,new Rect(0,0,32,32),pivot,16,0,SpriteMeshType.FullRect);sprite.name=key;cache[key]=sprite;return sprite;
  }
  public static Sprite Body(Sprite source,DcV7Geometry.Body body,string state,int frame){
   var mirrored=body.Facing=="left";var broad=body.Kind=="runesmith";var handX=broad?23:21;var handY=17-(state=="prepare"||state=="charge"?frame*2:0);var words=new uint[32];for(var y=0;y<32;y++)words[y]=Convert.ToUInt32(body.Mask.Substring(y*8,8),16);
   return Mask(source,"v7body:"+source.name+":"+body.ID,(x,y)=>{var rx=mirrored?31-x:x;return (words[y]&(1u<<x))!=0&&!(rx>=handX&&rx<=handX+3&&y>=handY&&y<=handY+3);});
  }
  public static Sprite WeaponHead(string id,out float nativeAngle,out float nativeDepth,out float nativeWidth){
   var hammer=id=="rune_hammer";var source=DcP5Art.Weapon(id);var grip=hammer?new Vector2(10,3):new Vector2(9,7);var axis=hammer?new Vector2(10,18).normalized:new Vector2(16,21).normalized;nativeAngle=Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg;
   var points=hammer?new[]{new Vector2(11,28),new Vector2(24,27),new Vector2(29,20),new Vector2(21,14),new Vector2(8,19)}:new[]{new Vector2(9,9),new Vector2(13,16),new Vector2(25,28),new Vector2(23,20),new Vector2(15,12)};
   bool Allowed(int x,int y)=>hammer?x>=7&&x<=30&&y>=3&&y<=19:x>=8&&x<=26&&y>=3&&y<=23;
   // Measure the actual retained alpha edges. The transform origin must be the
   // visible tip, not an approximate point on the icon or transparent padding.
   if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){var pixels=Crop(source);var visible=new List<Vector2>();for(var y=0;y<32;y++)for(var x=0;x<32;x++)if(pixels[y*32+x].a>0&&Allowed(x,31-y)){visible.Add(new Vector2(x,y));visible.Add(new Vector2(x+1,y));visible.Add(new Vector2(x,y+1));visible.Add(new Vector2(x+1,y+1));}if(visible.Count==0)throw new InvalidOperationException("Retained weapon slice is transparent: "+id);points=visible.ToArray();}
   var side=new Vector2(-axis.y,axis.x);var min=points.Min(p=>Vector2.Dot(p-grip,axis));var max=points.Max(p=>Vector2.Dot(p-grip,axis));var low=points.Min(p=>Vector2.Dot(p-grip,side));var high=points.Max(p=>Vector2.Dot(p-grip,side));nativeDepth=(max-min)/16;nativeWidth=(high-low)/16;var tip=grip+axis*max+side*(low+high)*.5f;var pivot=tip/32;
   return Mask(source,"v7head:"+id,Allowed,pivot);
  }
  public static Sprite Cover(){const string key="v7cover";if(cache.TryGetValue(key,out var value))return value;var source=DcP5Art.Get("terrain.cover.0");value=Sprite.Create(source.texture,new Rect(source.rect.x+3,source.rect.y+2,28,28),new Vector2(.5f,.5f),16);cache[key]=value;return value;}
  public static Sprite Ranged(string id,Vector2 tip){var key="v7ranged:"+id;if(cache.TryGetValue(key,out var value))return value;var source=DcP5Art.Weapon(id);value=Sprite.Create(source.texture,source.rect,new Vector2(tip.x/32,1-tip.y/32),16);cache[key]=value;return value;}
 }
 public sealed partial class DcP1View {
  private sealed class V7WeaponVisual {public GameObject Root;public Transform Axis;public SpriteRenderer Head;public string Weapon;public float NativeDepth,NativeWidth,NextTrailAt;public int TrailIndex;public readonly Vector2[] TrailPoints=new Vector2[3];public readonly float[] TrailAngles=new float[3],TrailUntil=new float[3];}
  private readonly Dictionary<string,V7WeaponVisual> v7Weapons=new Dictionary<string,V7WeaponVisual>();
  private string v7OwnFacing;
  private bool V7View=>(bool?)snapshot?["v7_stage"]==true;
  public Vector2 AimOrigin=>V7View&&snapshot!=null?V7Center(snapshot["players"].First(p=>(string)p["player_id"]==ownID),Position):Position;
  public void VisualBodyFacing(string facing){v7OwnFacing=facing;}
  private DcV7Geometry.Body V7Body(JToken actor){var player=actor["player_id"]!=null;var facing=player&&(string)actor["player_id"]==ownID?v7OwnFacing??(string)actor["body_facing"]:(string)actor["body_facing"];return DcV7Geometry.Profile((JObject)geometry["parameters"],(string)actor[player?"loadout_id":"kind"],facing??"down");}
  private Vector2 V7Center(JToken actor,Vector2 ground)=>ground+V7Body(actor).Center;
  private Sprite V7ActorSprite(JToken actor,Vector2 direction,string state,int pose){var kind=(string)(actor["loadout_id"]??actor["kind"]);var body=V7Body(actor);var vector=body.Facing=="left"?Vector2.left:body.Facing=="right"?Vector2.right:body.Facing=="up"?Vector2.up:Vector2.down;var source=DcP5Art.Actor(kind,vector,state,pose);return actor["player_id"]!=null&&state!="death"?DcV7Sprites.Body(source,body,state,pose):source;}
  private V7WeaponVisual V7Weapon(string player,string weapon){
   if(!v7Weapons.TryGetValue(player,out var model)){
    var holder=new GameObject("V7 weapon "+player);holder.transform.SetParent(root.transform);var axis=new GameObject("Weapon axis").transform;axis.SetParent(holder.transform,false);var head=new GameObject("Reused atlas head").AddComponent<SpriteRenderer>();head.transform.SetParent(axis,false);head.sharedMaterial=sprites["weapon:own"].sharedMaterial;model=new V7WeaponVisual{Root=holder,Axis=axis,Head=head};v7Weapons[player]=model;
   }
   if(model.Weapon!=weapon){model.Weapon=weapon;Array.Clear(model.TrailUntil,0,model.TrailUntil.Length);model.NextTrailAt=0;var melee=weapon=="ruin_blade"||weapon=="rune_hammer";if(melee){model.Head.sprite=DcV7Sprites.WeaponHead(weapon,out var angle,out model.NativeDepth,out model.NativeWidth);model.Head.transform.localRotation=Quaternion.Euler(0,0,-angle);}else{var grip=weapon=="repeating_crossbow"?new Vector2(12,24):new Vector2(16,20);var tip=weapon=="repeating_crossbow"?new Vector2(23,4):new Vector2(16,3);var vector=new Vector2(tip.x-grip.x,grip.y-tip.y);var angle=Mathf.Atan2(vector.y,vector.x)*Mathf.Rad2Deg;model.Head.sprite=DcV7Sprites.Ranged(weapon,tip);model.NativeDepth=vector.magnitude/16;model.NativeWidth=.6f;model.Head.transform.localRotation=Quaternion.Euler(0,0,-angle);}}
   return model;
  }
  private void DrawV7Weapons(){
   if(sprites.TryGetValue("weapon:own",out var old))old.enabled=false;var present=new HashSet<string>();
   foreach(var actor in ((JArray)snapshot["players"]).Where(a=>(int)a["room_id"]==room&&(int)a["health"]>0)){
    var id=(string)actor["player_id"];present.Add(id);var own=id==ownID;var ground=own?Position:sprites.TryGetValue("player:"+id,out var renderer)?(Vector2)renderer.transform.position-RoomOffset:Point(actor);var center=V7Center(actor,ground);var attack=actor["melee_action"] as JObject;var direction=own?PointerWorld(p5Controls?.Pointer??Vector2.zero)-center:new Vector2((float?)actor["aim_x"]??1,(float?)actor["aim_y"]??0);if(direction.sqrMagnitude<.0001f)direction=Vector2.right;direction.Normalize();var heading=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;var spin=V8View&&(bool?)(actor["death_hammer"] as JObject)?["active"]==true&&V6Game<(long)actor["death_hammer"]["ends_ms"];var weapon=spin?"rune_hammer":(string)actor["active_weapon_id"];var blade=weapon=="ruin_blade";var hammer=weapon=="rune_hammer";var melee=blade||hammer;var model=V7Weapon(id,weapon);model.Root.SetActive(true);var bladeRange=(float)(Math.Round((4000+(((JArray)actor["upgrades"]).Any(x=>(string)x=="ranger_pierce")?700:0))/3d,MidpointRounding.AwayFromZero)/1000);var hammerDef=geometry["parameters"]["loadouts"][1]["melee"];var hammerRange=V8View?(float)hammerDef["range_milli_tiles"]/1000:3.25f;var landingRange=V8View?(float)hammerDef["charge"]["landing_distance_milli_tiles"]/1000:1.875f;var radius=melee?(blade?bladeRange:hammerRange):.9f;var angle=heading;var active=false;var heavy=false;
    if(spin){active=true;angle=heading+(float)((V6Game-(long)actor["death_hammer"]["started_ms"])*.36*(float)geometry["parameters"]["v8_presentation"]["spin_revolutions_per_second"]);}else if(attack!=null){heavy=(bool)attack["heavy"];var start=(double)attack["attack_started_ms"];var tick=(long)attack["started_tick"];var activeAt=start+((long)attack["active_tick"]-tick)*1000d/30;var recoverAt=start+((long)attack["recovery_tick"]-tick)*1000d/30;var ends=start+((long)attack["end_tick"]-tick)*1000d/30;var t=V6Game;active=t>=activeAt&&t<recoverAt;if(!heavy){radius=(float)attack["range"];var arc=(float)attack["arc"];angle=heading+(t<activeAt?-arc/2:t<recoverAt?Mathf.Lerp(-arc/2,arc/2,Mathf.Clamp01((float)((t-activeAt)/Math.Max(1,recoverAt-activeAt)))):Mathf.Lerp(arc/2,0,Mathf.Clamp01((float)((t-recoverAt)/Math.Max(1,ends-recoverAt)))));}else{angle=heading+(t<activeAt?Mathf.Lerp(85,0,Mathf.Clamp01((float)((t-start)/Math.Max(1,activeAt-start)))):0);radius=t<recoverAt?(landingRange):Mathf.Lerp(landingRange,hammerRange,Mathf.Clamp01((float)((t-recoverAt)/Math.Max(1,ends-recoverAt))));}}
    else if(own&&melee&&ViewTime<v7PreviewMeleeUntil){V7PreviewMeleeTiming(actor,out var wind,out var span,out var duration,out var arc);var elapsed=ViewTime-v7PreviewMeleeStart;active=elapsed>=wind&&elapsed<wind+span;angle=heading+(elapsed<wind?-arc/2:elapsed<wind+span?Mathf.Lerp(-arc/2,arc/2,Mathf.Clamp01((elapsed-wind)/span)):Mathf.Lerp(arc/2,0,Mathf.Clamp01((elapsed-wind-span)/Math.Max(.001f,duration-wind-span))));}
    else if(hammer&&(bool)actor["charging"])angle=heading+Mathf.Lerp(0,85,Mathf.Clamp01((long)actor["charge_ms"]/900f));
    else if((long?)actor["reload_until_ms"]>V6Game){var q=Mathf.Clamp01((float)((V6Game-(long)actor["reload_started_ms"])/Math.Max(1,(int)actor["reload_duration_ms"])));angle-=35*(q<.2f?q/.2f:q<.75f?1:1-(q-.75f)/.25f);}
    var radial=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));var grip=center+radial*.4f;var tip=center+radial*radius;
    if(heavy&&active&&(bool?)attack["impact_valid"]==true)tip=new Vector2((float)attack["impact_x"],(float)attack["impact_y"]);var axis=tip-grip;var modelAngle=Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg;var length=axis.magnitude;var headLength=melee?(blade?length*.65f:V8View?(float)hammerDef["charge"]["head_depth_milli_tiles"]/1000:.6f):length;var width=melee?(blade?.16f:V8View?(float)hammerDef["charge"]["head_width_milli_tiles"]/1000:.6f):.6f;
    model.Root.transform.position=tip+RoomOffset;model.Root.transform.rotation=Quaternion.Euler(0,0,modelAngle);model.Axis.localScale=new Vector3(headLength/Math.Max(.01f,model.NativeDepth),width/Math.Max(.01f,model.NativeWidth),1);model.Head.sortingOrder=direction.y>.5f?6:10;model.Head.color=V8View&&(spin||heavy||(bool)actor["charging"])?Color.Lerp(Color.white,new Color(1,.25f,.2f),spin||heavy?1:Mathf.Clamp01((long)actor["charge_ms"]/900f)):heavy&&active?new Color(1,.45f,.4f):Color.white;
    var haftEnd=tip-axis.normalized*headLength*.4f;var haft=Draw("weapon:v7haft:"+id,(grip+haftEnd)*.5f,new Color32(128,97,61,255),new Vector2(Math.Max(.02f,Vector2.Distance(grip,haftEnd)),.07f),model.Head.sortingOrder);haft.sprite=square;haft.transform.rotation=Quaternion.Euler(0,0,modelAngle);haft.enabled=melee;
    var hand=Draw("weapon:v7hand:"+id,grip,new Color32(184,155,103,255),new Vector2(.15f,.15f),11);hand.sprite=square;
    var sweep=Draw("weapon:v7wind:"+id,center+radial*(radius-.15f),blade?new Color(.65f,.94f,.8f,.5f):new Color(1,.74f,.34f,.5f),new Vector2(.3f,blade?.14f:.6f),9);sweep.sprite=Sector(24);sweep.transform.rotation=Quaternion.Euler(0,0,angle);sweep.enabled=V8View?(active||heavy&&(string)attack?["phase"]!="recover"):active&&!heavy;if(V8View&&(heavy||spin))sweep.color=new Color(1,.2f,.15f,.8f);
    if((active&&!heavy||V8View&&heavy&&(string)attack?["phase"]!="recover")&&ViewTime>=model.NextTrailAt){var slot=model.TrailIndex++%3;model.TrailPoints[slot]=center+radial*(radius-.15f);model.TrailAngles[slot]=angle;model.TrailUntil[slot]=ViewTime+.08f;model.NextTrailAt=ViewTime+1f/30;}
    for(var i=0;i<3;i++){var key="weapon:v7trail:"+id+":"+i;if(melee&&model.TrailUntil[i]>ViewTime){var color=V8View&&(spin||heavy)?new Color(1,.2f,.15f):blade?new Color(.65f,.94f,.8f):new Color(1,.74f,.34f);color.a=.16f*Mathf.Clamp01((model.TrailUntil[i]-ViewTime)/.08f);var trail=Draw(key,model.TrailPoints[i],color,new Vector2(.25f,blade?.12f:.5f),8);trail.sprite=Sector(24);trail.transform.rotation=Quaternion.Euler(0,0,model.TrailAngles[i]);}else if(sprites.TryGetValue(key,out var trail))trail.enabled=false;}
   }
   foreach(var pair in v7Weapons)if(!present.Contains(pair.Key)){pair.Value.Root.SetActive(false);foreach(var prefix in new[]{"weapon:v7haft:","weapon:v7hand:","weapon:v7wind:"})if(sprites.TryGetValue(prefix+pair.Key,out var renderer))renderer.enabled=false;for(var i=0;i<3;i++)if(sprites.TryGetValue("weapon:v7trail:"+pair.Key+":"+i,out var trail))trail.enabled=false;}
   if(sprites.TryGetValue("weapon:v5.wind",out var legacyWind))legacyWind.enabled=false;foreach(var p in (JArray)snapshot["players"])if(sprites.TryGetValue("v6:sweep:"+p["player_id"],out var legacySweep))legacySweep.enabled=false;
  }
  private void DrawV7Props(){
   foreach(var c in ((JArray)snapshot["covers"]).Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0))if(sprites.TryGetValue("covers:"+c["id"],out var renderer)){renderer.sprite=DcV7Sprites.Cover();renderer.transform.localScale=Vector3.one*.5f;renderer.color=Color.white;}
   foreach(var t in ((JArray)snapshot["traps"]).Where(t=>(int)t["room_id"]==room))if(sprites.TryGetValue("traps:"+t["id"],out var renderer)){renderer.sprite=square;renderer.transform.localScale=new Vector3(1.5f,1,1);renderer.color=(string)t["phase"]=="active"?new Color(.8f,.15f,.12f):(string)t["phase"]=="warning"?new Color(.9f,.65f,.2f):new Color(.25f,.3f,.29f);}
  }
  private void DrawV7Final(){
   DrawV7Props();UpdateV7Previews();if(V8View)DrawV8Critical();foreach(var p in (JArray)snapshot["players"])if(sprites.TryGetValue("v6:sweep:"+p["player_id"],out var sweep))sweep.enabled=false;
   foreach(var f in ((JArray)snapshot["fields"]).Where(f=>(int)f["room_id"]==room)){var owner=(string)f["owner"];var p=snapshot["players"].FirstOrDefault(p=>(string)p["player_id"]==owner);if(p==null||!sprites.TryGetValue("fields:"+f["id"],out var circle))continue;var at=owner==ownID?Position:sprites.TryGetValue("player:"+owner,out var actor)?(Vector2)actor.transform.position-RoomOffset:Point(p);circle.transform.position=V7Center(p,at)+RoomOffset;}
  }
 }
}
