using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 // Isolated modified-world fixture only. No runtime admission or normal Run calls this.
 public static class DcV7PressureFixture {
  private static Vector2 Point(JToken t)=>new Vector2((float)t["x"],(float)t["y"]);
  private static JObject JsonPoint(Vector2 p)=>new JObject{["x"]=p.x,["y"]=p.y};
  public static void PlaceLegalBodiesAndFlights(JObject parameters,JObject geometry,JObject state){
   var occupied=new Dictionary<int,List<(DcV7Geometry.Body body,Vector2 point)>>();
   var blocks=((JArray)state["covers"]).GroupBy(c=>(int)c["Room"]).ToDictionary(g=>g.Key,g=>g.Select(c=>Point(c["Position"])).ToArray());
   Vector2[] Covers(int room)=>blocks.TryGetValue(room,out var found)?found:Array.Empty<Vector2>();
   Vector2 Place(int room,string kind,Vector2 preferred){
    var body=DcV7Geometry.Profile(parameters,kind,"down");var layout=(JObject)geometry["rooms"][room];if(!occupied.TryGetValue(room,out var used))occupied[room]=used=new List<(DcV7Geometry.Body,Vector2)>();var candidates=new List<Vector2>();
    for(var x=2f;x<(float)layout["width"]-2;x+=.5f)for(var y=2f;y<(float)layout["height"]-2;y+=.5f)candidates.Add(new Vector2(x,y));
    foreach(var at in candidates.OrderBy(p=>(p-preferred).sqrMagnitude).ThenBy(p=>p.x).ThenBy(p=>p.y)){if(DcV7Geometry.BodyTerrain(layout,body,at,Covers(room))||used.Any(o=>DcV7Geometry.Overlap(body.Vertices,at,o.body.Vertices,o.point)||Vector2.Distance(at+body.Center,o.point+o.body.Center)<1.25f))continue;used.Add((body,at));return at;}
    throw new InvalidOperationException("No legal v7 pressure body position "+room+":"+kind);
   }
   foreach(var p in state["players"]){var at=Place((int)p["Room"],(string)p["Loadout"],Point(p["Position"]));p["Position"]=JsonPoint(at);p["TickStart"]=JsonPoint(at);p["BodyFacing"]="down";}
   foreach(var e in state["enemies"]){var at=Place((int)e["Room"],(string)e["Kind"],Point(e["Position"]));e["Position"]=JsonPoint(at);e["TickStart"]=JsonPoint(at);e["Origin"]=JsonPoint(at);e["StuckPosition"]=JsonPoint(at);e["BodyFacing"]="down";}
   var free=new Dictionary<int,List<Vector2>>();var index=0;
   foreach(var shot in state["projectiles"]){var room=(int)shot["Room"];var radius=(float)shot["CollisionRadius"];if(!free.TryGetValue(room,out var points)){points=new List<Vector2>();var layout=(JObject)geometry["rooms"][room];for(var x=2f;x<(float)layout["width"]-2;x+=.5f)for(var y=2f;y<(float)layout["height"]-2;y+=.5f){var p=new Vector2(x,y);var end=p+Vector2.left*(float)shot["Speed"]/30;if(DcV6Geometry.Terrain(layout,p,end,radius).HasValue||Covers(room).Any(c=>DcV6Geometry.Box(p,end,c.x-.4375f,c.y-.4375f,.875f,.875f,radius).HasValue)||occupied[room].Any(o=>DcV7Geometry.Circle(p-o.point,end-o.point,o.body.Vertices,radius).HasValue))continue;points.Add(p);}if(points.Count==0)throw new InvalidOperationException("No legal v7 pressure flight points");free[room]=points;}shot["Position"]=JsonPoint(points[index++%points.Count]);}
  }
 }
}
