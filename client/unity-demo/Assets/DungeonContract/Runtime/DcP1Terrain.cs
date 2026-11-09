using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private int RoomIndex(JObject layout)=>Array.FindIndex(rooms,room=>(string)Layout(room.Index)["id"]==(string)layout["id"]);
        private readonly System.Collections.Generic.Dictionary<JObject,int> layoutIndexes=new System.Collections.Generic.Dictionary<JObject,int>();
        private readonly System.Collections.Generic.Dictionary<int,(string revision,JObject layout)> pathLayouts=new System.Collections.Generic.Dictionary<int,(string,JObject)>();
        private int CachedRoomIndex(JObject layout){if(!layoutIndexes.TryGetValue(layout,out var index)){index=RoomIndex(layout);layoutIndexes[layout]=index;}return index;}
        private bool CoverSolid(Cover cover)=>frameCovers.Contains(cover.ID)||cover.Health>0;
        private bool Blocked(JObject layout,Vector2 point,float radius)
        {
            if(RoomGeometry.Blocked(layout,point,radius))return true;
            var index=CachedRoomIndex(layout);
            if(V6){foreach(var item in V6Covers(index,point,point,radius))if(CoverSolid(item)&&(point-item.Position).sqrMagnitude<(radius+0.45f)*(radius+0.45f))return true;return false;}
            for(var coverIndex=0;coverIndex<covers.Count;coverIndex++){var item=covers[coverIndex];if(item.Room==index&&CoverSolid(item)&&(point-item.Position).sqrMagnitude<(radius+0.45f)*(radius+0.45f))return true;}
            return false;
        }
        private Vector2 Move(JObject layout,Vector2 start,Vector2 delta,float radius,Func<Vector2,bool> extra=null)=>V7&&v7MovingBody!=null?MoveV7Body(CachedRoomIndex(layout),v7MovingBody,start,delta,extra):RoomGeometry.Move(layout,start,delta,radius,point=>Blocked(layout,point,radius)||(extra?.Invoke(point)??false));
        private bool ClearLine(JObject layout,Vector2 start,Vector2 end)
        {
            if(V6)return !V6Terrain(CachedRoomIndex(layout),start,end,0.05f).HasValue;
            var steps=Math.Max(1,Mathf.CeilToInt(Vector2.Distance(start,end)/0.1f));
            for(var index=0;index<=steps;index++)if(Blocked(layout,Vector2.Lerp(start,end,(float)index/steps),0.05f))return false;
            return true;
        }
        private Vector2 PathDirection(JObject layout,Vector2 start,Vector2 end,float radius)
        {
            if(V7&&v7MovingBody!=null?!V7BodyContact(CachedRoomIndex(layout),v7MovingBody,start,end).HasValue:V6?!V6Terrain(CachedRoomIndex(layout),start,end,radius,true).HasValue:ClearLine(layout,start,end))return (end-start).normalized;
            if(V7&&v7MovingBody!=null){var b=v7MovingBody;var half=(b.Max-b.Min)*.5f;return DcV7Navigation.Direction(V7NavigationLayout(CachedRoomIndex(layout)),start+b.Center,end+b.Center,half);}
            var room=CachedRoomIndex(layout);var solid=covers.Where(item=>item.Room==room&&CoverSolid(item)).ToArray();var revision=string.Join("|",solid.Select(item=>item.ID+":"+item.Revision));
            if(!pathLayouts.TryGetValue(room,out var cached)||cached.revision!=revision){var next=V6?RoomGeometry.NavigationLayout(layout):(JObject)layout.DeepClone();foreach(var cover in solid)((JArray)next["obstacles"]).Add(new JArray(cover.Position.x-0.45f,cover.Position.y-0.45f,0.9f,0.9f));cached=(revision,next);pathLayouts[room]=cached;}
            var geometry=cached.layout;
            return RoomGeometry.PathDirection(geometry,start,end,radius);
        }
        private bool ProjectileTerrain(Projectile projectile)
        {
            if(RoomGeometry.Blocked(Layout(projectile.Room),projectile.Position,0.1f))return true;
            var cover=covers.FirstOrDefault(item=>item.Room==projectile.Room&&CoverSolid(item)&&Vector2.Distance(item.Position,projectile.Position)<0.55f);
            if(cover==null)return false;
            DamageCover(cover,projectile.Damage);return true;
        }
        private void DamageCover(Cover cover,int damage)
        {
            if(cover.Health<=0)return;
            cover.Health=Math.Max(0,cover.Health-damage);Effect(cover.Health==0?"cover_break":"cover_hit",null,cover.Room,cover.Position,damage);
        }
        public void InstallForCheck(string id,string module)
        {
            if(!Practice||!players.TryGetValue(id,out var player)||player.Upgrades.Contains(module)||!upgrades.TryGetValue(module,out var upgrade)||((string)upgrade["scope"]!="common"&&(string)upgrade["scope"]!=player.Loadout))throw new InvalidOperationException("Test module installation requires isolated practice");
            player.Upgrades.Add(module);
        }
    }
}
