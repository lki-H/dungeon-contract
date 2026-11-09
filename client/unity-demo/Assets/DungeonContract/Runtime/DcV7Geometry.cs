using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract {
 public static class DcV7Geometry {
  public sealed class Body {
   public string ID,Kind,Facing,Mask;public Vector2[] Vertices;public Vector2 Center,Min,Max,HullMin,HullMax;public float Scale,MaxCenterRadius;
   public float Radius=>Math.Max((Min-Center).magnitude,(Max-Center).magnitude);
  }
  private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<string,Body>> cache=new System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<string,Body>>();
  public static string Facing(Vector2 aim)=>Math.Abs(aim.x)>Math.Abs(aim.y)?aim.x<0?"left":"right":aim.y>0?"up":"down";
  public static Body Profile(JObject parameters,string kind,string facing){
   var values=cache.GetValue(parameters,p=>new Dictionary<string,Body>(StringComparer.Ordinal));var id=kind+":"+(facing??"down");
   if(values.TryGetValue(id,out var value))return value;
   var row=parameters["body_profiles"]?[id] as JObject??throw new InvalidOperationException("Missing body profile "+id);
   var b=(JArray)row["bounds_milli"];
   value=new Body{ID=id,Kind=kind,Facing=facing??"down",Mask=(string)row["body_mask_hex"],Vertices=((JArray)row["vertices_milli"]).Select(p=>new Vector2((float)p[0],(float)p[1])/1000).ToArray(),Center=new Vector2((float)row["center_milli"][0],(float)row["center_milli"][1])/1000,Min=new Vector2((float)b[0],(float)b[1])/1000,Max=new Vector2((float)b[2],(float)b[3])/1000,Scale=(float)row["scale_milli"]/1000};
   value.HullMin=value.HullMax=value.Vertices[0];foreach(var vertex in value.Vertices){value.HullMin=Vector2.Min(value.HullMin,vertex);value.HullMax=Vector2.Max(value.HullMax,vertex);}
   value.MaxCenterRadius=new Vector2(Math.Max(Math.Abs(value.Min.x-value.Center.x),Math.Abs(value.Max.x-value.Center.x)),Math.Max(Math.Abs(value.Min.y-value.Center.y),Math.Abs(value.Max.y-value.Center.y))).magnitude;
   if(value.Vertices.Length<3||value.Vertices.Length>64||value.Mask.Length!=256)throw new InvalidOperationException("Body geometry budget");values[id]=value;return value;
  }
  public static Vector2[] Rect(float x,float y,float width,float height)=>new[]{new Vector2(x,y),new Vector2(x+width,y),new Vector2(x+width,y+height),new Vector2(x,y+height)};
  private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
  private static void Project(Vector2[] points,Vector2 axis,Vector2 at,out float min,out float max){min=float.MaxValue;max=float.MinValue;var offset=Vector2.Dot(at,axis);foreach(var p in points){var x=Vector2.Dot(p,axis)+offset;min=Math.Min(min,x);max=Math.Max(max,x);}}
  public static DcV6Geometry.Contact? Sweep(Vector2[] body,Vector2 from,Vector2 to,Vector2[] target,Vector2 targetPosition=default,bool separating=false){
   // Reject only disjoint swept bounds; the original SAT contact/tie rules remain below.
   var bodyMin=body[0];var bodyMax=bodyMin;for(var i=1;i<body.Length;i++){bodyMin=Vector2.Min(bodyMin,body[i]);bodyMax=Vector2.Max(bodyMax,body[i]);}
   var targetMin=target[0];var targetMax=targetMin;for(var i=1;i<target.Length;i++){targetMin=Vector2.Min(targetMin,target[i]);targetMax=Vector2.Max(targetMax,target[i]);}
   var sweptMin=Vector2.Min(from,to)+bodyMin;var sweptMax=Vector2.Max(from,to)+bodyMax;targetMin+=targetPosition;targetMax+=targetPosition;
   if(sweptMax.x<targetMin.x-.00001f||sweptMin.x>targetMax.x+.00001f||sweptMax.y<targetMin.y-.00001f||sweptMin.y>targetMax.y+.00001f)return null;
   var delta=to-from;var enter=0f;var exit=1f;var normal=Vector2.zero;var overlap=true;var penetration=float.MaxValue;var separatingNormal=Vector2.zero;
   for(var group=0;group<2;group++){
    var polygon=group==0?body:target;
    for(var i=0;i<polygon.Length;i++){
     var edge=polygon[(i+1)%polygon.Length]-polygon[i];if(edge.sqrMagnitude<1e-10f)continue;var axis=new Vector2(-edge.y,edge.x).normalized;
     Project(body,axis,from,out var a,out var b);Project(target,axis,targetPosition,out var c,out var d);var velocity=Vector2.Dot(delta,axis);
     if(b<c-0.00001f||a>d+0.00001f)overlap=false;
     var depth=Math.Min(b-c,d-a);if(depth<penetration){penetration=depth;separatingNormal=(a+b<c+d?-axis:axis);}
     if(Math.Abs(velocity)<1e-8f){if(b<c-0.00001f||a>d+0.00001f)return null;continue;}
     var first=(c-b)/velocity;var last=(d-a)/velocity;if(first>last){var swap=first;first=last;last=swap;}
     if(first>enter){enter=first;normal=velocity>0?-axis:axis;}exit=Math.Min(exit,last);if(enter>exit+0.000001f)return null;
    }
   }
   if(exit<0||enter>1)return null;
   if(overlap){normal=separatingNormal;if(separating&&Vector2.Dot(delta,normal)>=-0.000001f)return null;}
   return new DcV6Geometry.Contact{Time=Math.Max(0,enter),Normal=normal,Kind="wall"};
  }
  public static bool Overlap(Vector2[] a,Vector2 at,Vector2[] b,Vector2 bt=default)=>Sweep(a,at,at,b,bt).HasValue;
  public static bool OverlapBodies(Body a,Vector2 at,Body b,Vector2 bt){
   if(at.x+a.Max.x<bt.x+b.Min.x-.00001f||at.x+a.Min.x>bt.x+b.Max.x+.00001f||at.y+a.Max.y<bt.y+b.Min.y-.00001f||at.y+a.Min.y>bt.y+b.Max.y+.00001f)return false;
   return Overlap(a.Vertices,at,b.Vertices,bt);
  }
  public static Vector2 Separation(Body a,Vector2 at,Body b,Vector2 bt,float gap,bool positive){
   var delta=(at+a.Center)-(bt+b.Center);var distance=delta.magnitude;
   // A directional support cannot exceed either body's enclosing center radius.
   // Far pairs contribute exactly zero, without walking either visible contour.
   if(distance>=Math.Max(gap,a.MaxCenterRadius+b.MaxCenterRadius+.01f))return Vector2.zero;
   var away=distance>.001f?delta/distance:positive?Vector2.right:Vector2.left;
   var aSupport=float.MinValue;var bSupport=float.MinValue;
   foreach(var vertex in a.Vertices)aSupport=Math.Max(aSupport,Vector2.Dot(vertex-a.Center,-away));
   foreach(var vertex in b.Vertices)bSupport=Math.Max(bSupport,Vector2.Dot(vertex-b.Center,away));
   var minimum=Math.Max(gap,aSupport+bSupport+.01f);return distance<minimum?away*(minimum-distance)*2:Vector2.zero;
  }
  public static float Distance(Vector2 point,Vector2[] polygon){
   var inside=true;var distance=float.MaxValue;
   for(var i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];if(Cross(b-a,point-a)<-0.000001f)inside=false;var edge=b-a;var t=edge.sqrMagnitude<1e-9f?0:Mathf.Clamp01(Vector2.Dot(point-a,edge)/edge.sqrMagnitude);distance=Math.Min(distance,Vector2.Distance(point,a+edge*t));}
   return inside?0:distance;
  }
  public static float? Circle(Vector2 from,Vector2 to,Vector2[] polygon,float radius){
   var minX=Math.Min(from.x,to.x)-radius;var maxX=Math.Max(from.x,to.x)+radius;var minY=Math.Min(from.y,to.y)-radius;var maxY=Math.Max(from.y,to.y)+radius;var outside=15;foreach(var v in polygon){outside&=(v.x<minX?1:0)|(v.x>maxX?2:0)|(v.y<minY?4:0)|(v.y>maxY?8:0);if(outside==0)break;}if(outside!=0)return null;
   if(Distance(from,polygon)<=radius+0.000001f)return 0;float best=2;var delta=to-from;
   for(var i=0;i<polygon.Length;i++){
    var a=polygon[i];var b=polygon[(i+1)%polygon.Length];var edge=b-a;var n=new Vector2(edge.y,-edge.x).normalized;var v=Vector2.Dot(delta,n);
    if(v<-0.0000001f){var t=(radius-Vector2.Dot(from-a,n))/v;if(t>=0&&t<=1){var point=from+delta*t;var along=Vector2.Dot(point-a,edge)/edge.sqrMagnitude;if(along>=0&&along<=1)best=Math.Min(best,t);}}
    var corner=DcV6Geometry.Circle(from,to,a,radius);if(corner.HasValue)best=Math.Min(best,corner.Value);
   }
   return best<=1?best:(float?)null;
  }
  public static float? CircleBody(Vector2 from,Vector2 to,Body body,float radius){
   if(Math.Max(from.x,to.x)+radius<body.Min.x-.00001f||Math.Min(from.x,to.x)-radius>body.Max.x+.00001f||Math.Max(from.y,to.y)+radius<body.Min.y-.00001f||Math.Min(from.y,to.y)-radius>body.Max.y+.00001f)return null;
   return Circle(from,to,body.Vertices,radius);
  }
  private static float SegmentDistance(Vector2 a,Vector2 b,Vector2 c,Vector2 d){
   var u=b-a;var v=d-c;var cross=Cross(u,v);if(Math.Abs(cross)>1e-8f){var t=Cross(c-a,v)/cross;var s=Cross(c-a,u)/cross;if(t>=0&&t<=1&&s>=0&&s<=1)return 0;}
   float Point(Vector2 p,Vector2 x,Vector2 y){var e=y-x;return Vector2.Distance(p,x+e*(e.sqrMagnitude<1e-9f?0:Mathf.Clamp01(Vector2.Dot(p-x,e)/e.sqrMagnitude)));}
   return Math.Min(Math.Min(Point(a,c,d),Point(b,c,d)),Math.Min(Point(c,a,b),Point(d,a,b)));
  }
  public static float? Weapon(Vector2 oldCenter,Vector2 center,float fromDegrees,float toDegrees,float inner,float outer,float width,Vector2 targetFrom,Vector2 targetTo,Vector2[] target){
   var speed=(center-oldCenter-(targetTo-targetFrom)).magnitude+Math.Abs(toDegrees-fromDegrees)*Mathf.Deg2Rad*outer;float t=0;
   for(var iteration=0;iteration<512&&t<=1;iteration++){
    var angle=Mathf.Lerp(fromDegrees,toDegrees,t)*Mathf.Deg2Rad;var dir=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var c=Vector2.Lerp(oldCenter,center,t)-Vector2.Lerp(targetFrom,targetTo,t);var a=c+dir*inner;var b=c+dir*outer;var distance=Math.Min(Distance(a,target),Distance(b,target));
    for(var i=0;i<target.Length;i++)distance=Math.Min(distance,SegmentDistance(a,b,target[i],target[(i+1)%target.Length]));distance-=width;
    if(distance<=0.00002f)return t;if(speed<1e-8f)return null;var step=distance/speed*.9f;if(t+step>1)return null;t+=Math.Max(0.000001f,step);
   }
   return null;
  }
  public static float? WeaponCircle(Vector2 oldCenter,Vector2 center,float fromDegrees,float toDegrees,float inner,float outer,float width,Vector2 targetFrom,Vector2 targetTo,float radius){
   var speed=(center-oldCenter-(targetTo-targetFrom)).magnitude+Math.Abs(toDegrees-fromDegrees)*Mathf.Deg2Rad*outer;float t=0;
   for(var iteration=0;iteration<512&&t<=1;iteration++){
    var angle=Mathf.Lerp(fromDegrees,toDegrees,t)*Mathf.Deg2Rad;var axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var relative=Vector2.Lerp(targetFrom,targetTo,t)-Vector2.Lerp(oldCenter,center,t);var distance=(relative-axis*Mathf.Clamp(Vector2.Dot(relative,axis),inner,outer)).magnitude-radius-width;
    if(distance<=.00002f)return t;if(speed<1e-8f)return null;var step=distance/speed*.9f;if(t+step>1)return null;t+=Math.Max(.000001f,step);
   }return null;
  }
  // Configured inner/outer distances denote visible edges. Capsule centerline
  // endpoints must be inset so the blade/head thickness does not add hidden reach.
  public static float? VisibleWeapon(Vector2 oldCenter,Vector2 center,float from,float to,float inner,float outer,float width,Vector2 targetFrom,Vector2 targetTo,Vector2[] target){var band=Math.Min(width,(outer-inner)/2);return Weapon(oldCenter,center,from,to,inner+band,outer-band,band,targetFrom,targetTo,target);}
  public static float? VisibleWeaponCircle(Vector2 oldCenter,Vector2 center,float from,float to,float inner,float outer,float width,Vector2 targetFrom,Vector2 targetTo,float radius){var band=Math.Min(width,(outer-inner)/2);return WeaponCircle(oldCenter,center,from,to,inner+band,outer-band,band,targetFrom,targetTo,radius);}
  public static bool BodyTerrain(JObject layout,Body body,Vector2 position,IEnumerable<Vector2> covers){
   // Authored bounds include conservative raster padding. Only the actual hull
   // decides border legality, as it does in movement and prediction sweeps.
   if(position.x+body.HullMin.x<1-.00001f||position.y+body.HullMin.y<1-.00001f||position.x+body.HullMax.x>(float)layout["width"]-1+.00001f||position.y+body.HullMax.y>(float)layout["height"]-1+.00001f)return true;
   foreach(var r in (JArray)layout["obstacles"]){var x=(float)r[0];var y=(float)r[1];var w=(float)r[2];var h=(float)r[3];if(position.x+body.Max.x<x||position.x+body.Min.x>x+w||position.y+body.Max.y<y||position.y+body.Min.y>y+h)continue;if(Overlap(body.Vertices,position,Rect(x,y,w,h)))return true;}
   foreach(var c in covers)if(Overlap(body.Vertices,position,Rect(c.x-.4375f,c.y-.4375f,.875f,.875f)))return true;return false;
  }
 }
}
