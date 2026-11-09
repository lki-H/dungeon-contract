using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcP2NetworkProbe:MonoBehaviour
    {
        public DcP0V3 Runtime;
        private double started=-1;
        private long connection;
        private long frame;
        private bool disconnected;
        private bool separate;
        private int slot;
        private int combatFrames;
        private int maximumEnemies;
        private int maximumProjectiles;
        private bool ordinaryAttackEpisode;
        private double skillAt;
        private double dodgeAt;
        private void Update()
        {
            if(Runtime==null)return;
            if(started>=0&&Time.realtimeSinceStartupAsDouble-started>int.Parse(DcBootstrap.Argument("-dc-p2-probe-duration","50"))){Write();Application.Quit(0);return;}
            if(!Runtime.ProbeReady)return;
            var own=Runtime.ProbeSnapshot["players"].First(item=>(string)item["player_id"]==Runtime.ProbePlayerID);
            if(own["attack_episode_id"]?.Type==JTokenType.String||Runtime.ProbeOrdinaryActionObserved)ordinaryAttackEpisode=true;
            if(started<0){started=Time.realtimeSinceStartupAsDouble;connection=Runtime.ProbeConnection;frame=(long)Runtime.ProbeSnapshot["world_frame_id"];slot=int.Parse(DcBootstrap.Argument("-dc-p2-probe-slot","0"));}
            var elapsed=Time.realtimeSinceStartupAsDouble-started;
            if(Runtime.ProbeSnapshot["team_rooms"] is JArray team&&team.Any(item=>(int)item["room_id"]!=(int)own["room_id"]))separate=true;
            var moveAt=slot==0?0:10;var moving=elapsed>=moveAt&&elapsed<moveAt+5;
            Runtime.ProbeIntent(moving?(DcV7Profile.Active?Runtime.ProbeRightDoorMove():Vector2.right):Vector2.zero,false);
            var enemyCount=((JArray)Runtime.ProbeSnapshot["enemies"]).Count;maximumEnemies=Math.Max(maximumEnemies,enemyCount);maximumProjectiles=Math.Max(maximumProjectiles,((JArray)Runtime.ProbeSnapshot["projectiles"]).Count);if(enemyCount>0)combatFrames++;
            var inBattle=(string)Runtime.ProbeSnapshot["rooms"][(int)own["room_id"]]["status"]=="active";
            if(DcBootstrap.Argument("-dc-p2-combat-pressure","false")=="true"&&(elapsed>moveAt+5||inBattle)){Runtime.ProbeCombat();if(ordinaryAttackEpisode&&enemyCount>0&&elapsed>=skillAt){skillAt=elapsed+0.5;if((long?)own["ability_remaining_ms"]==0)Runtime.ProbeWeaponAction("ability_primary");if((long?)own["secondary_remaining_ms"]==0)Runtime.ProbeWeaponAction("ability_secondary");}if(elapsed>=dodgeAt&&(long?)own["dodge_remaining_ms"]==0&&DcP21Pilot.Dodge(Runtime.ProbeSnapshot,Runtime.ProbePlayerID)){dodgeAt=elapsed+0.5;Runtime.ProbeWeaponAction("dodge");}}
            if(slot==0&&!disconnected&&elapsed>7){disconnected=true;Runtime.ProbeDisconnect();}
        }
        private void Write()
        {
            var target=Path.GetFullPath(DcBootstrap.Argument("-dc-p2-probe-output"));if(!target.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Probe output scope");
            var snapshot=Runtime.ProbeSnapshot;var own=snapshot?["players"]?.FirstOrDefault(item=>(string)item["player_id"]==Runtime.ProbePlayerID);
            // Final probe position distinguishes a transport failure from a driver
            // that never reached the intended combat room. No normal-run file writes.
            var position=new JObject{["room_id"]=own?["room_id"],["x"]=own?["x"],["y"]=own?["y"],["body_center_x"]=own?["body_center_x"],["body_center_y"]=own?["body_center_y"]};
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(target),"probe-position-"+slot+".json"),position.ToString());
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(target),"probe-network-events-"+slot+".json"),Runtime.ProbeNetworkEvents.ToString());
                        File.WriteAllText(target,new JObject{["run_id"]=snapshot?["run_id"],["slot"]=slot,["input_ack"]=(long?)own?["last_input_sequence"]??0,["dynamic_packets"]=Runtime.ProbeDynamicPackets,["max_dynamic_bytes"]=Runtime.ProbeDynamicBytes,["max_baseline_bytes"]=Runtime.ProbeBaselineBytes,["separate_rooms_observed"]=separate,["world_continued"]=snapshot!=null&&(long)snapshot["world_frame_id"]>frame+30,["reconnect_generation_changed"]=Runtime.ProbeConnection>connection,["ordinary_attack_episode_observed"]=ordinaryAttackEpisode,["combat_observed_frames"]=combatFrames,["max_enemies"]=maximumEnemies,["max_projectiles"]=maximumProjectiles,["health"]=own?["health"],["damage"]=own?["damage"],["service_tick_mean_ms"]=snapshot?["service_tick_mean_ms"],["service_tick_max_ms"]=snapshot?["service_tick_max_ms"],["dynamic_sent_bytes"]=snapshot?["dynamic_sent_bytes"],["baseline_sent_bytes"]=snapshot?["baseline_sent_bytes"],["game_elapsed_ms"]=snapshot?["elapsed_ms"],["real_udp"]=true,["human"]=false}.ToString());
        }
    }
}
