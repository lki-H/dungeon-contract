using System;
using System.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private void TickWeaponProjectiles()
        {
            if(V6){TickV6Projectiles();return;}
            foreach(var projectile in projectiles.ToArray())
            {
                if(P2&&(!Finite(projectile.Position.x)||!Finite(projectile.Position.y)||!Finite(projectile.Direction.x)||!Finite(projectile.Direction.y)||!Finite(projectile.Speed)||projectile.Speed<=0||projectile.Direction.sqrMagnitude<0.99f||projectile.Direction.sqrMagnitude>1.01f)){SetTerminal("aborted","technical_failure");return;}
                var distance=projectile.Unbounded?projectile.Speed/30f:Math.Min(projectile.Remaining,projectile.Speed/30f);var removed=false;var steps=Math.Min(8,Math.Max(1,Mathf.CeilToInt(distance/0.1f)));var stride=distance/steps;
                for(var index=0;index<steps&&!removed;index++)
                {
                    var previous=projectile.Position;var next=previous+projectile.Direction*stride;if(!projectile.Unbounded)projectile.Remaining=Math.Max(0,projectile.Remaining-stride);
                    if(V5&&projectile.Enemy){removed=StepV5Hostile(projectile,previous,next);continue;}
                    var cover=covers.Where(item=>item.Room==projectile.Room&&CoverSolid(item)).OrderBy(item=>Vector2.Distance(item.Position,previous)).FirstOrDefault(item=>Vector2.Distance(next,item.Position)<0.45f+projectile.CollisionRadius);
                    var wall=RoomGeometry.Blocked(Layout(projectile.Room),next,projectile.CollisionRadius);
                    if(cover!=null||wall)
                    {
                        if(cover!=null)DamageCover(cover,projectile.Damage);
                        if(!projectile.RuneBolt||projectile.Bounces<=0){removed=true;continue;}
                        var normal=cover!=null?(previous-cover.Position).normalized:WeaponWallNormal(projectile,previous,next);
                        if(normal.sqrMagnitude<0.01f){removed=true;continue;}
                        projectile.Direction=(projectile.Direction-2*Vector2.Dot(projectile.Direction,normal)*normal).normalized;projectile.Bounces--;projectile.Position=previous+normal*0.01f;
                        if(Blocked(Layout(projectile.Room),projectile.Position,projectile.CollisionRadius))removed=true;
                        Effect("ricochet",projectile.Owner,projectile.Room,previous);continue;
                    }
                    projectile.Position=next;
                    if(WeaponErase(projectile)){removed=true;continue;}
                    if(projectile.Enemy)
                    {
                        var target=players.Values.Where(player=>!player.Left&&player.Health>0&&player.Room==projectile.Room).OrderBy(player=>player.Slot).FirstOrDefault(player=>Vector2.Distance(player.Position,next)<0.3f+projectile.CollisionRadius);
                        if(target!=null){Hurt(target,projectile.Damage,projectile.Action);removed=true;}
                    }
                    else
                    {
                        var enemy=enemies.Where(enemy=>enemy.Health>0&&enemy.Room==projectile.Room&&!projectile.Hit.Contains(enemy.ID)&&Vector2.Distance(enemy.Position,next)<Radius(enemy)+projectile.CollisionRadius).OrderBy(enemy=>Vector2.Distance(enemy.Position,previous)).ThenBy(enemy=>enemy.ID,StringComparer.Ordinal).FirstOrDefault();
                        if(enemy==null)continue;
                        projectile.Hit.Add(enemy.ID);Hit(enemy,projectile.Owner,projectile.Action,projectile.Damage,projectile.Direction,true,projectile.SourceLife,projectile.EmissionFrame,projectile.Root,P2?(bool?)projectile.FrostEnabled:null);
                        if(projectile.RuneBolt&&!projectile.ChainTriggered&&players.TryGetValue(projectile.Owner,out var owner)&&(P2?projectile.ChainEnabled:Has(owner,"runesmith_chain")))
                        {
                            projectile.ChainTriggered=true;
                            if(V5&&!v5ChainRoots.Add(projectile.Root??projectile.Action)){if(projectile.RuneBolt||projectile.Penetration--<=0)removed=true;continue;}
                            var nextEnemy=enemies.Where(item=>item.Health>0&&item.Room==projectile.Room&&item.ID!=enemy.ID&&Vector2.Distance(item.Position,enemy.Position)<=3&&ClearLine(Layout(item.Room),enemy.Position,item.Position)).OrderBy(item=>Vector2.Distance(item.Position,enemy.Position)).ThenBy(item=>item.ID,StringComparer.Ordinal).FirstOrDefault();
                            if(nextEnemy!=null)Hit(nextEnemy,owner.ID,V5?projectile.Action+"_chain":projectile.Action,projectile.Damage*700/1000,null,true,projectile.SourceLife,projectile.EmissionFrame,projectile.Root,P2?(bool?)projectile.FrostEnabled:null);
                        }
                        if(projectile.RuneBolt||projectile.Penetration--<=0)removed=true;
                    }
                }
                if(removed||!projectile.Unbounded&&projectile.Remaining<=0)projectiles.Remove(projectile);
            }
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private Vector2 WeaponWallNormal(Projectile projectile,Vector2 previous,Vector2 next)
        {
            var horizontal=RoomGeometry.Blocked(Layout(projectile.Room),new Vector2(next.x,previous.y),projectile.CollisionRadius);
            var vertical=RoomGeometry.Blocked(Layout(projectile.Room),new Vector2(previous.x,next.y),projectile.CollisionRadius);
            if(horizontal&&vertical)return -projectile.Direction;
            if(horizontal)return new Vector2(-Math.Sign(projectile.Direction.x),0);
            if(vertical)return new Vector2(0,-Math.Sign(projectile.Direction.y));
            var rectangle=((Newtonsoft.Json.Linq.JArray)Layout(projectile.Room)["obstacles"]).FirstOrDefault(item=>next.x+projectile.CollisionRadius>(float)item[0]&&next.x-projectile.CollisionRadius<(float)item[0]+(float)item[2]&&next.y+projectile.CollisionRadius>(float)item[1]&&next.y-projectile.CollisionRadius<(float)item[1]+(float)item[3]);
            if(rectangle==null)return -projectile.Direction;
            var center=new Vector2((float)rectangle[0]+(float)rectangle[2]/2,(float)rectangle[1]+(float)rectangle[3]/2);
            return (previous-center).normalized;
        }
    }
}
