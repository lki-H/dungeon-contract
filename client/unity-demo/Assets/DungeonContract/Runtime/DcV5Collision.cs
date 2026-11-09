using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcV5Collision
    {
        private static bool CircleInterval(Vector2 start,Vector2 end,Vector2 center,float radius,out float entry,out float exit)
        {
            entry=0;exit=1;var delta=end-start;var offset=start-center;var square=delta.sqrMagnitude;
            if(square<0.00000001f)return offset.sqrMagnitude<=radius*radius;
            var projection=Vector2.Dot(offset,delta);var discriminant=projection*projection-square*(offset.sqrMagnitude-radius*radius);
            if(discriminant<0)return false;
            var root=Mathf.Sqrt(discriminant);entry=Math.Max(0,(-projection-root)/square);exit=Math.Min(1,(-projection+root)/square);return entry<=exit;
        }
        public static float? Circle(Vector2 start,Vector2 end,Vector2 center,float radius)=>CircleInterval(start,end,center,radius,out var entry,out _)?entry:(float?)null;
        private static bool Clip(float value,float delta,ref float entry,ref float exit)
        {
            if(Math.Abs(delta)<0.00000001f)return value>=-0.000001f;
            var crossing=-value/delta;if(delta>0)entry=Math.Max(entry,crossing);else exit=Math.Min(exit,crossing);return entry<=exit;
        }
        private static float Cross(Vector2 first,Vector2 second)=>first.x*second.y-first.y*second.x;
        private static Vector2 Rotate(Vector2 direction,float angle){var radians=angle*Mathf.Deg2Rad;return new Vector2(direction.x*Mathf.Cos(radians)-direction.y*Mathf.Sin(radians),direction.x*Mathf.Sin(radians)+direction.y*Mathf.Cos(radians));}
        public static float? Sector(Vector2 start,Vector2 end,Vector2 center,Vector2 direction,float radius,float arc)
        {
            if(!CircleInterval(start,end,center,radius,out var entry,out var exit))return null;
            if(arc>=359)return entry;
            var offset=start-center;var delta=end-start;var right=Rotate(direction,-arc/2);var left=Rotate(direction,arc/2);
            if(!Clip(Cross(right,offset),Cross(right,delta),ref entry,ref exit)||!Clip(-Cross(left,offset),-Cross(left,delta),ref entry,ref exit))return null;
            return entry>=0&&entry<=1?entry:(float?)null;
        }
        private static float? Box(Vector2 start,Vector2 end,float left,float bottom,float right,float top)
        {
            var entry=0f;var exit=1f;var delta=end-start;
            return Clip(start.x-left,delta.x,ref entry,ref exit)&&Clip(right-start.x,-delta.x,ref entry,ref exit)&&Clip(start.y-bottom,delta.y,ref entry,ref exit)&&Clip(top-start.y,-delta.y,ref entry,ref exit)?entry:(float?)null;
        }
        public static float? Terrain(JObject room,Vector2 start,Vector2 end,float radius)
        {
            var result=2f;var left=1+radius;var bottom=1+radius;var right=(int)room["width"]-1-radius;var top=(int)room["height"]-1-radius;var delta=end-start;
            if(start.x<left||start.x>right||start.y<bottom||start.y>top)result=0;
            else
            {
                if(end.x<left)result=Math.Min(result,(left-start.x)/delta.x);if(end.x>right)result=Math.Min(result,(right-start.x)/delta.x);
                if(end.y<bottom)result=Math.Min(result,(bottom-start.y)/delta.y);if(end.y>top)result=Math.Min(result,(top-start.y)/delta.y);
            }
            foreach(var obstacle in (JArray)room["obstacles"]){var hit=Box(start,end,(float)obstacle[0]-radius,(float)obstacle[1]-radius,(float)obstacle[0]+(float)obstacle[2]+radius,(float)obstacle[1]+(float)obstacle[3]+radius);if(hit.HasValue)result=Math.Min(result,hit.Value);}
            return result<=1?result:(float?)null;
        }
    }

    public sealed partial class DcP1World
    {
        private bool StepV5Hostile(Projectile projectile,Vector2 start,Vector2 end)
        {
            var terrain=DcV5Collision.Terrain(Layout(projectile.Room),start,end,projectile.CollisionRadius)??2;Cover cover=null;
            foreach(var candidate in covers)if(candidate.Room==projectile.Room&&CoverSolid(candidate)){var hit=DcV5Collision.Circle(start,end,candidate.Position,0.45f+projectile.CollisionRadius);if(hit.HasValue&&hit.Value<terrain){terrain=hit.Value;cover=candidate;}}
            var body=2f;Player target=null;var erase=2f;Player protector=null;string cast=null;
            foreach(var player in players.Values)
            {
                if(player.Left||player.Health<=0||player.Room!=projectile.Room)continue;
                var contact=DcV5Collision.Circle(start,end,player.Position,0.3f+projectile.CollisionRadius);
                if(contact.HasValue&&(contact.Value<body||contact.Value==body&&player.Slot<target.Slot)){body=contact.Value;target=player;}
                var attack=player.MeleeAttack;if(attack==null||attack.Phase!="active")continue;
                var heading=Rotate(attack.Direction,(attack.SweepStart+attack.SweepEnd)/2);
                var parry=attack.Heavy?DcV5Collision.Circle(start,end,attack.Impact,0.5f+projectile.CollisionRadius):DcV5Collision.Sector(start,end,player.Position,heading,attack.Range+projectile.CollisionRadius,attack.SweepEnd-attack.SweepStart+12);
                if(parry.HasValue&&parry.Value<erase&&ClearLine(Layout(player.Room),player.Position,Vector2.Lerp(start,end,parry.Value))){erase=parry.Value;protector=player;cast=attack.Root;}
            }
            foreach(var field in fields)
            {
                if(field.Kind!="wind_bind"||field.Room!=projectile.Room)continue;
                var radius=Distance(Kit(players[field.Owner].Loadout)["secondary"],"radius_milli_tiles");var contact=DcV5Collision.Circle(start,end,field.Position,radius+projectile.CollisionRadius);
                if(contact.HasValue&&contact.Value<erase){erase=contact.Value;protector=players[field.Owner];cast=field.ID;}
            }
            var earliest=Math.Min(terrain,Math.Min(body,erase));projectile.Position=Vector2.Lerp(start,end,Math.Min(1,earliest));
            if(earliest>1)return false;
            if(terrain<=Math.Min(body,erase)){if(cover!=null)DamageCover(cover,projectile.Damage);return true;}
            if(erase<=body+0.00001f){if(P3)P3ClearProjectile(protector,projectile,cast);Effect("projectile_erased",protector.ID,projectile.Room,projectile.Position);return true;}
            Hurt(target,projectile.Damage,projectile.Action);return true;
        }
    }
}
