using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private sealed class GrowthChoice
        {
            public int Node;
            public int Revision=1;
            public long Opened;
            public long Deadline;
            public string[] Options;
            public string Hash;
            public string Selected;
            public bool Automatic;
            public long InstalledAt;
        }
        public bool P4 {get;private set;}
        private int P4Number(string key,int fallback)=>Number(manifest["config"]?["p4"],key,fallback);
        private GrowthChoice PendingChoice(Player player)=>player.Growth.FirstOrDefault(item=>item.Selected==null);
        private static string GrowthHash(string value)
        {
            using var hash=SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
        }
        private string[] GrowthCandidates(Player player,int node)=>upgrades.Values.Where(item=>((string)item["scope"]=="common"||(string)item["scope"]==player.Loadout)&&!player.Upgrades.Contains((string)item["id"]))
            .Select(item=>(string)item["id"]).OrderBy(id=>GrowthHash(Convert.ToString(manifest["seed"],CultureInfo.InvariantCulture)+":growth_"+(node+1)+":"+player.Loadout+":"+id),StringComparer.Ordinal).Take(3).ToArray();
        private void RefreshGrowth(Player player,GrowthChoice choice,bool revise)
        {
            var options=GrowthCandidates(player,choice.Node);
            if(options.Length!=3){AbortTechnical();return;}
            if(revise&&!options.SequenceEqual(choice.Options??Array.Empty<string>()))choice.Revision++;
            choice.Options=options;choice.Hash=GrowthHash(string.Join("|",options));
        }
        private void OpenGrowth()
        {
            if(!P4||Practice||Terminal)return;
            var nodes=manifest["config"]?["p4"]?["node_rooms"] is JArray configuredNodes?configuredNodes.Select(item=>(int)item).ToArray():new[]{1,3,4};
            for(var node=0;node<nodes.Length;node++)
            {
                var room=rooms[nodes[node]];
                if(room.Status!="cleared"||room.NodeOpened)continue;
                room.NodeOpened=true;
                foreach(var player in players.Values.Where(item=>!item.Left).OrderBy(item=>item.Slot))
                {
                    var choice=new GrowthChoice{Node=node,Opened=now,Deadline=now+P4Number("choice_timeout_game_ms",60000)};
                    RefreshGrowth(player,choice,false);player.Growth.Add(choice);
                    Effect("growth_available",player.ID,player.Room,player.Position);
                }
            }
        }
        private void TickGrowth()
        {
            if(!P4||Terminal)return;
            foreach(var player in players.Values.Where(item=>!item.Left).OrderBy(item=>item.Slot))
            {
                GrowthChoice choice;
                while((choice=PendingChoice(player))!=null&&now>=choice.Deadline)
                {
                    InstallGrowth(player,choice,choice.Options[0],true,NextID("growth_default"));
                    if(Terminal)return;
                }
            }
        }
        private bool ChooseGrowth(Player player,DcP1Input intent,string action)
        {
            var choice=PendingChoice(player);LastActionReason="stale_choice";
            if(!P4||Terminal||player.Left||!player.Connected||choice==null||now>=choice.Deadline||intent.ChoiceNode!=choice.Node||intent.ChoiceRevision!=choice.Revision||intent.ChoiceHash!=choice.Hash||!choice.Options.Contains(intent.ChoiceOption))return false;
            InstallGrowth(player,choice,intent.ChoiceOption,false,action);LastActionReason="accepted";return true;
        }
        private void InstallGrowth(Player player,GrowthChoice choice,string option,bool automatic,string action)
        {
            if(choice.Selected!=null||player.Upgrades.Contains(option)){AbortTechnical();return;}
            var fact=Fact("growth_choice",player.ID,option,action,0);
            fact["proof"]=new JObject{["node_id"]="growth_"+(choice.Node+1),["candidate_revision"]=choice.Revision,["candidate_hash"]=choice.Hash,["opened_game_ms"]=choice.Opened,["deadline_game_ms"]=choice.Deadline,["automatic"]=automatic};
            facts.Add(fact);choice.Selected=option;choice.Automatic=automatic;choice.InstalledAt=now;
            player.Upgrades.Add(option);player.Choices[choice.Node]=option;
            if(option=="vitality"&&player.Health>0)player.Health=Math.Min(MaxHealth(player),player.Health+20);
            foreach(var pending in player.Growth.Where(item=>item.Selected==null))RefreshGrowth(player,pending,true);
            Effect("growth_installed",player.ID,player.Room,player.Position);
        }
        private int MaxHealth(Player player)=>Number(parameters["player"],"max_health")+(Has(player,"vitality")?20:0);
        private bool TickP4Supply(Player player)
        {
            if(!P4||player.Room!=7||rooms[7].Status!="cleared")return false;
            if(!player.Input.Interact||player.Input.Move.sqrMagnitude>0||player.Input.Attack||player.Input.Melee||player.Charging||!player.Connected||player.Health<=0||now<player.DodgeUntil||now<player.ProtectedUntil||player.LastCombatAt==now||player.HurtAt==now)return true;
            var point=V6?DcV6Profile.SupplyPoint(Layout(7)):new Vector2(15,6);
            if(Vector2.Distance(player.Position,point)>Distance(config["player"],"interaction_radius_milli_tiles")||!ClearLine(Layout(7),player.Position,point))return true;
            if(player.Claims.Contains("branch_supply")||player.Health>=MaxHealth(player))return true;
            player.Claims.Add("branch_supply");player.Health=Math.Min(MaxHealth(player),player.Health+P4Number("supply_health",30));
            Effect("supply",player.ID,player.Room,point);return true;
        }
        private void TriggerP4Shield(Player player)
        {
            if(!P4||player.Left||!player.Connected||player.Health<=0||player.PendingShield<=0||player.Room==6||rooms[player.Room].Status!="active"||now<player.ProtectedUntil)return;
            GrantWeaponShield(player,player.PendingShield,P4Number("branch_shield_duration_ms",30000));player.PendingShield=0;
        }
        private void P4Snapshot(JObject snapshot)
        {
            if(!P4)return;
            snapshot["p4_stage"]=true;
            foreach(var item in snapshot["rooms"])item["variant_id"]=Layout((int)item["index"])["variant_id"];
            foreach(var item in snapshot["players"])
            {
                var player=players[(string)item["player_id"]];var pending=PendingChoice(player);
                item["max_health"]=MaxHealth(player);item["pending_shield"]=player.PendingShield;item["branch_supply_used"]=player.Claims.Contains("branch_supply");
                item["growth"]=new JArray(player.Growth.Select(choice=>new JObject{["node_index"]=choice.Node,["revision"]=choice.Revision,["candidate_hash"]=choice.Hash,["opened_game_ms"]=choice.Opened,["deadline_game_ms"]=choice.Deadline,["remaining_ms"]=Math.Max(0,choice.Deadline-now),["candidates"]=new JArray(choice.Options),["selected"]=choice.Selected,["automatic"]=choice.Automatic,["installed_game_ms"]=choice.InstalledAt}));
                item["candidates"]=pending==null||player.Left?new JArray():new JArray(pending.Options);
                item["choice_node"]=pending==null||player.Left?-1:pending.Node;
                item["choice_revision"]=pending?.Revision??0;item["choice_hash"]=pending?.Hash;
                item["choice_remaining_ms"]=pending==null?0:Math.Max(0,pending.Deadline-now);
            }
        }
    }
}
