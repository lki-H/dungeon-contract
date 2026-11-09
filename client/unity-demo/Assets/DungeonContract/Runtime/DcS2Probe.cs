using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcS2Probe : MonoBehaviour
    {
        public DcP0V3 Runtime;
        private double started=-1;
        private double at;
        private int step;
        private uint initialRevision;
        private JObject frozen;
        private readonly JObject result=new JObject{["real_player"]=true,["human"]=false};
        private async void Update()
        {
            if(Runtime==null||!Runtime.ProbeReady)return;
            if(started<0){started=Time.realtimeSinceStartupAsDouble;initialRevision=(uint)Own()["weapon_revision"];Runtime.ProbeWeaponAction("switch_weapon");at=Time.realtimeSinceStartupAsDouble;step=1;}
            try
            {
                if(step==1&&(bool)Own()["melee_equipped"]&&(uint)Own()["weapon_revision"]>initialRevision){result["single_weapon_switch"]=true;step=2;at=Time.realtimeSinceStartupAsDouble;}
                if(step==2&&Time.realtimeSinceStartupAsDouble-at>0.3){Runtime.ProbeWeaponAction("attack_press");step=3;at=Time.realtimeSinceStartupAsDouble;}
                if(step==3&&(bool)Own()["charging"]&&Time.realtimeSinceStartupAsDouble-at>0.3){Runtime.ProbeWeaponAction("attack_release");step=4;at=Time.realtimeSinceStartupAsDouble;}
                if(step==4&&(long)Own()["heavy_attacks"]>0){result["manual_charge_release"]=true;Runtime.ProbeWeaponAction("ability_primary");step=5;at=Time.realtimeSinceStartupAsDouble;}
                if(step==5&&Time.realtimeSinceStartupAsDouble-at>0.6){result["primary_from_melee"]=(long)Own()["ability_remaining_ms"]>0;Runtime.ProbeWeaponAction("ability_secondary");step=6;at=Time.realtimeSinceStartupAsDouble;}
                if(step==6&&Time.realtimeSinceStartupAsDouble-at>0.6){result["secondary_from_melee"]=(long)Own()["secondary_remaining_ms"]>0;step=7;await Runtime.ProbePause(true);}
                if(step==7&&Runtime.ProbeFrozenReady){frozen=FrozenState();at=Time.realtimeSinceStartupAsDouble;step=8;}
                if(step==8&&Time.realtimeSinceStartupAsDouble-at>1){var after=FrozenState();result["pause_new_skills"]=JToken.DeepEquals(JObject.Parse(frozen.ToString()),JObject.Parse(after.ToString()))&&Time.timeScale==0;result["pause_before"]=frozen;result["pause_after"]=after;result["pause_time_scale"]=Time.timeScale;step=9;await Runtime.ProbePause(false);}
                if(step==9&&!Runtime.ProbePaused){result["resume_new_skills"]=true;Runtime.ProbeWeaponAction("switch_weapon");step=10;}
                if(step==10&&!(bool)Own()["melee_equipped"]&&(uint)Own()["weapon_revision"]>initialRevision+1){result["post_resume_action"]=true;Write();Application.Quit(0);}
                if(Time.realtimeSinceStartupAsDouble-started>25){result["timeout"]=true;Write();Application.Quit(3);}
            }
            catch(Exception){result["failed"]=true;Write();Application.Quit(3);}
        }
        private JToken Own()=>((JArray)Runtime.ProbeSnapshot["players"]).First();
        private JObject FrozenState()=>new JObject{["world_frame_id"]=Runtime.ProbeSnapshot["world_frame_id"],["weapon_id"]=Own()["active_weapon_id"],["weapon_revision"]=Own()["weapon_revision"],["magazine"]=Own()["magazine"],["primary_remaining_ms"]=Own()["ability_remaining_ms"],["secondary_remaining_ms"]=Own()["secondary_remaining_ms"],["fields"]=Runtime.ProbeSnapshot["fields"].DeepClone(),["turrets"]=Runtime.ProbeSnapshot["turrets"].DeepClone()};
        private void Write()
        {
            var output=DcBootstrap.Argument("-dc-s2-probe-output");if(string.IsNullOrEmpty(output))return;
            var path=Path.GetFullPath(output);if(!path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("S2 probe path");
            File.WriteAllText(path,result.ToString());
        }
    }
}
