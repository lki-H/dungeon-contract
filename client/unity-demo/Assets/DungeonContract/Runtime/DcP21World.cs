using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        internal int ProjectileLoad=>projectiles.Count+(P2?enemies.Where(item=>item.Health>0).Sum(item=>item.ReservedShots):0);
        private int EnemyLoad=>enemies.Count(item=>item.Health>0)+spawnReservations.Count(item=>item.Recovering==null);

        private bool CanTraverse(int source,int destination)
        {
            if(!rooms[destination].Exists)return false;
            if(!P2)return destination>source?rooms[source].Status=="cleared":rooms[source].Status=="cleared";
            if(!rooms[source].Connector)return rooms[source].Status=="cleared"&&rooms[destination].Connector;
            return !rooms[destination].Connector;
        }

        private Vector2 EnemyPath(Enemy enemy,Vector2 target)
        {
            var revision=covers.Where(item=>item.Room==enemy.Room).Sum(item=>item.Revision);
            if(combatNow>=enemy.PathAt||revision!=enemy.PathTerrainRevision){enemy.Path=PathDirection(Layout(enemy.Room),enemy.Position,target,Radius(enemy));enemy.PathAt=combatNow+Number(parameters["ai"],"path_replan_interval_ms",200);enemy.PathTerrainRevision=revision;}
            return enemy.Path;
        }
        private Player P21EnemyTarget(Enemy enemy)
        {
            var nearest=players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==enemy.Room).OrderBy(item=>Vector2.Distance(enemy.Position,item.Position)).ThenBy(item=>item.Slot).FirstOrDefault();
            if(nearest==null)return null;
            if(players.TryGetValue(enemy.TargetID??"",out var previous)&&!previous.Left&&previous.Health>0&&previous.Room==enemy.Room&&(enemy.WarningUntil>0||combatNow<enemy.TargetUntil||Vector2.Distance(enemy.Position,previous.Position)<=Vector2.Distance(enemy.Position,nearest.Position)+1.5f))return previous;
            enemy.TargetID=nearest.ID;enemy.TargetUntil=combatNow+500;return nearest;
        }

        private Vector2 SeparateEnemy(Enemy enemy,Vector2 desired)
        {
            var grouping=combatNow<rooms[enemy.Room].GroupUntil&&rooms[enemy.Room].GroupIDs.Contains(enemy.ID)&&Convert.ToInt32(Rank("group_"+enemy.Room+"_"+rooms[enemy.Room].Wave,"wave").Substring(0,4),16)%100<10;
            var near=enemies.Where(item=>item.ID!=enemy.ID&&item.Room==enemy.Room&&item.Health>0).OrderBy(item=>Vector2.Distance(item.Position,enemy.Position)).ThenBy(item=>item.ID,StringComparer.Ordinal).ToArray();
            var repulsion=Vector2.zero;
            foreach(var other in near){
                var gap=grouping&&rooms[enemy.Room].GroupIDs.Contains(other.ID)?0.9f:1.2f;
                if(V7){repulsion+=DcV7Geometry.Separation(Body(enemy),enemy.Position,Body(other),other.Position,gap,string.CompareOrdinal(enemy.ID,other.ID)>0);continue;}
                var delta=enemy.Position-other.Position;var distance=delta.magnitude;var away=distance>0.001f?delta/distance:(string.CompareOrdinal(enemy.ID,other.ID)>0?Vector2.right:Vector2.left);var minimum=Math.Max(gap,Radius(enemy)+Radius(other));if(distance<minimum)repulsion+=away*(minimum-distance)*2;
            }
            return Vector2.ClampMagnitude(desired+repulsion,1);
        }
        private bool EnemyOverlap(Enemy enemy,Vector2 point){if(V7)return enemies.Any(other=>other.ID!=enemy.ID&&other.Health>0&&other.Room==enemy.Room&&DcV7Geometry.OverlapBodies(Body(enemy),point,Body(other),other.Position)&&Vector2.Distance(point,other.Position)<Vector2.Distance(enemy.Position,other.Position));foreach(var other in enemies){if(other.ID==enemy.ID||other.Health<=0||other.Room!=enemy.Room)continue;var distance=(point-other.Position).sqrMagnitude;var radii=Radius(enemy)+Radius(other);if(distance<radii*radii&&distance<(enemy.Position-other.Position).sqrMagnitude)return true;}return false;}

        private void PrepareEnemyMotion(Enemy enemy,Player target)
        {
            if(enemy.WarningUntil-combatNow<=200)return;
            var offset=target.Position-enemy.Position;
            var direction=offset.normalized;
            if(enemy.FeintSign==0)enemy.FeintSign=Convert.ToInt32(Rank("feint",enemy.ID).Substring(0,4),16)%2==0?1:-1;var sign=enemy.FeintSign;
            var strafe=new Vector2(-direction.y,direction.x)*sign;
            var movement=SeparateEnemy(enemy,strafe*0.35f+direction*(offset.magnitude>3?0.15f:-0.15f));
            enemy.Position=Move(Layout(enemy.Room),enemy.Position,movement*Distance(enemyDefinitions[enemy.Kind],"move_milli_tiles_per_second")/30,Radius(enemy),point=>WeaponWindBlocks(enemy,point)||EnemyOverlap(enemy,point));
            enemy.Origin=enemy.Position;enemy.Aim=(Center(target)-Center(enemy)).normalized;
        }

        private bool SpawnLegal(int room,Vector2 point,string recovering=null,string reservationID=null)
        {
            if(V7)return V7SpawnLegal(room,point,recovering,reservationID);
            var layout=Layout(room);
            if(IsConnector(room)||Blocked(layout,point,0.9f))return false;
            if(players.Values.Any(item=>!item.Left&&(item.Health>0||P3&&item.Life=="downed")&&item.Room==room&&Vector2.Distance(item.Position,point)<2))return false;
            if(enemies.Any(item=>item.ID!=recovering&&item.Health>0&&item.Room==room&&Vector2.Distance(item.Position,point)<Radius(item)+0.9f))return false;
            if(spawnReservations.Any(item=>item.ID!=reservationID&&item.Room==room&&Vector2.Distance(item.Position,point)<1.8f))return false;
            if(traps.Any(item=>item.Room==room&&Vector2.Distance(item.Position,point)<1.8f))return false;
            if(layout["objective"] is Newtonsoft.Json.Linq.JArray&&Vector2.Distance(RoomGeometry.Point(layout["objective"]),point)<2)return false;
            foreach(var door in extension["doors"]){var forward=(int)door["from"]==room;var backward=(int)door["to"]==room;if((forward||backward)&&Vector2.Distance(point,RoomGeometry.DoorPoint(layout,forward?(string)door["side"]:RoomGeometry.Opposite((string)door["side"])))<2)return false;}
            return true;
        }

        private Vector2? P2SpawnPoint(int room,string decision)
        {
            if(V6)return V6SpawnPoint(room,decision);
            var layout=Layout(room);var reachable=new HashSet<Vector2Int>();var pending=new Queue<Vector2Int>();var origin=Vector2Int.RoundToInt(RoomGeometry.Point(layout["spawn"]));reachable.Add(origin);pending.Enqueue(origin);
            var directions=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
            while(pending.Count>0&&reachable.Count<=1024){var cell=pending.Dequeue();foreach(var direction in directions){var next=cell+direction;if(!reachable.Contains(next)&&!Blocked(layout,next,0.9f)){reachable.Add(next);pending.Enqueue(next);}}}
            var decisionSeed=Convert.ToUInt32(Rank(decision,"spawn_order").Substring(0,8),16);
            var candidates=reachable.Select(cell=>(Vector2)cell).Where(point=>SpawnLegal(room,point)).Select(point=>new{Point=point,Visible=SharedVisible(room,point),Edge=SharedVisibleEdge(room,point),Rank=SpawnRank(decisionSeed,(int)point.x,(int)point.y)}).OrderByDescending(item=>item.Edge).ThenByDescending(item=>item.Visible).ThenBy(item=>item.Rank).ThenBy(item=>item.Point.x).ThenBy(item=>item.Point.y).ToArray();
            return candidates.Length==0?(Vector2?)null:candidates[0].Point;
        }
        private static uint SpawnRank(uint seed,int horizontal,int vertical){unchecked{var rank=seed^(uint)(horizontal*73856093)^(uint)(vertical*19349663);rank^=rank>>16;rank*=0x7feb352d;rank^=rank>>15;rank*=0x846ca68b;return rank^(rank>>16);}}

        private bool SharedVisible(int room,Vector2 point)
        {
            var layout=Layout(room);
            foreach(var player in players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==room))
            {
                var center=V8?Center(player):DcP21Camera.Center(layout,player.Position);
                if(Math.Abs(point.x-center.x)<=(V8?10:11.5f)&&Math.Abs(point.y-center.y)<=(V8?4.75f:6.25f))return true;
            }
            return false;
        }
        private bool SharedVisibleEdge(int room,Vector2 point)=>players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==room).Any(item=>{var center=V8?Center(item):DcP21Camera.Center(Layout(room),item.Position);var horizontal=(V8?10:11.5f)-Math.Abs(point.x-center.x);var vertical=(V8?4.75f:6.25f)-Math.Abs(point.y-center.y);return horizontal>=0&&vertical>=0&&Math.Min(horizontal,vertical)<=2;});

        private void ReserveSpawn(int room,string kind,Vector2 position,bool summon=false,string recovering=null)
        {
            spawnReservations.Add(new SpawnReservation{ID=NextID("spawn_warning"),Room=room,Kind=kind,Position=position,Until=combatNow+800,Summon=summon,Recovering=recovering});
        }

        private void TickSpawnReservations()
        {
            if(V7&&!TickV7RecoveryPlacements())return;
            if(V5)TickV5SummonBatch();
            foreach(var reservation in spawnReservations.ToArray())
            {
                if(reservation.Batch!=null)continue;
                if(reservation.Recovering!=null&&!enemies.Any(item=>item.ID==reservation.Recovering&&item.Health>0)){spawnReservations.Remove(reservation);continue;}
                if(!SpawnLegal(reservation.Room,reservation.Position,reservation.Recovering,reservation.ID))
                {
                    spawnReservations.Remove(reservation);var point=V7?V7SpawnPoint(reservation.Room,reservation.ID+":retry",reservation.Kind,reservation.Recovering):SpawnPoint(reservation.Room,reservation.ID+":retry");
                    if(point.HasValue)ReserveSpawn(reservation.Room,reservation.Kind,point.Value,reservation.Summon,reservation.Recovering);
                    else if(reservation.Recovering!=null){if(V7)QueueV7RecoveryPlacement(reservation.Recovering);else AbortTechnical();}else rooms[reservation.Room].Pending.Enqueue(reservation.Kind);
                    continue;
                }
                if(combatNow<reservation.Until)continue;
                spawnReservations.Remove(reservation);
                if(reservation.Recovering==null)SpawnEnemy(reservation.Room,reservation.Kind,reservation.Position,reservation.Summon);
                else {var enemy=enemies.First(item=>item.ID==reservation.Recovering);enemy.Position=reservation.Position;if(V7){enemy.TickStart=reservation.Position;enemy.BodyFacing="down";}enemy.Origin=reservation.Position;enemy.StuckPosition=reservation.Position;enemy.StuckAt=combatNow;enemy.ReadyAt=combatNow+(enemy.Kind=="elite"||enemy.Kind=="boss"?800:500);enemy.AttackAt=enemy.ReadyAt;}
            }
        }

        public void TechnicalWait(bool waiting,long milliseconds)
        {
            if(!P2)return;
            if(waiting&&!technicalWaiting){projectiles.Clear();foreach(var reservation in spawnReservations.Where(item=>item.Recovering==null&&(!V5||!item.Summon)))rooms[reservation.Room].Pending.Enqueue(reservation.Kind);foreach(var reservation in spawnReservations.Where(item=>item.Recovering!=null)){var enemy=enemies.FirstOrDefault(item=>item.ID==reservation.Recovering);if(enemy!=null)enemy.ReadyAt=combatNow+800;}spawnReservations.Clear();FreezeInputs("technical_wait");foreach(var enemy in enemies){enemy.Warning=null;enemy.WarningUntil=0;enemy.ChargeUntil=0;enemy.ReservedShots=0;}}
            if(!waiting&&technicalWaiting){var passed=Math.Max(0,milliseconds-lastTickAt);combatNow+=passed;now=milliseconds;lastTickAt=milliseconds;turrets.RemoveAll(item=>combatNow>=item.Until);foreach(var turret in turrets)turret.AttackAt=combatNow+600;foreach(var enemy in enemies){enemy.AttackAt=combatNow;if(V6){enemy.StuckAt=combatNow;enemy.StuckPosition=enemy.Position;enemy.PathAt=combatNow;enemy.CoverBlockedAt=0;enemy.CoverProbeAt=combatNow;enemy.CoverProbePosition=enemy.Position;}}ResumeInputs();}
            if(V5&&waiting){v5Batch=null;foreach(var player in players.Values)CancelBasic(player);foreach(var enemy in enemies)if(enemy.V5State!=null){enemy.V5State.Emitter=null;enemy.V5State.BurstRemaining=0;enemy.V5State.MeleeEndTick=0;enemy.V5State.MeleeAfterDash=false;enemy.V5State.RecoverUntil=combatNow;}}
            if(V6&&!waiting&&technicalWaiting){ResetV6Traps(milliseconds);v6TickEpoch=now-WorldTick*1000/30;}
            technicalWaiting=waiting;
        }
    }
}
