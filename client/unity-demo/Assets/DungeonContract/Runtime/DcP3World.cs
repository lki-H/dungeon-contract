using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private readonly Dictionary<string,int> p3Samples=new Dictionary<string,int>();
        private readonly Dictionary<string,JObject> p3Defense=new Dictionary<string,JObject>();
        private readonly HashSet<string> p3Effects=new HashSet<string>();
        public JObject CaptureP3Intents()=>new JObject{["queued"]=JArray.FromObject(queued,serializer),["foundation"]=JObject.FromObject(foundationInputs,serializer)};
        public void RestoreP3Intents(JObject value){queued.Clear();foreach(var item in value["queued"].ToObject<QueuedAction[]>(serializer))if(!players[item.Player].Left)queued.Enqueue(item);foundationInputs.Clear();foreach(var property in ((JObject)value["foundation"]).Properties())if(!players[property.Name].Left)foundationInputs[property.Name]=property.Value.ToObject<DcP1Input>(serializer);}
        public void AbortP3(){WorldTick++;SetTerminal("aborted","technical_failure");}

        private bool NeedP3Fact(string kind,string actor,string target)
        {
            if(facts.Count>=96)return false;
            var key=kind+":"+(kind=="kill"?"team":actor);p3Samples.TryGetValue(key,out var count);
            var maximum=kind=="damage"?100:64;
            if(count>=maximum)return false;p3Samples[key]=count+1;return true;
        }
        private JObject P3LifeFact(string kind,Player player)
        {
            var fact=Fact(kind,player.ID,player.ID,null,0);fact["life_generation"]=player.LifeGeneration;fact["downed_generation"]=player.DownedGeneration;return fact;
        }
        private List<JObject> P3Facts()
        {
            foreach(var fact in facts)
            {
                var actor=(string)fact["actor_player_id"];
                if(fact["life_generation"]==null)fact["life_generation"]=actor!=null&&players.TryGetValue(actor,out var player)?player.LifeGeneration:0;
                if(fact["downed_generation"]==null)fact["downed_generation"]=0;
                fact["event_game_ms"]=now;if(fact["proof"]==null)fact["proof"]=null;
            }
            return facts.Select(item=>(JObject)item.DeepClone()).ToList();
        }
        private void RecordP3Defense(Player player,string kind,string action,int amount,int room)
        {
            if(player.Left||player.Room!=room||!(Layout(room)["objective"] is JArray objective))return;
            var distance=Vector2.Distance(player.Position,RoomGeometry.Point(objective));if(distance>Distance(config["player"],"defense_radius_milli_tiles"))return;
            p3Defense[room+":"+player.ID]=new JObject{["player_id"]=player.ID,["kind"]=kind,["action_id"]=action,["room_id"]=room,["event_game_ms"]=now,["distance_milli"]=Mathf.RoundToInt(distance*1000),["effective_amount"]=amount};
        }
        private void P3Defenders(JObject fact,Player operatorPlayer)
        {
            var proofs=players.Values.OrderBy(item=>item.Slot).Where(item=>!item.Left&&item.Room==operatorPlayer.Room&&item.ID!=operatorPlayer.ID&&p3Defense.TryGetValue(item.Room+":"+item.ID,out var proof)&&now-(long)proof["event_game_ms"]<=5000&&Vector2.Distance(item.Position,RoomGeometry.Point(Layout(item.Room)["objective"]))<=Distance(config["player"],"defense_radius_milli_tiles")).Select(item=>p3Defense[item.Room+":"+item.ID]).ToArray();
            fact["contributors"]=new JArray(proofs.Select(item=>(string)item["player_id"]));fact["proof"]=new JObject{["defenders"]=new JArray(proofs.Select(item=>item.DeepClone()))};
        }
        private bool TickP3Rescue(Player actor)
        {
            var target=players.Values.Where(item=>!item.Left&&item.Life=="downed"&&item.ID!=actor.ID&&item.Room==actor.Room&&now<item.DownedUntil&&Vector2.Distance(item.Position,actor.Position)<=Distance(parameters["player"],"rescue_range_milli_tiles")&&ClearLine(Layout(actor.Room),actor.Position,item.Position)).OrderBy(item=>Vector2.Distance(item.Position,actor.Position)).ThenBy(item=>item.Slot).FirstOrDefault();
            var legal=actor.Health>0&&!actor.Left&&actor.Connected&&actor.Input.Interact&&!actor.Input.Attack&&!actor.Input.Melee&&!actor.Charging&&actor.Input.Move.sqrMagnitude==0&&now>=actor.DodgeUntil&&actor.LastCombatAt!=now;
            if(!legal||target==null){actor.RescueTarget=null;return false;}
            actor.InteractionTarget=null;
            if(players.Values.Any(item=>item.Slot<actor.Slot&&item.RescueTarget==target.ID&&item.Connected&&item.Health>0)){actor.RescueTarget=null;return true;}
            if(actor.RescueTarget!=target.ID||actor.RescueEpisode!=target.DownedGeneration){actor.RescueTarget=target.ID;actor.RescueEpisode=target.DownedGeneration;actor.RescueAt=now;actor.RescueAction=NextID("rescue");}
            if(actor.HurtAt>=actor.RescueAt){actor.RescueTarget=null;return true;}
            if(now-actor.RescueAt<Number(parameters["player"],"rescue_ms"))return true;
            if((V7?V7Blocked(target.Room,Body(target),target.Position):Blocked(Layout(target.Room),target.Position,PlayerRadius))||enemies.Any(enemy=>enemy.Room==target.Room&&enemy.Health>0&&(enemy.Kind=="elite"||enemy.Kind=="boss")&&(V7?V7ActorOverlap(Body(target),target.Position,enemy):Vector2.Distance(enemy.Position,target.Position)<Radius(enemy)+PlayerRadius))){actor.RescueTarget=null;Effect("rescue_blocked",actor.ID,actor.Room,actor.Position);return true;}
            var fact=Fact("rescued",actor.ID,target.ID,actor.RescueAction,Number(parameters["player"],"rescue_health"));fact["downed_generation"]=target.DownedGeneration;
            fact["proof"]=new JObject{["target_player_id"]=target.ID,["target_life_generation"]=target.LifeGeneration,["started_game_ms"]=actor.RescueAt,["distance_milli"]=Mathf.RoundToInt(Vector2.Distance(actor.Position,target.Position)*1000)};facts.Add(fact);
            target.Health=Number(parameters["player"],"rescue_health");target.Life="alive";target.DownedUntil=0;target.Input=default;target.ReleaseRequired=true;target.InvulnerableUntil=now+Number(parameters["player"],"rescue_protection_ms");target.ProtectedUntil=target.InvulnerableUntil;if(V6)target.RescueUntil=target.ProtectedUntil;actor.Rescues++;actor.RescueTarget=null;Effect("rescued",actor.ID,target.Room,target.Position);return true;
        }
        private void TickP3Life(bool victory)
        {
            foreach(var player in players.Values.OrderBy(item=>item.Slot))
            {
                if(player.Left){player.Life="left";player.RescueTarget=null;continue;}
                if(player.Health>0&&player.Room==4&&!player.Reached.Contains("inspect_zone")&&(V7?V7InInspectZone(player):Vector2.Distance(player.Position,RoomGeometry.Point(Layout(4)["spawn"]))<=2)){player.Reached.Add("inspect_zone");player.Reaches++;facts.Add(Fact("reach",player.ID,"inspect_zone",null,0));}
                if(player.Life=="downed"&&(now>=player.DownedUntil||!players.Values.Any(item=>!item.Left&&item.Health>0&&item.Connected))){player.Life="dead";player.Deaths++;player.RescueTarget=null;facts.Add(P3LifeFact("death",player));Effect("death",player.ID,player.Room,player.Position);}
                if(victory||player.Life!="awaiting_respawn"||player.Reinforcement==null||player.SpawnAwaitingAck||now<player.RespawnAt)continue;
                var room=Math.Min(4,Math.Max(0,MaximumRoom-1));var spawn=RoomGeometry.Point(Layout(room)["spawn"]);Vector2? point=null;
                for(var index=0;index<32;index++){var candidate=spawn+Rotate(Vector2.right,index*137.5f)*(index/8)*0.6f;if((V7?!V7Blocked(room,Body(player),candidate):!Blocked(Layout(room),candidate,PlayerRadius))&&!enemies.Any(enemy=>enemy.Room==room&&enemy.Health>0&&(V7?(Vector2.Distance(Center(enemy),candidate+Body(player).Center)<2||V7ActorOverlap(Body(player),candidate,enemy)):Vector2.Distance(enemy.Position,candidate)<2))&&!traps.Any(trap=>trap.Room==room&&(V7?DcV7Geometry.Overlap(Body(player).Vertices,candidate,DcV7Geometry.Rect(-.875f,-.625f,1.75f,1.25f),trap.Position):Vector2.Distance(trap.Position,candidate)<1.5f))){point=candidate;break;}}
                if(!point.HasValue){SetTerminal("aborted","technical_failure");continue;}
                player.Room=room;player.Explored.Add(room);player.Position=point.Value;player.SpawnAwaitingAck=true;
                var fact=P3LifeFact("reinforcement_spawned",player);fact["reinforcement_action_id"]=player.Reinforcement;facts.Add(fact);
            }
            if(victory||Terminal)return;
            var members=players.Values.Where(item=>!item.Left).ToArray();
            if(members.Length>0&&members.All(item=>item.Health<=0)&&reinforcementRemaining<=0&&members.All(item=>item.Reinforcement==null))SetTerminal("failed","team_wipe");
            foreach(var key in p3Defense.Where(item=>now-(long)item.Value["event_game_ms"]>5000).Select(item=>item.Key).ToArray())p3Defense.Remove(key);
            var active=fields.Select(item=>item.ID).Concat(players.Values.Where(item=>item.Shield>0&&now<item.ShieldUntil).Select(item=>item.ShieldCast)).Where(item=>item!=null).ToHashSet();
            p3Effects.RemoveWhere(key=>!active.Any(cast=>key.Contains(":"+cast+":")));
        }
        private void P3Protection(Player target,int amount,string attack)
        {
            if(target.ShieldOwner==null||target.ShieldOwner==target.ID||!players.TryGetValue(target.ShieldOwner,out var owner)||owner.Left)return;
            owner.ProtectedDamage+=amount;RecordP3Defense(owner,"protection",target.ShieldCast,amount,target.Room);
            if(facts.Count>=96)return;
            var key="shield:"+target.ShieldCast+":"+target.ID;if(!p3Effects.Add(key))return;
            var sample="protection:"+owner.ID;p3Samples.TryGetValue(sample,out var sampled);if(sampled>=256)return;p3Samples[sample]=sampled+1;
            var fact=Fact("protection",owner.ID,target.ID,target.ShieldCast,amount);fact["proof"]=new JObject{["target_player_id"]=target.ID};facts.Add(fact);
        }
        private void P3Control(Player owner,Enemy enemy,string cast,bool effective)
        {
            if(!effective||owner.Left)return;owner.ControlEffect+=33;RecordP3Defense(owner,"control_effect",cast,33,enemy.Room);
            if(facts.Count>=96)return;
            if(!p3Effects.Add("control:"+cast+":"+enemy.ID))return;
            var sample="control:"+owner.ID;p3Samples.TryGetValue(sample,out var sampled);if(sampled>=256)return;p3Samples[sample]=sampled+1;
            var fact=Fact("control_effect",owner.ID,enemy.ID,cast,33);facts.Add(fact);
        }
        private void P3ClearProjectile(Player owner,Projectile projectile,string cast)
        {
            var target=players.Values.Where(item=>!item.Left&&item.Health>0&&item.Room==projectile.Room).OrderBy(item=>item.Slot).FirstOrDefault(item=>{var offset=item.Position-projectile.Position;var travel=Vector2.Dot(offset,projectile.Direction);if(V6){var contact=now+Math.Max(0,travel-projectile.CollisionRadius-PlayerHurtRadius)*1000/Math.Max(0.001f,projectile.Speed);if(contact<Math.Max(Math.Max(item.SpaceUntil,item.GustUntil),Math.Max(item.HurtUntil,item.ProtectedUntil)))return false;}return travel>=0&&travel<=projectile.Speed&&Vector2.Distance(projectile.Position+projectile.Direction*travel,item.Position)<=projectile.CollisionRadius+PlayerHurtRadius&&ClearLine(Layout(projectile.Room),projectile.Position,item.Position);});
            if(target==null)return;owner.ClearedProjectiles++;RecordP3Defense(owner,"control_effect",cast,1,projectile.Room);if(facts.Count>=96)return;
            var sample="clear:"+owner.ID;p3Samples.TryGetValue(sample,out var sampled);if(sampled>=128)return;p3Samples[sample]=sampled+1;
            var fact=Fact("projectile_cleared",owner.ID,projectile.ID,cast,1);fact["proof"]=new JObject{["target_player_id"]=target.ID};facts.Add(fact);
        }
        private void P3Snapshot(JObject snapshot)
        {
            if(!P3)return;snapshot["p3_stage"]=true;
            foreach(var value in snapshot["players"]){var player=players[(string)value["player_id"]];value["life_generation"]=player.LifeGeneration;value["downed_generation"]=player.DownedGeneration;value["downed_remaining_ms"]=Math.Max(0,player.DownedUntil-now);value["rescue_target"]=player.RescueTarget;value["rescue_remaining_ms"]=player.RescueTarget==null?0:Math.Max(0,Number(parameters["player"],"rescue_ms")-(now-player.RescueAt));value["rescues"]=player.Rescues;value["protected_damage"]=player.ProtectedDamage;value["control_effect_ms"]=player.ControlEffect;}
        }
        private void P3Acknowledge(JObject receipt)
        {
            if(receipt["task_requirements"] is JObject updated){requirements.Clear();foreach(var property in updated.Properties())requirements[property.Name]=(JObject)property.Value.DeepClone();}
            reinforcementRemaining=Number(parameters["player"],"reinforcement_budget");
            if(!(receipt["reinforcements"] is JObject actions))return;
            foreach(var property in actions.Properties())
            {
                var action=property.Value;var status=(string)action["status"];if(status=="pending_spawn"||status=="spawned")reinforcementRemaining--;
                if(!players.TryGetValue((string)action["player_id"],out var player)||player.Left)continue;
                if(status=="canceled"&&player.Reinforcement==property.Name){player.Reinforcement=null;player.SpawnAwaitingAck=false;player.Life="dead";}
                if(status=="pending_spawn"&&player.Reinforcement==null&&player.Life=="dead"&&!Terminal){player.Reinforcement=property.Name;player.Life="awaiting_respawn";player.RespawnAt=now+Number(parameters["player"],"reinforcement_delay_ms");}
                if(status=="spawned"&&player.Reinforcement==property.Name&&player.SpawnAwaitingAck){player.Reinforcement=null;player.SpawnAwaitingAck=false;player.LifeGeneration++;player.HurtActions.Clear();player.Life="alive";player.Health=Number(parameters["player"],"reinforcement_health");player.Shield=0;player.Input=default;player.ReleaseRequired=true;player.InvulnerableUntil=now+Number(parameters["player"],"reinforcement_protection_ms");player.ProtectedUntil=player.InvulnerableUntil;if(V6)player.ReinforcementUntil=player.ProtectedUntil;Effect("reinforcement",player.ID,player.Room,player.Position);}
            }
        }
    }
}
