using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcV5Probe : MonoBehaviour
    {
        public DcP0V3 Runtime;
        private double started=-1;
        private double changed;
        private int step;
        private uint revision;
        private JObject frozen;
        private readonly JObject result=new JObject{["protocol_version"]=DcV8Profile.Active?8:DcV7Profile.Active?7:DcV6Profile.Active?6:5,["real_player"]=true,["human"]=false};
        private JToken Own()=>Runtime.ProbeSnapshot["players"].First(item=>(string)item["player_id"]==Runtime.ProbePlayerID);
        private bool Hammer=>(string)Own()["active_weapon_id"]=="rune_hammer";
        private async void Update()
        {
            if(Runtime==null||!Runtime.ProbeReady)return;
            if(started<0){started=Time.realtimeSinceStartupAsDouble;revision=(uint)Own()["weapon_revision"];Runtime.ProbeWeaponAction("switch_weapon");step=1;changed=started;}
            try
            {
                if(step==1&&(bool)Own()["melee_equipped"]&&(uint)Own()["weapon_revision"]>revision){result["single_weapon_switch"]=true;changed=Time.realtimeSinceStartupAsDouble;step=2;}
                if(step==2&&Time.realtimeSinceStartupAsDouble-changed>0.3){Runtime.ProbeWeaponAction(Hammer?"charge_begin":"attack_press");if(!Hammer)Runtime.ProbeWeaponAction("attack_release");changed=Time.realtimeSinceStartupAsDouble;step=3;}
                if(step==3&&Hammer&&(bool)Own()["charging"]&&Time.realtimeSinceStartupAsDouble-changed>0.95){Runtime.ProbeWeaponAction("charge_release");step=4;}
                if((step==3&&!Hammer&&(long)Own()["light_attacks"]>0)||(step==4&&Hammer&&(long)Own()["heavy_attacks"]>0)){result["melee_current_weapon"]=true;result["rune_charge_or_blade_no_charge"]=Hammer?(long)Own()["heavy_attacks"]>0:!(bool)Own()["charging"];Runtime.ProbeWeaponAction("ability_primary");changed=Time.realtimeSinceStartupAsDouble;step=5;}
                if(step==5&&Time.realtimeSinceStartupAsDouble-changed>0.5){result["primary_from_melee"]=(long)Own()["ability_remaining_ms"]>0;Runtime.ProbeWeaponAction("ability_secondary");changed=Time.realtimeSinceStartupAsDouble;step=6;}
                if(step==6&&Time.realtimeSinceStartupAsDouble-changed>0.5){result["secondary_from_melee"]=(long)Own()["secondary_remaining_ms"]>0;if(Runtime.ProbePauseAllowed){step=7;await Runtime.ProbePause(true);}else{Runtime.ProbeWeaponAction(Hammer?"charge_begin":"attack_press");frozen=Frozen();changed=Time.realtimeSinceStartupAsDouble;step=12;await Runtime.ProbeMenu(true);}}
                if(step==12&&Time.realtimeSinceStartupAsDouble-changed>1){result["trial_menu_not_paused"]=Runtime.ProbeMenuOpen&&!Runtime.ProbePaused&&Time.timeScale==1;result["trial_world_continued"]=(long)Runtime.ProbeSnapshot["world_frame_id"]>(long)frozen["frame"];result["trial_menu_cancels_continuous_input"]=!(bool)Own()["charging"]&&Own()["melee_action"]?.Type==JTokenType.Null;step=9;await Runtime.ProbeMenu(false);}
                if(step==7&&Runtime.ProbeFrozenReady){frozen=Frozen();changed=Time.realtimeSinceStartupAsDouble;step=8;}
                if(step==8&&Time.realtimeSinceStartupAsDouble-changed>1){result["pause_new_skills"]=JToken.DeepEquals(JObject.Parse(frozen.ToString()),JObject.Parse(Frozen().ToString()))&&Time.timeScale==0;step=9;await Runtime.ProbePause(false);}
                if(step==9&&!Runtime.ProbePaused){if(Runtime.ProbePauseAllowed)result["resume_new_skills"]=true;Runtime.ProbeWeaponAction("switch_weapon");step=10;}
                if(step==10&&(!DcV8Profile.Active||(bool?)(Own()["death_hammer"] as JObject)?["active"]!=true)&&!(bool)Own()["melee_equipped"]&&(uint)Own()["weapon_revision"]>revision+1&&(!DcV7Profile.Active||!(bool)Own()["release_required"])){result[Runtime.ProbePauseAllowed?"post_resume_action":"post_menu_action"]=true;if(DcV7Profile.Active){Runtime.ProbeWeaponAction("attack_press");Runtime.ProbeWeaponAction("attack_release");changed=Time.realtimeSinceStartupAsDouble;step=11;}else{Write();Application.Quit(0);}}
                if(step==11&&Time.realtimeSinceStartupAsDouble-changed>6.5&&(!DcV8Profile.Active||Runtime.ProbeLifecycleTerminations>0)){result["v7_ranged_emission_after_resume"]=(uint?)Own()["emission_index"]>0;Write();Application.Quit(0);}
                if(Time.realtimeSinceStartupAsDouble-started>30){result["timeout"]=true;Write();Application.Quit(3);}
            }
            catch(Exception failure){result["failed"]=true;result["failure_type"]=failure.GetType().Name;Write();Application.Quit(3);}
        }
        private JObject Frozen()=>new JObject{["frame"]=Runtime.ProbeSnapshot["world_frame_id"],["primary"]=Own()["ability_remaining_ms"],["secondary"]=Own()["secondary_remaining_ms"],["weapon"]=Own()["active_weapon_id"],["charge"]=Own()["charge_ms"],["death_hammer"]=Own()["death_hammer"]?.DeepClone(),["turret_charges"]=Own()["turret_charges"],["battle_until_ms"]=Own()["battle_until_ms"],["fields"]=Runtime.ProbeSnapshot["fields"].DeepClone(),["turrets"]=Runtime.ProbeSnapshot["turrets"].DeepClone()};
        private void Write()
        {
            if(DcV8Profile.Active){Runtime.ProbeFlushWorldEvidence();result["last_action_kind"]=Runtime.ProbeLastActionKind;result["last_action_reason"]=Runtime.ProbeLastActionReason;var own=Own();result["last_actor"]=new JObject(own is JObject o?o.Properties().Where(p=>new[]{"active_weapon_id","weapon_revision","charging","charge_ms","release_required","charge_episode_id","last_action_sequence","light_attacks","heavy_attacks"}.Contains(p.Name)).Select(p=>new JProperty(p.Name,p.Value.DeepClone())):Enumerable.Empty<JProperty>());}
            var output=Path.GetFullPath(DcBootstrap.Argument("-dc-s2-probe-output"));if(!output.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("V5 probe evidence path");File.WriteAllText(output,result.ToString());
        }
    }
}
