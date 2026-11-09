using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private bool p1;
        private DcP1World p1World;
        private DcP1Controls p1Controls;
        private DcP1View p1View;
        private JObject p1Snapshot;
        private double p1NextTick;
        private double p1NextSnapshot;
        private readonly Dictionary<string,string> p1ActionHashes=new Dictionary<string,string>();
        private readonly Dictionary<string,ulong> p1Pending=new Dictionary<string,ulong>();
        private readonly Queue<string> p1ActionOrder=new Queue<string>();
        private long p1SnapshotFrame=-1;
        private double p1NextPilotAction;
        private bool p1MenuWasOpen;
        private bool p1ResultConfirmed;

        private void ConfigureP1Input()
        {
            p1Controls=new DcP1Controls(weaponStage,v5Runtime);inputAsset=p1Controls.Asset;gameplayMap=inputAsset.FindActionMap("Gameplay");menuMap=inputAsset.FindActionMap("Menu");
        }
        private void ConfigureP1Client()
        {
            var content=Path.Combine(Application.dataPath,"..","Content");
            DcV6Profile.Expand(assignment);
            var geometryFile=DcV8Profile.Active?"world-v8-p5.json":DcV7Profile.Active?"world-v7-p5.json":DcV6Profile.Active?"world-v6-p5.json":v5Runtime?"world-v5-p4.json":p4Stage?"world-v4-p4.json":p2Stage?"world-v4-p2-1.json":"world-v3-p1.json";var parametersFile=DcV8Profile.Active?"parameters-v8-p5.json":DcV7Profile.Active?"parameters-v7-p5.json":DcV6Profile.Active?"parameters-v6-p5.json":v5Runtime?"parameters-v5-p4.json":p2Stage?"parameters-v4-p2-1.json":syncFoundation?"parameters-v4-s1.json":"parameters-v3-p1.json";
            if(FileHash(Path.Combine(content,geometryFile))!=(string)configuration["world"]["geometry_sha256"]||FileHash(Path.Combine(content,parametersFile))!=(string)configuration["parameters_sha256"])throw new InvalidDataException("P1 bundled content hash mismatch");
            if(Hash(System.Text.Encoding.UTF8.GetBytes(Canonical(assignment["p1_world"])))!=(string)assignment["p1_world_hash"])throw new InvalidDataException("P1 manifest world hash mismatch");
            if(p1View==null){p1View=gameObject.AddComponent<DcP1View>();p1View.Configure((JObject)assignment["p1_world"],playerID);p1View.AttachP5Controls(p1Controls);p1View.Exit=()=>Application.Quit();ConfigureP4();}
            if(syncFoundation){p1View.Foundation=true;p1View.Weapons=weaponStage;p1View.RoomCameraStage=p2Stage;ConfigureS1Client();}p1Controls.SetEnabled(!syncFoundation);if(statusLabel!=null)statusLabel.gameObject.SetActive(false);
            P5JoinReady();
            if(DcP5Settings.Active)p1Controls.MarkerRequested=point=>SendP5Marker(p1View.PointerWorld(point));
        }
        private void PrepareP1World()
        {
            p3FactFrontier.Clear();
            var allocation=(JObject)assignment.DeepClone();allocation["weapon_stage"]=weaponStage;allocation["p2_stage"]=p2Stage;allocation["p3_stage"]=p3Stage;allocation["p4_stage"]=p4Stage;allocation["parameters"]=assignment["p1_world"]["parameters"].DeepClone();p1World=new DcP1World(allocation);p1NextTick=Time.realtimeSinceStartupAsDouble;
        }
        private void P1Focus(bool focused)
        {
            if(role=="client")p1Controls?.SetEnabled(focused&&!p1Controls.Menu&&loadedConfirmed);
        }
        private void UpdateP1()
        {
            if(role=="server")
            {
                if(!running||p1World==null)return;
                var steps=0;
                while(Time.realtimeSinceStartupAsDouble>=p1NextTick&&steps++<4&&!p1World.Terminal){p1World.Tick(runClock.ElapsedMilliseconds);p1NextTick+=1d/30;}
                if(steps>4)p1NextTick=Time.realtimeSinceStartupAsDouble;
                foreach(var action in p1Pending.ToArray())
                {
                    var reason=p1World.Receipt(action.Key);if(reason=="pending")continue;
                    if(identities.ContainsKey(action.Value))Send("dc.action.receipt.v3",action.Value,new JObject { ["protocol_version"]=Protocol,["run_id"]=assignment["run_id"],["connection_generation"]=identities[action.Value]["connection_generation"],["action_id"]=action.Key,["status"]=reason=="accepted"||reason=="buffered"?"accepted":"rejected",["reason"]=reason,["effective_frame"]=p1World.WorldTick,["state_revision"]=p1World.WorldTick });
                    p1Pending.Remove(action.Key);
                }
                if(Time.realtimeSinceStartupAsDouble>=p1NextSnapshot)
                {
                    p1NextSnapshot=Time.realtimeSinceStartupAsDouble+0.1;
                    var snapshot=p1World.Snapshot();snapshot["protocol_version"]=3;snapshot["config_hash"]=configHash;snapshot["source_generation"]=assignment["source_generation"];snapshot["runtime_stage"]="p1_single_player";snapshot["result_confirmed"]=p1ResultConfirmed;
                    foreach(var identity in identities.ToArray()){snapshot["connection_generation"]=identity.Value["connection_generation"];Send("dc.p1.snapshot.v3",identity.Key,snapshot,NetworkDelivery.ReliableFragmentedSequenced);}
                }
                return;
            }
            if(!loadedConfirmed||p1View==null||!network.IsConnectedClient)return;
            var input=p1Controls.Read(sentSequence+1,p1View.Position,p1View.PointerWorld(p1Controls.Pointer));
            if(DcBootstrap.Argument("-dc-p1-autoplay","false")=="true"&&p1Snapshot!=null)input=DcP1Pilot.Decide(p1Snapshot,playerID,input.Sequence);
            var actions=p1Controls.Drain();
            if(Time.unscaledTime>=nextInput||actions.Length>0)
            {
                nextInput=Time.unscaledTime+1f/30;input.Sequence=++sentSequence;
                Send("dc.input.v3",NetworkManager.ServerClientId,new JObject { ["protocol_version"]=3,["message"]="dc.input.v3",["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["input_sequence"]=input.Sequence,["move_x"]=Mathf.RoundToInt(input.Move.x*1000),["move_y"]=Mathf.RoundToInt(input.Move.y*1000),["aim_u16"]=input.Aim,["ranged_held"]=input.Attack,["melee_held"]=input.Melee,["interact_held"]=input.Interact,["rescue_held"]=false },NetworkDelivery.UnreliableSequenced);
            }
            foreach(var kind in actions){p1View.InputStart(kind);SendP1Action(kind,input);}
            if(DcBootstrap.Argument("-dc-p1-autoplay","false")=="true"&&p1Snapshot!=null&&Time.realtimeSinceStartupAsDouble>=p1NextPilotAction){p1NextPilotAction=Time.realtimeSinceStartupAsDouble+0.25;var kind=DcP1Pilot.Action(p1Snapshot,playerID);if(kind!=null)SendP1Action(kind,input);}
            if(p1Controls.Menu)p1View.Status("菜单已打开，世界仍继续\n本阶段正式暂停尚未接入，Esc返回");
            else if(p1MenuWasOpen&&p1Snapshot?["outcome"]?.Type==JTokenType.Null)p1View.ClearStatus();
            p1MenuWasOpen=p1Controls.Menu;
        }
        private void SendP1Action(string kind,DcP1Input input)
        {
            var id=Guid.NewGuid().ToString();
            Send("dc.action.v3",NetworkManager.ServerClientId,new JObject { ["protocol_version"]=3,["message"]="dc.action.v3",["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["action_id"]=id,["operation_id"]=id,["action"]=kind,["input_sequence"]=input.Sequence,["aim_u16"]=input.Aim,["move_x"]=Mathf.RoundToInt(input.Move.x*1000),["move_y"]=Mathf.RoundToInt(input.Move.y*1000),["placement_x_milli"]=Mathf.RoundToInt(input.Placement.x*1000),["placement_y_milli"]=Mathf.RoundToInt(input.Placement.y*1000) });
        }
        private void P1Action(ulong connection,JObject value)
        {
            RequireFields(value,"protocol_version","message","run_id","source_generation","connection_generation","action_id","operation_id","action","input_sequence","aim_u16","move_x","move_y","placement_x_milli","placement_y_milli");ValidateMessage(connection,value,WireName("dc.action.v3"));
            if(p1World==null||!running)throw new InvalidDataException("Not running");
            var id=(string)value["action_id"];if(!Guid.TryParse(id,out _)||id!=(string)value["operation_id"])throw new InvalidDataException("Action identity");
            var digest=Hash(System.Text.Encoding.UTF8.GetBytes(Canonical(value)));var key=identities[connection]["player_id"]+":"+id;
            if(p1ActionHashes.TryGetValue(key,out var old)&&old!=digest)throw new InvalidDataException("Action conflict");
            if(!p1ActionHashes.ContainsKey(key)){p1ActionHashes[key]=digest;p1ActionOrder.Enqueue(key);while(p1ActionOrder.Count>512)p1ActionHashes.Remove(p1ActionOrder.Dequeue());}
            if(p1Pending.Count>=32)throw new InvalidDataException("Action queue");
            var horizontal=Integer(value,"move_x",-1000,1000);var vertical=Integer(value,"move_y",-1000,1000);if(horizontal*horizontal+vertical*vertical>1002001)throw new InvalidDataException("Action movement");
            var intent=new DcP1Input {Sequence=(uint)Integer(value,"input_sequence",0,uint.MaxValue),Move=new Vector2(horizontal/1000f,vertical/1000f),Aim=(ushort)Integer(value,"aim_u16",0,65535),Placement=new Vector2(Integer(value,"placement_x_milli",-20000,20000)/1000f,Integer(value,"placement_y_milli",-20000,20000)/1000f),HasPlacement=true};
            p1World.QueueAction((string)identities[connection]["player_id"],(string)value["action"],intent,id);p1Pending[id]=connection;
        }
        private void P1Snapshot(ulong connection,Unity.Netcode.FastBufferReader reader)
        {
            try
            {
                var value=Read(reader,60000);
                if((int)value["protocol_version"]!=3||(string)value["run_id"]!=runID||(string)value["config_hash"]!=configHash||(long)value["source_generation"]!=sourceGeneration||(long)value["connection_generation"]!=connectionGeneration)throw new InvalidDataException("P1 snapshot binding");
                if((long)value["world_frame_id"]<=p1SnapshotFrame)return;p1SnapshotFrame=(long)value["world_frame_id"];p1Snapshot=value;p1View.Receive(value);
                var own=((JArray)value["players"]).First(item=>(string)item["player_id"]==playerID);
                var output=DcBootstrap.Argument("-dc-p1-evidence");
                if(!string.IsNullOrEmpty(output)&&Path.GetFullPath(output).StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))File.WriteAllText(output,new JObject { ["run_id"]=runID,["protocol_version"]=3,["input_ack"]=own["last_input_sequence"],["world_frame_id"]=value["world_frame_id"],["damage"]=own["damage"],["magazine"]=own["magazine"],["light_attacks"]=own["light_attacks"],["heavy_attacks"]=own["heavy_attacks"],["knockdowns"]=own["knockdowns"],["outcome"]=value["outcome"],["contexts"]=6,["real_udp_world"]=true,["no_rewards"]=true }.ToString());
            }
            catch(Exception){p1View?.Status("同步验证失败，请关闭游戏后重新启动");network.Shutdown();}
        }
        private async Task ConfirmP1Result(long elapsed)
        {
            var snapshot=p1World.Snapshot();var reason=(string)snapshot["reason"];var outcome=(string)snapshot["outcome"]=="success"?"success":reason=="p1_incapacitated"?reason:reason=="timeout"?reason:"technical_failure";
            await Internal("runs/"+assignment["run_id"]+"/p1-result",new JObject { ["operation_id"]=Guid.NewGuid().ToString(),["assignment_id"]=assignment["assignment_id"],["source_generation"]=assignment["source_generation"],["control_version"]=controlVersion,["outcome"]=outcome,["world_frame_id"]=p1World.WorldTick,["elapsed_ms"]=Math.Min(elapsed,1800000),["completed_rooms"]=snapshot["completed"],["summary_hash"]=Hash(System.Text.Encoding.UTF8.GetBytes(Canonical(p1World.Summary()))) });
            p1ResultConfirmed=true;snapshot["protocol_version"]=3;snapshot["config_hash"]=configHash;snapshot["source_generation"]=assignment["source_generation"];snapshot["runtime_stage"]="p1_single_player";snapshot["result_confirmed"]=true;snapshot["world_frame_id"]=p1World.WorldTick+1;
            if(syncFoundation){snapshot["protocol_version"]=4;snapshot["runtime_stage"]=RuntimeStage;snapshot["state_revision"]=++s1StateRevision;foreach(var identity in identities)SendS1Baseline(identity.Key,snapshot,identity.Value);return;}
            foreach(var identity in identities.ToArray()){snapshot["connection_generation"]=identity.Value["connection_generation"];Send("dc.p1.snapshot.v3",identity.Key,snapshot,NetworkDelivery.ReliableFragmentedSequenced);}
        }
    }

    public static class DcP1Pilot
    {
        public static DcP1Input Decide(JObject snapshot,string id,uint sequence)
        {
            var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==id);var room=(int)own["room_id"];var position=new Vector2((float)own["x"],(float)own["y"]);var target=((JArray)snapshot["enemies"]).Where(item=>(int)item["room_id"]==room).OrderBy(item=>Vector2.Distance(position,new Vector2((float)item["x"],(float)item["y"]))).FirstOrDefault();
            var destination=target==null?new Vector2(16.6f,5):new Vector2(9,5)+new Vector2(Mathf.Cos((long)snapshot["elapsed_ms"]/900f)*5,Mathf.Sin((long)snapshot["elapsed_ms"]/900f)*2.8f);var aim=target==null?Vector2.right:new Vector2((float)target["x"],(float)target["y"])-position;
            var state=snapshot["rooms"][room];var interacting=target==null&&(room==2||room==3)&&!(bool)state["mechanism_complete"];
            if(interacting)destination=room==2?new Vector2(11,5):new Vector2(9,5);
            var radians=Mathf.Atan2(aim.y,aim.x);if(radians<0)radians+=Mathf.PI*2;
            return new DcP1Input {Sequence=sequence,Move=Vector2.Distance(position,destination)<0.4f?(interacting?Vector2.zero:Vector2.right):(destination-position).normalized,Aim=(ushort)(radians/(Mathf.PI*2)*65535),Attack=target!=null,Interact=interacting&&Vector2.Distance(position,destination)<0.8f,Placement=position+Vector2.up,HasPlacement=true};
        }
        public static string Action(JObject snapshot,string id)
        {
            var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==id);
            if((int)own["health"]<=0)return null;
            if((long)own["ability_remaining_ms"]==0)return "ability_primary";
            if((long)own["secondary_remaining_ms"]==0)return "ability_secondary";
            return null;
        }
    }
}
