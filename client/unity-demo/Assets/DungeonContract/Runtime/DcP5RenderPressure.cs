using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcP5RenderPressure : MonoBehaviour
    {
        private DcP1View view;
        private JObject snapshot;
        private JObject v8Parameters;
        private double started;
        private double next;
        private long frame;
        private long effectID;
        private string output;
        private string scenario;
        private int duration;
        private int peakRenderers;
        private int peakTurrets;
        public void Configure(DcP1View target,JObject initial)
        {
            if(DcBootstrap.Argument("-dc-settings-profile","")!="validation")throw new InvalidOperationException("Pressure requires isolated validation preferences");
            view=target;snapshot=(JObject)initial.DeepClone();scenario=DcBootstrap.Argument("-dc-p5-pressure-case","mixed");duration=int.Parse(DcBootstrap.Argument("-dc-p5-pressure-seconds","150"));if(duration<35||duration>600)throw new InvalidOperationException("Pressure duration bounds");
            output=Path.GetFullPath(DcBootstrap.Argument("-dc-p5-pressure-output"));if(!DcBootstrap.IsUnderDemoCache(output)||!output.StartsWith(DcBootstrap.DemoEvidenceRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Pressure evidence path");
            var players=(JArray)snapshot["players"];var template=(JObject)players[0].DeepClone();players.Clear();
            for(var index=0;index<4;index++){var actor=(JObject)template.DeepClone();actor["player_id"]=(index+1).ToString();actor["slot"]=index;actor["room_id"]=1;actor["x"]=13+index;actor["y"]=8+index%2;actor["loadout_id"]=scenario=="ranger_fan"||scenario=="mixed"&&index%2==0?"ranger":"runesmith";actor["active_weapon_id"]=(string)actor["loadout_id"]=="ranger"?"repeating_crossbow":"rune_bolt";actor["upgrades"]=(string)actor["loadout_id"]=="ranger"?new JArray("ranger_fan","ranger_pierce","frost"):new JArray("runesmith_twin","runesmith_heavy","runesmith_chain");players.Add(actor);}
            var enemies=(JArray)snapshot["enemies"];var enemyTemplate=(JObject)enemies[0].DeepClone();enemies.Clear();
            for(var index=0;index<24;index++){var enemy=(JObject)enemyTemplate.DeepClone();enemy["id"]="render_enemy_"+index;enemy["kind"]=index==0?"boss":index%7==0?"elite":index%3==0?"archer":index%3==1?"guard":"charger";enemy["room_id"]=1;enemy["x"]=7+index%6*3;enemy["y"]=4+index/6*3;enemy["health"]=500;enemy["maximum"]=500;enemy["warning"]="";enemy["visual_phase"]="idle";enemies.Add(enemy);}
            snapshot["projectiles"]=new JArray(Enumerable.Range(0,1024).Select(index=>new JObject{["id"]="render_projectile_"+index,["room_id"]=1,["x"]=4+index%48*0.5f,["y"]=3+index/48*0.5f,["enemy"]=index%3!=0,["speed"]=5,["radius"]=index%3==0?(DcV7Profile.Active?.3f:DcV6Profile.Active?0.72f:0.18f):0.1f,["direction_x"]=-1,["direction_y"]=0,["bounces"]=0,["penetrations"]=0,["unbounded"]=true}));
            foreach(var projectile in snapshot["projectiles"]){projectile["rune_bolt"]=(bool)projectile["enemy"]==false&&scenario=="runesmith_twin";projectile["wind_volley"]=(bool)projectile["enemy"]==false&&scenario=="ranger_fan";}
            if(scenario=="offscreen")snapshot["players"][0]["x"]=3;
            snapshot["turrets"]=new JArray(Enumerable.Range(0,8).Select(index=>new JObject{["id"]="render_turret_"+index,["room_id"]=1,["owner"]=(index/2+1).ToString(),["x"]=8+index*2,["y"]=6}));
            snapshot["rooms"][1]["status"]="active";snapshot["rooms"][1]["enemies_remaining"]=24;snapshot["outcome"]=null;snapshot["practice"]=true;snapshot["fields"]=new JArray(new JObject{["id"]="render_wind",["room_id"]=1,["kind"]="wind_bind",["radius"]=2.5f,["x"]=14,["y"]=8},new JObject{["id"]="render_field",["room_id"]=1,["kind"]="guard",["radius"]=2.5f,["x"]=18,["y"]=8});if(DcV6Profile.Active)foreach(var field in snapshot["fields"]){field["owner"]=(string)field["kind"]=="wind_bind"?"1":"2";field["owner_life"]=1;field["cast_id"]=field["id"].DeepClone();field["ends_ms"]=3600000;}snapshot["effects"]=new JArray();snapshot["spawn_reservations"]=new JArray();snapshot["markers"]=new JArray();snapshot["team_rooms"]=new JArray();
            if(DcV8Profile.Active)v8Parameters=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"..","Content","parameters-v8-p5.json")));
            DcP5Settings.Current.MusicVolume=0;DcP5Settings.Current.EffectsVolume=0;DcP5Settings.Current.TextScale=scenario=="large_text"?1.5f:1;DcP5Settings.Current.Save();started=Time.realtimeSinceStartupAsDouble;next=started;view.Status("P5 渲染压力夹具 · 非实际游玩 / 非UDP");
        }
        private void Update()
        {
            if(snapshot==null)return;
            var elapsed=Time.realtimeSinceStartupAsDouble-started;
            if(elapsed>=duration)
            {
                File.WriteAllText(output,new JObject{["profile"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":DcV6Profile.Active?"dc_ruins.v6.p5.2":"dc_ruins.v5.p5.1",["scope"]="modified_snapshot_render_pressure_only",["modified_world_state"]=true,["real_udp"]=false,["authority_simulation"]=false,["human"]=false,["scenario"]=scenario,["duration_seconds"]=duration,["players"]=4,["enemies"]=24,["projectiles"]=1024,["turrets"]=peakTurrets,["effect_events_per_burst"]=64,["fixture_version"]=DcV8Profile.Active?DcV8PressureFixture.Version:"inherited",["turret_count_scope"]="maximum_staged_peak",["peak_sprite_renderers"]=peakRenderers,["audio_muted"]=true,["frame_metrics"]="separate actual_player_render_loop report"}.ToString());PlayerPrefs.DeleteKey("DungeonContract.P5.Settings.1.Validation");Application.Quit(0);return;
            }
            if(Time.realtimeSinceStartupAsDouble<next)return;next+=1d/30;snapshot["world_frame_id"]=++frame;snapshot["elapsed_ms"]=(long)(elapsed*1000);
            var index=0;foreach(var projectile in snapshot["projectiles"]){projectile["x"]=scenario=="offscreen"?26+index%8*0.2f:4+(index%48*0.5f+(float)elapsed*2)%23;projectile["y"]=3+(index/48*0.5f+(float)Math.Sin(elapsed+index)*0.15f)%11;index++;}
            var enemyIndex=0;foreach(var enemy in snapshot["enemies"]){var phase=(frame/15+enemyIndex++)%4;enemy["warning"]=phase==0?"melee":"";enemy["warning_x"]=enemy["x"];enemy["warning_y"]=enemy["y"];enemy["aim_x"]=1;enemy["aim_y"]=0;enemy["visual_phase"]=phase==0?"prepare":phase==1?"attack":phase==2?"recover":"idle";}
            var effects=new JArray();if(frame%3==0)for(var effect=0;effect<64;effect++)effects.Add(new JObject{["id"]="pressure_fx_"+(++effectID),["kind"]=effect%3==0?"hit":effect%3==1?"heavy":"light",["actor"]="1",["target"]="render_enemy_"+(effect%24),["room"]=1,["x"]=8+effect%8*2,["y"]=4+effect/8,["amount"]=effect%3==0?12:0,["at"]=(long)(elapsed*1000),["visual_radius"]=effect%3==1?2.3f:1.2f,["visual_aim_x"]=1,["visual_aim_y"]=0});snapshot["effects"]=effects;
            if(DcV8Profile.Active)DcV8PressureFixture.Render(snapshot,v8Parameters,(long)(elapsed*1000),scenario);
            peakTurrets=Math.Max(peakTurrets,((JArray)snapshot["turrets"]).Count);
            view.Receive(snapshot);view.VisualPosition(new Vector2(scenario=="offscreen"?3:15+(float)Math.Sin(elapsed)*0.5f,9));if(frame%30==0)peakRenderers=Math.Max(peakRenderers,FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length);
        }
    }
}
