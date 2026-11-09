using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract {
 public static class DcV7Navigation {
  private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<Vector2Int,Grid>> maps=new System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<Vector2Int,Grid>>();
  private static readonly Vector2Int[] steps={Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
  private sealed class Grid {
   private readonly JObject room;private readonly Vector2 half;private readonly Vector4[] rectangles;private readonly int width,height;private readonly bool[] legal;private readonly byte[] links;private readonly int[] queue;private readonly Dictionary<int,int[]> fields=new Dictionary<int,int[]>();
   private Vector2 Point(int id)=>new Vector2((id%width)*.5f,(id/width)*.5f);
   public Grid(JObject r,Vector2 size){room=r;half=size;width=(int)r["width"]*2+1;height=(int)r["height"]*2+1;if(width*height>4096)throw new InvalidOperationException("V7 navigation grid budget");legal=new bool[width*height];links=new byte[legal.Length];queue=new int[legal.Length];rectangles=((JArray)r["obstacles"]).Select(v=>new Vector4((float)v[0],(float)v[1],(float)v[2],(float)v[3])).Concat(new[]{new Vector4(-100,-100,101,height+100),new Vector4(width/2f-1.5f,-100,101,height+100),new Vector4(-100,-100,width+100,101),new Vector4(-100,height/2f-1.5f,width+100,101)}).ToArray();
    for(var id=0;id<legal.Length;id++)legal[id]=true;var thin=false;
    foreach(var v in rectangles){
     var x0=Math.Max(0,Mathf.FloorToInt((v.x-half.x+.00001f)*2)+1);var x1=Math.Min(width-1,Mathf.CeilToInt((v.x+v.z+half.x-.00001f)*2)-1);var y0=Math.Max(0,Mathf.FloorToInt((v.y-half.y+.00001f)*2)+1);var y1=Math.Min(height-1,Mathf.CeilToInt((v.y+v.w+half.y-.00001f)*2)-1);
     for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++)legal[y*width+x]=false;
     if(v.z+half.x*2<.5001f||v.w+half.y*2<.5001f)thin=true;
    }
    // A cardinal half-tile edge with legal ends cannot cross an expanded rectangle
    // wider than that edge. Retain a swept check for genuinely thinner geometry.
    for(var y=1;y<height-1;y++)for(var x=1;x<width-1;x++){var id=y*width+x;if(!legal[id])continue;for(var d=0;d<4;d++){var next=(y+steps[d].y)*width+x+steps[d].x;if(legal[next]&&(!thin||!BlockedSegment(Point(id),Point(next))))links[id]|=(byte)(1<<d);}}
   }
   private bool BlockedSegment(Vector2 a,Vector2 b){foreach(var v in rectangles)if(DcV6Geometry.Box(a,b,v.x-half.x,v.y-half.y,v.z+half.x*2,v.w+half.y*2,0,true).HasValue)return true;return false;}
   private int[] Field(Vector2 goal){
    var gx=Mathf.Clamp(Mathf.RoundToInt(goal.x*2),0,width-1);var gy=Mathf.Clamp(Mathf.RoundToInt(goal.y*2),0,height-1);var target=gy*width+gx;
    if(!legal[target]){var best=float.MaxValue;var found=-1;for(var id=0;id<legal.Length;id++)if(legal[id]){var distance=(Point(id)-goal).sqrMagnitude;if(distance<best&&distance<4&&!DcV6Geometry.Terrain(room,Point(id),goal,.05f).HasValue){best=distance;found=id;}}if(found<0)return null;target=found;}
    if(fields.TryGetValue(target,out var known))return known;if(fields.Count>=16)fields.Clear();var distanceField=Enumerable.Repeat(-1,legal.Length).ToArray();var head=0;var tail=0;queue[tail++]=target;distanceField[target]=0;
    while(head<tail){var id=queue[head++];for(var d=0;d<4;d++){var x=id%width+steps[d].x;var y=id/width+steps[d].y;if(x<0||y<0||x>=width||y>=height)continue;var next=y*width+x;if((links[next]&(1<<((d+2)%4)))!=0&&distanceField[next]<0){distanceField[next]=distanceField[id]+1;queue[tail++]=next;}}}
    fields[target]=distanceField;return distanceField;
   }
   public Vector2 Direction(Vector2 start,Vector2 goal){
    if(!BlockedSegment(start,goal))return (goal-start).normalized;var distances=Field(goal);if(distances==null)return Vector2.zero;var cell=Vector2Int.RoundToInt(start*2);var best=float.MaxValue;var nextID=-1;
    for(var y=Math.Max(0,cell.y-3);y<=Math.Min(height-1,cell.y+3);y++)for(var x=Math.Max(0,cell.x-3);x<=Math.Min(width-1,cell.x+3);x++){var id=y*width+x;if(!legal[id]||distances[id]<0)continue;var delta=Point(id)-start;if(delta.sqrMagnitude<.0025f)continue;var score=distances[id]*.5f+delta.magnitude*1.01f;if(score>=best||BlockedSegment(start,Point(id)))continue;best=score;nextID=id;}
    return nextID<0?Vector2.zero:(Point(nextID)-start).normalized;
   }
   public float Distance(Vector2 start,Vector2 goal){
    if(!BlockedSegment(start,goal))return Vector2.Distance(start,goal);
    var distances=Field(goal);if(distances==null)return 100000;var cell=Vector2Int.RoundToInt(start*2);var best=100000f;
    for(var y=Math.Max(0,cell.y-3);y<=Math.Min(height-1,cell.y+3);y++)for(var x=Math.Max(0,cell.x-3);x<=Math.Min(width-1,cell.x+3);x++){var id=y*width+x;if(!legal[id]||distances[id]<0||BlockedSegment(start,Point(id)))continue;best=Math.Min(best,distances[id]*.5f+Vector2.Distance(start,Point(id)));}return best;
   }
  }
  private static Grid GetGrid(JObject room,Vector2 half){var sizes=maps.GetValue(room,_=>new Dictionary<Vector2Int,Grid>());var key=new Vector2Int(Mathf.CeilToInt(half.x*1000),Mathf.CeilToInt(half.y*1000));if(!sizes.TryGetValue(key,out var grid)){if(sizes.Count>=8)sizes.Clear();sizes[key]=grid=new Grid(room,(Vector2)key/1000f);}return grid;}
  public static Vector2 Direction(JObject room,Vector2 start,Vector2 goal,Vector2 half)=>GetGrid(room,half).Direction(start,goal);
  public static float Distance(JObject room,Vector2 start,Vector2 goal,Vector2 half)=>GetGrid(room,half).Distance(start,goal);
 }
}
