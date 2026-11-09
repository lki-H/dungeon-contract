using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public sealed partial class DcS1Prediction {
  private bool V7=>(int?)geometry["parameters"]?["versions"]?["battle_protocol"]>=7;
  private string v7Kit="ranger",v7Facing="down";private ushort v7Aim;
  public string BodyFacing=>v7Facing;
  private DcV7Geometry.Body PredictedBody=>DcV7Geometry.Profile((JObject)geometry["parameters"],v7Kit,v7Facing);
  private bool V7PredictionLegal(Vector2 at,DcV7Geometry.Body body){
   var layout=(JObject)geometry["rooms"][room];if(DcV7Geometry.BodyTerrain(layout,body,at,((JArray)snapshot["covers"]).Where(c=>(int)c["room_id"]==room&&(int)c["health"]>0).Select(c=>new Vector2((float)c["x"],(float)c["y"]))))return false;
   foreach(var e in (JArray)snapshot["enemies"]){if((int)e["room_id"]!=room||((string)e["kind"]!="elite"&&(string)e["kind"]!="boss"))continue;var b=DcV7Geometry.Profile((JObject)geometry["parameters"],(string)e["kind"],(string)e["body_facing"]??"down");if(DcV7Geometry.Overlap(body.Vertices,at,b.Vertices,new Vector2((float)e["x"],(float)e["y"])))return false;}return true;
  }
  private DcV6Geometry.Contact? V7PredictionContact(Vector2 from,Vector2 to,DcV7Geometry.Body body){
   DcV6Geometry.Contact? best=null;void Pick(DcV6Geometry.Contact? h){if(h.HasValue&&(!best.HasValue||h.Value.Time<best.Value.Time))best=h;}
   var layout=(JObject)geometry["rooms"][room];var w=(float)layout["width"];var height=(float)layout["height"];
   foreach(var r in new[]{DcV7Geometry.Rect(-100,-100,101,height+200),DcV7Geometry.Rect(w-1,-100,101,height+200),DcV7Geometry.Rect(-100,-100,w+200,101),DcV7Geometry.Rect(-100,height-1,w+200,101)})Pick(DcV7Geometry.Sweep(body.Vertices,from,to,r,default,true));
   foreach(var r in (JArray)layout["obstacles"]){var x=(float)r[0];var y=(float)r[1];var rw=(float)r[2];var rh=(float)r[3];if(Math.Max(from.x,to.x)+body.Max.x<x||Math.Min(from.x,to.x)+body.Min.x>x+rw||Math.Max(from.y,to.y)+body.Max.y<y||Math.Min(from.y,to.y)+body.Min.y>y+rh)continue;Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(x,y,rw,rh),default,true));}
   foreach(var c in (JArray)snapshot["covers"])if((int)c["room_id"]==room&&(int)c["health"]>0)Pick(DcV7Geometry.Sweep(body.Vertices,from,to,DcV7Geometry.Rect(-.4375f,-.4375f,.875f,.875f),new Vector2((float)c["x"],(float)c["y"]),true));
   foreach(var e in (JArray)snapshot["enemies"]){if((int)e["room_id"]!=room||((string)e["kind"]!="elite"&&(string)e["kind"]!="boss"))continue;var b=DcV7Geometry.Profile((JObject)geometry["parameters"],(string)e["kind"],(string)e["body_facing"]??"down");Pick(DcV7Geometry.Sweep(body.Vertices,from,to,b.Vertices,new Vector2((float)e["x"],(float)e["y"]),true));}return best;
  }
  private void AdvanceV7Prediction(Vector2 move,float dt,double at){
   var requested=DcV7Geometry.Facing(DcP1World.Aim(v7Aim));if(requested!=v7Facing){var next=DcV7Geometry.Profile((JObject)geometry["parameters"],v7Kit,requested);if(V7PredictionLegal(Position,next))v7Facing=requested;}
   var body=PredictedBody;var remaining=dt;var dashTime=(float)Math.Max(0,Math.Min(dt,(dashUntil-(at-dt*1000))/1000));
   if(dashTime>0){var end=Position+direction*dodgeSpeed*dashTime;var h=V7PredictionContact(Position,end,body);if(h.HasValue){Position=Vector2.Lerp(Position,end,Math.Max(0,h.Value.Time-.0001f));remaining-=dashTime*h.Value.Time;dashUntil=at;}else{Position=end;remaining-=dashTime;}}
   var delta=move*Speed*(charging?.7f:meleeMoveFactor)*remaining;
   for(var n=0;n<3&&delta.sqrMagnitude>.00000001f;n++){var h=V7PredictionContact(Position,Position+delta,body);if(!h.HasValue){Position+=delta;break;}Position+=delta*Math.Max(0,h.Value.Time-.0001f);delta*=1-h.Value.Time;delta-=h.Value.Normal*Math.Min(0,Vector2.Dot(delta,h.Value.Normal));}
  }
 }
}
