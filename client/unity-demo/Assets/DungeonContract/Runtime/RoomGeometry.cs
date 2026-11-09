using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class RoomGeometry
    {
        private sealed class Shape {public int Width;public int Height;public Vector4[] Obstacles;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Shape> shapes=new System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Shape>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<float,Navigation>> navigation=new System.Runtime.CompilerServices.ConditionalWeakTable<JObject,Dictionary<float,Navigation>>();
        private static readonly Vector2Int[] pathDirections={Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
        private sealed class Navigation
        {
            private readonly int width,height;private readonly bool[] walkable;private readonly byte[] links;private readonly int[] queue;private readonly Dictionary<Vector2,int[]> fields=new Dictionary<Vector2,int[]>();
            public Navigation(JObject room,float radius){width=(int)room["width"];height=(int)room["height"];if(width*height>4096)throw new System.IO.InvalidDataException("Navigation grid budget");walkable=new bool[width*height];links=new byte[walkable.Length];queue=new int[walkable.Length];for(var y=0;y<height;y++)for(var x=0;x<width;x++)walkable[y*width+x]=!Blocked(room,new Vector2(x+0.5f,y+0.5f),radius);
                for(var y=1;y<height-1;y++)for(var x=1;x<width-1;x++){var id=y*width+x;if(!walkable[id])continue;for(var d=0;d<4;d++){var q=new Vector2Int(x,y)+pathDirections[d];var next=q.y*width+q.x;if(walkable[next]&&!DcV6Geometry.Terrain(room,new Vector2(x+0.5f,y+0.5f),new Vector2(q.x+0.5f,q.y+0.5f),radius,true).HasValue)links[id]|=(byte)(1<<d);}}
            }
            private Vector2 Center(int id)=>new Vector2(id%width+0.5f,id/width+0.5f);
            private int[] Field(JObject room,Vector2 goal){var cell=Vector2Int.FloorToInt(goal);var target=cell.y*width+cell.x;var key=target>=0&&target<walkable.Length&&walkable[target]?Center(target):goal;if(fields.TryGetValue(key,out var old))return old;if(fields.Count>=24)fields.Clear();var distances=Enumerable.Repeat(-1,walkable.Length).ToArray();var tail=0;
                if(target>=0&&target<walkable.Length&&walkable[target]){distances[target]=0;queue[tail++]=target;}else for(var id=0;id<walkable.Length;id++)if(walkable[id]&&Vector2.Distance(Center(id),goal)<=1.5f&&ClearLine(room,Center(id),goal)){distances[id]=0;queue[tail++]=id;}
                var head=0;while(head<tail){var id=queue[head++];for(var d=0;d<4;d++){var step=pathDirections[d];var x=id%width+step.x;var y=id/width+step.y;if(x<0||y<0||x>=width||y>=height)continue;var next=y*width+x;if((links[next]&(1<<((d+2)%4)))!=0&&distances[next]<0){distances[next]=distances[id]+1;queue[tail++]=next;}}}fields[key]=distances;return distances;
            }
            public Vector2 Direction(JObject room,Vector2 start,Vector2 goal,float radius){var distances=Field(room,goal);var cell=Vector2Int.FloorToInt(start);if(cell.x<0||cell.y<0||cell.x>=width||cell.y>=height)return Vector2.zero;var id=cell.y*width+cell.x;var nextID=-1;var best=int.MaxValue;
                if(distances[id]==0)return (Center(id)-start).normalized;
                for(var d=0;d<4;d++){var q=cell+pathDirections[d];if(q.x<0||q.y<0||q.x>=width||q.y>=height)continue;var next=q.y*width+q.x;if(distances[next]<0||distances[next]>=best||distances[id]>=0&&(links[id]&(1<<d))==0)continue;if(DcV6Geometry.Terrain(room,start,Center(next),radius,true).HasValue)continue;nextID=next;best=distances[next];}
                if(nextID>=0)return (Center(nextID)-start).normalized;if(distances[id]>0&&Vector2.Distance(start,Center(id))>0.05f&&!DcV6Geometry.Terrain(room,start,Center(id),radius,true).HasValue)return (Center(id)-start).normalized;return Vector2.zero;
            }
        }
        private static Shape ParseShape(JObject room){var items=(JArray)room["obstacles"];var rectangles=new Vector4[items.Count];for(var index=0;index<items.Count;index++){var item=items[index];rectangles[index]=new Vector4((float)item[0],(float)item[1],(float)item[2],(float)item[3]);}return new Shape{Width=(int)room["width"],Height=(int)room["height"],Obstacles=rectangles};}
        public static Vector2 Point(JToken point) => new Vector2((float)point[0], (float)point[1]);
        public static JObject NavigationLayout(JObject room){if((bool?)room["v6_geometry"]!=true)return (JObject)room.DeepClone();return new JObject{["id"]=room["id"].DeepClone(),["width"]=room["width"].DeepClone(),["height"]=room["height"].DeepClone(),["v6_geometry"]=true,["obstacles"]=room["obstacles"].DeepClone()};}

        public static bool Blocked(JObject room, Vector2 position, float radius)
        {
            if((bool?)room["v6_geometry"]==true)return DcV6Geometry.Blocked(room,position,radius);
            if(!shapes.TryGetValue(room,out var shape)){shape=ParseShape(room);shapes.Add(room,shape);}
            if (position.x - radius < 1 || position.x + radius > shape.Width - 1 || position.y - radius < 1 || position.y + radius > shape.Height - 1) return true;
            foreach(var rectangle in shape.Obstacles)if(position.x+radius>rectangle.x&&position.x-radius<rectangle.x+rectangle.z&&position.y+radius>rectangle.y&&position.y-radius<rectangle.y+rectangle.w)return true;
            return false;
        }

        public static Vector2 Move(JObject room, Vector2 start, Vector2 delta, float radius, Func<Vector2, bool> extra = null)
        {
            var steps = Math.Max(1, Mathf.CeilToInt(delta.magnitude / 0.1f));
            var step = delta / steps;
            for (var index = 0; index < steps; index++)
            {
                var horizontal = start + new Vector2(step.x, 0);
                if (!Blocked(room, horizontal, radius) && (extra == null || !extra(horizontal))) start = horizontal;
                var vertical = start + new Vector2(0, step.y);
                if (!Blocked(room, vertical, radius) && (extra == null || !extra(vertical))) start = vertical;
            }
            return start;
        }

        public static bool ClearLine(JObject room, Vector2 start, Vector2 end)
        {
            if((bool?)room["v6_geometry"]==true)return !DcV6Geometry.Terrain(room,start,end,0.05f).HasValue;
            var steps = Math.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / 0.1f));
            for (var index = 0; index <= steps; index++) if (Blocked(room, Vector2.Lerp(start, end, (float)index / steps), 0.05f)) return false;
            return true;
        }

        public static Vector2 PathDirection(JObject room, Vector2 start, Vector2 destination, float radius)
        {
            destination.x = Mathf.Clamp(destination.x, 1.5f, (int)room["width"] - 1.5f); destination.y = Mathf.Clamp(destination.y, 1.5f, (int)room["height"] - 1.5f);
            var modern=(bool?)room["v6_geometry"]==true;
            if (modern?!DcV6Geometry.Terrain(room,start,destination,radius,true).HasValue:ClearLine(room,start,destination)) return (destination-start).normalized;
            if(modern){var maps=navigation.GetValue(room,key=>new Dictionary<float,Navigation>());if(!maps.TryGetValue(radius,out var map)){if(maps.Count>=8)maps.Clear();maps[radius]=map=new Navigation(room,radius);}return map.Direction(room,start,destination,radius);}
            var origin = Vector2Int.FloorToInt(start);
            var goal = Vector2Int.FloorToInt(destination);
            var goalBlocked=modern&&Blocked(room,(Vector2)goal+Vector2.one*0.5f,radius);
            var queue = new Queue<Vector2Int>(); queue.Enqueue(origin);
            var parents = new Dictionary<Vector2Int, Vector2Int> { [origin] = origin };
            var directions = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            while (queue.Count > 0 && parents.Count <= 1024)
            {
                var current = queue.Dequeue();
                if (current == goal||goalBlocked&&Vector2.Distance((Vector2)current+Vector2.one*0.5f,destination)<=1.5f&&ClearLine(room,(Vector2)current+Vector2.one*0.5f,destination))
                {
                    while (parents[current] != origin && parents[current] != current) current = parents[current];
                    return ((Vector2)current + Vector2.one * 0.5f - start).normalized;
                }
                foreach (var direction in directions)
                {
                    var next = current + direction;
                    if (!parents.ContainsKey(next) && !Blocked(room, (Vector2)next + Vector2.one * 0.5f, radius)) { parents[next] = current; queue.Enqueue(next); }
                }
            }
            return Vector2.zero;
        }

        public static Vector2 DoorPoint(JObject room, string side)
        {
            var width = (int)room["width"]; var height = (int)room["height"];
            return side == "east" ? new Vector2(width - 1.4f, height / 2f) : side == "west" ? new Vector2(1.4f, height / 2f) : side == "north" ? new Vector2(width / 2f, height - 1.4f) : new Vector2(width / 2f, 1.4f);
        }

        public static string Opposite(string side) => side == "east" ? "west" : side == "west" ? "east" : side == "north" ? "south" : "north";
    }
}
