using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private uint s2LastAction;
        private string s2Press;
        private bool s2SwitchPending; private bool v8ProbeChargeHeld; private string v8ProbeLastID,v8ProbeLastKind,v8ProbeLastReason; public string ProbeLastActionReason=>v8ProbeLastReason; public string ProbeLastActionKind=>v8ProbeLastKind;
        private string s2SwitchActionID;
        private readonly Dictionary<string,JObject> s2Requests=new Dictionary<string,JObject>();
        private readonly Dictionary<string,string> s2RequestHashes=new Dictionary<string,string>();
        private readonly Dictionary<string,JObject> s2Receipts=new Dictionary<string,JObject>();
        private readonly Queue<string> s2RequestOrder=new Queue<string>();
        private readonly Dictionary<string,ulong> s2Pending=new Dictionary<string,ulong>();
        private readonly Dictionary<ulong,uint> s2ActionSequences=new Dictionary<ulong,uint>();

        private void UpdateS2Input(DcP1Input input,string[] actions)
        {
            if(p1Snapshot==null||string.IsNullOrEmpty(input.WeaponID))return;
            if(v5Runtime&&(p2ProbeCombat||DcBootstrap.Argument("-dc-p2-autoplay","false")=="true"))
            {
                actions=DcV5AutoInput.Compose(ref v5AutoHeld,s2SwitchPending,ref input,actions);
            }
            input.Sequence=sentSequence;
            foreach(var action in actions)
            {
                if(s2SwitchPending&&action!="cancel_input"&&action!="attack_release")continue;
                p1View.InputStart(action);SendS2Action(action,input);
            }
            if(Time.unscaledTime<nextInput&&actions.Length==0)return;
            nextInput=Time.unscaledTime+1f/30;input.Sequence=++sentSequence;
            var packet=new JObject{["protocol_version"]=Protocol,["message"]=WireName("dc.input.v3"),["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["input_sequence"]=input.Sequence,["action_watermark"]=s2LastAction,["weapon_id"]=input.WeaponID,["weapon_revision"]=input.WeaponRevision,["move_x"]=Mathf.RoundToInt(input.Move.x*1000),["move_y"]=Mathf.RoundToInt(input.Move.y*1000),["aim_u16"]=input.Aim,["attack_held"]=input.Attack||input.Melee,["interact_held"]=input.Interact};
            if(v5Runtime){packet["charge_held"]=input.Charge;packet["attack_episode_id"]=s2Press;packet["charge_episode_id"]=v5ChargePress;}
            Send("dc.input.v3",NetworkManager.ServerClientId,packet,NetworkDelivery.UnreliableSequenced);
        }
        private void SendS2Action(string kind,DcP1Input input)
        {
            var id=Guid.NewGuid().ToString();
            var own=(JObject)((JArray)p1Snapshot["players"]).First(item=>(string)item["player_id"]==playerID);
            var kit=((JArray)assignment["p1_world"]["parameters"]["loadouts"]).First(item=>(string)item["id"]==(string)own["loadout_id"]);
            if(kind=="attack_press")s2Press=id;
            if(kind=="charge_begin")v5ChargePress=id;
            if(DcV8Profile.Active&&DcBootstrap.Argument("-dc-s2-probe","false")=="true"){v8ProbeLastID=id;v8ProbeLastKind=kind;v8ProbeLastReason=null;}
            var request=new JObject{["protocol_version"]=Protocol,["message"]=WireName("dc.action.v3"),["run_id"]=runID,["source_generation"]=sourceGeneration,["connection_generation"]=connectionGeneration,["operation_id"]=id,["action_id"]=id,["input_sequence"]=Math.Max(1,input.Sequence),["action_sequence"]=++s2LastAction,["weapon_id"]=p1Controls.ActiveWeapon,["weapon_revision"]=p1Controls.WeaponRevision,["requested_weapon_id"]=kind=="switch_weapon"?kit[p1Controls.MeleeEquipped?"ranged":"melee"]["weapon"]:null,["action"]=kind,["press_action_id"]=kind=="attack_release"?s2Press:null,["aim_u16"]=input.Aim,["move_x"]=Mathf.RoundToInt(input.Move.x*1000),["move_y"]=Mathf.RoundToInt(input.Move.y*1000)};
            if(p4Stage){var pending=own["candidates"] as JArray;request["choice_node"]=kind=="growth_choice"?own["choice_node"]:new JValue(-1);request["choice_revision"]=kind=="growth_choice"?own["choice_revision"]:new JValue(0);request["choice_hash"]=kind=="growth_choice"?own["choice_hash"]:JValue.CreateNull();request["choice_option"]=kind=="growth_choice"&&pending?.Count>0?pending[p4ChoiceIndex]:JValue.CreateNull();}
            if(v5Runtime&&(kind=="charge_release"||kind=="charge_cancel"))request["press_action_id"]=v5ChargePress;
            if(kind=="switch_weapon"){s2SwitchPending=true;s2SwitchActionID=id;}
            if(DcV7Profile.Active)p1View.V7ActionPreview(kind,id,input);
            Send("dc.action.v3",NetworkManager.ServerClientId,request);
        }
        private DcP1Input S2Intent(JObject value)=>new DcP1Input{Sequence=(uint)Integer(value,"input_sequence",1,uint.MaxValue),Move=new Vector2(Integer(value,"move_x",-1000,1000)/1000f,Integer(value,"move_y",-1000,1000)/1000f),Aim=(ushort)Integer(value,"aim_u16",0,65535),WeaponID=(string)value["weapon_id"],WeaponRevision=(uint)Integer(value,"weapon_revision",1,uint.MaxValue)};
        private void S2InputMessage(ulong connection,JObject value)
        {
            var expected=new[]{"protocol_version","message","run_id","source_generation","connection_generation","input_sequence","action_watermark","weapon_id","weapon_revision","move_x","move_y","aim_u16","attack_held","interact_held"};
            RequireFields(value,v5Runtime?expected.Concat(new[]{"charge_held","attack_episode_id","charge_episode_id"}).ToArray():expected);
            ValidateMessage(connection,value,WireName("dc.input.v3"));var intent=S2Intent(value);
            if(intent.Move.sqrMagnitude>1.002f||value["attack_held"].Type!=JTokenType.Boolean||value["interact_held"].Type!=JTokenType.Boolean)throw new InvalidOperationException("S2 input bounds");
            if(sequences.TryGetValue(connection,out var previous)&&intent.Sequence<=previous)return;
            intent.ActionWatermark=(uint)Integer(value,"action_watermark",0,uint.MaxValue);
            var player=(string)identities[connection]["player_id"];var melee=p1World.MeleeEquipped(player);
            intent.Attack=(bool)value["attack_held"]&&!melee;intent.Melee=(bool)value["attack_held"]&&melee;intent.Interact=(bool)value["interact_held"];
            if(v5Runtime){if(value["charge_held"].Type!=JTokenType.Boolean)throw new InvalidOperationException("V5 charge bounds");intent.Charge=(bool)value["charge_held"];intent.AttackEpisode=V5Episode(value["attack_episode_id"]);intent.ChargeEpisode=V5Episode(value["charge_episode_id"]);}
            sequences[connection]=intent.Sequence;p1World.QueueFoundationInput(player,intent);
        }
        private void S2ActionMessage(ulong connection,JObject value)
        {
            var expectedFields=new[]{"protocol_version","message","run_id","source_generation","connection_generation","operation_id","action_id","input_sequence","action_sequence","weapon_id","weapon_revision","requested_weapon_id","action","press_action_id","aim_u16","move_x","move_y"};
            RequireFields(value,p4Stage?expectedFields.Concat(new[]{"choice_node","choice_revision","choice_hash","choice_option"}).ToArray():expectedFields);
            ValidateMessage(connection,value,WireName("dc.action.v3"));
            var id=(string)value["action_id"];if(!Guid.TryParse(id,out _)||id!=(string)value["operation_id"])throw new InvalidOperationException("S2 action identity");
            var key=identities[connection]["player_id"]+":"+identities[connection]["connection_generation"]+":"+id;var hash=Hash(System.Text.Encoding.UTF8.GetBytes(Canonical(value)));
            if(s2RequestHashes.TryGetValue(key,out var prior))
            {
                if(prior!=hash)throw new InvalidOperationException("S2 action conflict");
                if(s2Receipts.TryGetValue(key,out var receipt))Send("dc.action.receipt.v3",connection,receipt);
                return;
            }
            var intent=S2Intent(value);if(intent.Move.sqrMagnitude>1.002f)throw new InvalidOperationException("S2 movement bounds");
            intent.ActionSequence=(uint)Integer(value,"action_sequence",1,uint.MaxValue);intent.PressActionID=(string)value["press_action_id"];
            var player=(string)identities[connection]["player_id"];
            if(intent.ActionSequence!=(s2ActionSequences.TryGetValue(connection,out var sequence)?sequence:p1World.WeaponActionSequence(player))+1)throw new InvalidOperationException("S2 action gap");
            if(s2Pending.Count>=32)throw new InvalidOperationException("S2 pending budget");
            var kind=(string)value["action"];
            if(p4Stage&&kind!="growth_choice"&&(Integer(value,"choice_node",-1,-1)!=-1||Integer(value,"choice_revision",0,0)!=0||value["choice_hash"].Type!=JTokenType.Null||value["choice_option"].Type!=JTokenType.Null))throw new InvalidOperationException("P4 unrelated choice fields");
            if(kind=="growth_choice"&&p4Stage){intent.ChoiceNode=(int)Integer(value,"choice_node",0,2);intent.ChoiceRevision=(int)Integer(value,"choice_revision",1,3);intent.ChoiceHash=(string)value["choice_hash"];intent.ChoiceOption=(string)value["choice_option"];if(intent.ChoiceHash?.Length!=64||intent.ChoiceOption?.Length>64)throw new InvalidOperationException("P4 choice bounds");}
            else if(!new[]{"switch_weapon","attack_press","attack_release","attack_cancel","cancel_input","dodge","reload","ability_primary","ability_secondary","interact_begin","interact_cancel"}.Contains(kind)&&!(v5Runtime&&new[]{"charge_begin","charge_release","charge_cancel"}.Contains(kind)))throw new InvalidOperationException("S2 action enum");
            if(kind!="switch_weapon"&&value["requested_weapon_id"].Type!=JTokenType.Null)throw new InvalidOperationException("S2 requested weapon");
            if(kind!="attack_release"&&!(v5Runtime&&(kind=="charge_release"||kind=="charge_cancel"))&&value["press_action_id"].Type!=JTokenType.Null)throw new InvalidOperationException("S2 press reference");
            // A delayed request can name the former weapon. Validate its frozen pair
            // here; the authoritative action queue rejects a stale revision normally.
            if(kind=="switch_weapon"&&!p1World.ValidSwitchPair(player,intent.WeaponID,(string)value["requested_weapon_id"]))throw new InvalidOperationException("S2 switch target");
            s2ActionSequences[connection]=intent.ActionSequence;s2RequestHashes[key]=hash;s2Requests[key]=value;s2Pending[key]=connection;s2RequestOrder.Enqueue(key);
            while(s2RequestOrder.Count>512){var expired=s2RequestOrder.Dequeue();s2RequestHashes.Remove(expired);s2Requests.Remove(expired);s2Receipts.Remove(expired);}
            if(s1Paused||p2Stage&&s1Waiting)p1World.RejectWeaponAction(player,intent,id,s1Paused?"paused":"technical_wait");else p1World.QueueAction(player,kind,intent,id);
        }
        private void FlushS2Receipts()
        {
            foreach(var item in s2Pending.ToArray())
            {
                var request=s2Requests[item.Key];var reason=p1World.Receipt((string)request["action_id"]);if(reason=="pending")continue;
                if(reason=="buffered"&&s2Receipts.ContainsKey(item.Key))continue;
                if(!identities.TryGetValue(item.Value,out var identity)){s2Pending.Remove(item.Key);continue;}
                var player=(string)identity["player_id"];var accepted=reason=="accepted";
                var receipt=new JObject{["protocol_version"]=Protocol,["message"]=WireName("dc.action.receipt.v3"),["run_id"]=assignment["run_id"],["source_generation"]=sourceGeneration,["connection_generation"]=identity["connection_generation"],["operation_id"]=request["operation_id"],["action_id"]=request["action_id"],["action_sequence"]=request["action_sequence"],["status"]=accepted?"accepted":reason=="buffered"?"buffered":"rejected",["reason"]=reason,["effective_frame"]=accepted?new JValue(p1World.WorldTick):JValue.CreateNull(),["weapon_id"]=p1World.ActiveWeapon(player),["weapon_revision"]=p1World.WeaponRevision(player),["release_required"]=p1World.RequiresRelease(player)};
                s2Receipts[item.Key]=receipt;Send("dc.action.receipt.v3",item.Value,receipt);if(reason!="buffered")s2Pending.Remove(item.Key);
            }
        }
        private void S2Receipt(JObject value)
        {
            if((string)value["run_id"]!=runID||(long)value["source_generation"]!=sourceGeneration||(long)value["connection_generation"]!=connectionGeneration)return;
            if(!string.IsNullOrEmpty(s2Press)&&(string)value["action_id"]==s2Press&&(string)value["status"]=="accepted")probeOrdinaryRun=runID;
            if(DcV7Profile.Active)p1View.V7ReceiptPreview((string)value["action_id"],(string)value["status"]=="rejected");
            if(DcV8Profile.Active&&(string)value["action_id"]==v8ProbeLastID)v8ProbeLastReason=(string)value["reason"];
            // A newer baseline may precede this receipt. Its state stays newer,
            // but the matching request has still finished and must release input.
            if((string)value["action_id"]==s2SwitchActionID){s2SwitchPending=false;s2SwitchActionID=null;}
            var id=(string)value["weapon_id"];var revision=(uint)value["weapon_revision"];
            if(revision<p1Controls.WeaponRevision)return;
            var melee=id=="ruin_blade"||id=="rune_hammer";p1Controls.WeaponState(id,revision,melee);
            if(p1Snapshot!=null){var own=((JArray)p1Snapshot["players"]).First(item=>(string)item["player_id"]==playerID);if((uint)own["weapon_revision"]<revision){own["charging"]=false;own["charge_ms"]=0;if(v5Runtime)own["melee_action"]=null;}own["active_weapon_id"]=id;own["weapon_revision"]=revision;own["melee_equipped"]=melee;}
            p1View.Feedback((string)value["reason"]);
        }
        private void PreserveLatestWeapon(JObject member)
        {
            if((uint)member["weapon_revision"]>=p1Controls.WeaponRevision)return;
            member["active_weapon_id"]=p1Controls.ActiveWeapon;member["weapon_revision"]=p1Controls.WeaponRevision;member["melee_equipped"]=p1Controls.MeleeEquipped;member["charging"]=false;member["charge_ms"]=0;
            if(v5Runtime)member["melee_action"]=null;
        }
        private static string V5Episode(JToken token)
        {
            if(token.Type==JTokenType.Null)return null;
            var value=(string)token;if(!Guid.TryParse(value,out _))throw new InvalidOperationException("V5 input episode");return value;
        }
        public void ProbeWeaponAction(string kind)
        {
            if(s2SwitchPending&&kind!="cancel_input"&&kind!="attack_release")return;
            if(DcV8Profile.Active&&DcBootstrap.Argument("-dc-s2-probe","false")=="true"){if(kind=="charge_begin")v8ProbeChargeHeld=true;else if(kind=="charge_release"||kind=="charge_cancel"||kind=="switch_weapon"||kind=="cancel_input")v8ProbeChargeHeld=false;}
            var input=p2ProbeCombat&&p1Snapshot!=null?DcP21Pilot.Decide(p1Snapshot,p2PilotGeometry,Math.Max(1,sentSequence),playerID):p1Controls.Read(Math.Max(1,sentSequence),p1View.AimOrigin,p1View.PointerWorld(p1Controls.Pointer));SendS2Action(kind,input);
        }
    }
    // Explicit synthetic input only: a blocked switch must not consume a press.
    public static class DcV5AutoInput
    {
        public static string[] Compose(ref bool previous,bool switchPending,ref DcP1Input input,string[] actions)
        {
            if(switchPending){input.Attack=false;input.Melee=false;}
            var held=input.Attack||input.Melee;var pending=actions.ToList();
            if(held&&!previous)pending.Add("attack_press");else if(!held&&previous)pending.Add("attack_release");
            previous=held;return pending.ToArray();
        }
    }

}
