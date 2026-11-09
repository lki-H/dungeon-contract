using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private AudioSource p5Music;
        private readonly Dictionary<string,Vector2> p5Previous=new Dictionary<string,Vector2>();
        private readonly Dictionary<string,float> p5RecoverUntil=new Dictionary<string,float>();
        private readonly HashSet<string> p5Preparing=new HashSet<string>();
        private readonly Queue<(GameObject item,float until,string kind)> p5DeathEffects=new Queue<(GameObject,float,string)>();
        private readonly Dictionary<string,GameObject> p5Corpses=new Dictionary<string,GameObject>();
        private readonly Dictionary<string,string> p5CorpseKinds=new Dictionary<string,string>();
        private readonly Dictionary<string,string> p5ActorStates=new Dictionary<string,string>();
        private readonly Dictionary<string,float> p5ActorStateAt=new Dictionary<string,float>();
        private float p5NextAudio;
        private readonly Dictionary<string,float> v6ContactAudioAt=new Dictionary<string,float>();
        private float p5NextPulse;
        private string p5PreviousOutcome;

        private void BuildP5Terrain()
        {
            var layout=geometry["rooms"][room];var width=(float)layout["width"];var height=(float)layout["height"];
            foreach(var item in terrain)
            {
                var renderer=item.GetComponent<SpriteRenderer>();
                var size=(Vector2)item.transform.localScale;
                if(item.name.StartsWith("机关",StringComparison.Ordinal)){renderer.sprite=DcP5Art.Get("terrain.mechanism.0");item.transform.localScale=Vector3.one;renderer.color=Color.white;renderer.sortingOrder=1;}
                else{renderer.sprite=DcP5Art.Get("terrain.wall.0");item.transform.localScale=Vector3.one;renderer.drawMode=SpriteDrawMode.Tiled;renderer.size=size;renderer.color=Color.white;renderer.sortingOrder=1;}
            }
            var floor=new GameObject("P5 stone floor").AddComponent<SpriteRenderer>();floor.transform.SetParent(root.transform);floor.transform.position=new Vector2(width/2,height/2)+RoomOffset;floor.sprite=DcP5Art.Get("terrain.floor."+(room%3));floor.drawMode=SpriteDrawMode.Tiled;floor.size=new Vector2(width,height);floor.sortingOrder=-20;terrain.Add(floor.gameObject);
            p5Previous.Clear();p5Preparing.Clear();p5RecoverUntil.Clear();
            foreach(var corpse in p5Corpses.Values)Destroy(corpse);foreach(var corpse in p5DeathEffects)Destroy(corpse.item);p5Corpses.Clear();p5CorpseKinds.Clear();p5ActorStates.Clear();p5ActorStateAt.Clear();p5DeathEffects.Clear();
            foreach(var effect in p5Fx){effect.Until=0;effect.Renderer.enabled=false;}foreach(var number in p5Numbers){number.Until=0;number.Label.gameObject.SetActive(false);}
            p5Operator=null;p5InterruptUntil=0;if(p5InteractionLabel!=null)p5InteractionLabel.text="";
        }
        private void P5Actors()
        {
            var present=new HashSet<string>();
            foreach(var group in new[]{"players","enemies"})
                foreach(var actor in ((JArray)snapshot[group]).Where(item=>(int)item["room_id"]==room))
                {
                    var player=group=="players";var key=(player?"player:":"enemy:")+(string)actor[player?"player_id":"id"];present.Add(key);
                    if(!sprites.TryGetValue(key,out var renderer))continue;
                    var kind=player?(string)actor["loadout_id"]:(string)actor["kind"];
                    var location=(Vector2)renderer.transform.position-RoomOffset;var movement=p5Previous.TryGetValue(key,out var previous)?location-previous:Vector2.zero;p5Previous[key]=location;
                    var direction=player&&key=="player:"+ownID?PointerWorld(p5Controls?.Pointer??Vector2.zero)-Position:movement.sqrMagnitude>0.00001f?movement:new Vector2((float?)actor["aim_x"]??0,(float?)actor["aim_y"]??-1);
                    var state=movement.sqrMagnitude>0.00001f?"move":"idle";
                    var action=actor["melee_action"] as JObject;
                    if(player){if((int)actor["health"]<=0)state="death";else if((bool?)actor["charging"]==true)state="charge";else if(action!=null)state=(string)action["phase"]=="active"?"attack":((string)action["phase"]=="recover"||(string)action["phase"]=="recovery")?"recover":"prepare";else if((long)actor["dodge_duration_remaining_ms"]>0)state="attack";}
                    else
                    {
                        var phase=(string)actor["visual_phase"];
                        if(phase=="prepare"||phase=="attack"||phase=="recover")state=phase;
                    }
                    if(flashes.TryGetValue(key,out var flash)&&ViewTime<flash&&state!="death")state="hurt";
                    if(!p5ActorStates.TryGetValue(key,out var previousState)||previousState!=state){p5ActorStates[key]=state;p5ActorStateAt[key]=ViewTime;}
                    var animated=(int)((ViewTime-p5ActorStateAt[key])*(state=="idle"||state=="move"?8:12));var pose=state=="death"?1:state=="idle"||state=="move"?animated%4:Math.Min(3,animated);var drawnState=state=="hurt"&&DcP5Settings.Current.ReducedFlash?(movement.sqrMagnitude>0.00001f?"move":"idle"):state;
                    renderer.sprite=V7View?V7ActorSprite(actor,direction,drawnState,pose):DcP5Art.Actor(kind,direction,drawnState,pose);
                    renderer.transform.localScale=new Vector3(state=="hurt"&&!V7View?1.04f:1,state=="hurt"&&!V7View?0.96f:1,1)*(kind=="boss"?1.5f:kind=="elite"?1.15f:player&&(bool?)snapshot["v6_stage"]==true?0.8f:1);
                    renderer.color=state=="hurt"&&!V7View&&!DcP5Settings.Current.ReducedFlash?new Color(1,1,1,0.7f):Color.white;
                    renderer.sortingOrder=7;
                    if(!player&&!p5Corpses.ContainsKey(key))
                    {
                        var corpse=new GameObject("P5 death "+kind);corpse.transform.SetParent(root.transform);corpse.SetActive(false);var ghost=corpse.AddComponent<SpriteRenderer>();ghost.sprite=DcP5Art.Actor(kind,Vector2.down,"death",0);ghost.sortingOrder=2;corpse.transform.localScale=renderer.transform.localScale;p5Corpses[key]=corpse;p5CorpseKinds[key]=kind;
                    }
                    if(!player)p5Corpses[key].transform.position=renderer.transform.position;
                }
            foreach(var key in p5Previous.Keys.Where(key=>!present.Contains(key)).ToArray())
            {
                if(p5Corpses.TryGetValue(key,out var corpse)){corpse.SetActive(true);p5DeathEffects.Enqueue((corpse,ViewTime+0.7f,p5CorpseKinds[key]));p5Corpses.Remove(key);}
                p5Previous.Remove(key);p5RecoverUntil.Remove(key);p5Preparing.Remove(key);p5ActorStates.Remove(key);p5ActorStateAt.Remove(key);p5CorpseKinds.Remove(key);
            }
            foreach(var corpse in p5DeathEffects)corpse.item.GetComponent<SpriteRenderer>().sprite=DcP5Art.Actor(corpse.kind,Vector2.down,"death",Mathf.Clamp((int)((ViewTime-corpse.until+0.7f)*6),0,3));while(p5DeathEffects.Count>0&&p5DeathEffects.Peek().until<=ViewTime)Destroy(p5DeathEffects.Dequeue().item);
            foreach(var group in new[]{"turrets","covers","traps"})foreach(var actor in ((JArray)snapshot[group]).Where(item=>(int)item["room_id"]==room))
            {
                if(!sprites.TryGetValue(group+":"+actor["id"],out var renderer))continue;
                renderer.sprite=DcP5Art.Get(group=="turrets"?"icon.turret":group=="covers"?"terrain.cover.0":"terrain.trap."+((string)actor["phase"]=="active"?1:0));
                renderer.transform.localScale=Vector3.one*(group=="covers"?0.5f:group=="turrets"?0.55f:0.7f);renderer.color=Color.white;
            }
            P5Weapon();P5Occlusion();DrawP5Markers();
        }
        private void P5Weapon()
        {
            if(V7View){DrawV7Weapons();return;}
            var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==ownID);
            if(!sprites.TryGetValue("weapon:own",out var weapon))return;
            weapon.enabled=(int)own["health"]>0;
            var id=(string)own["active_weapon_id"];weapon.sprite=DcP5Art.Weapon(id);weapon.transform.localScale=Vector3.one*0.55f;
            var action=own["melee_action"] as JObject;var direction=PointerWorld(p5Controls?.Pointer??Vector2.zero)-Position;var degrees=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            if(action!=null)
            {
                var age=(long)snapshot["world_frame_id"]-(long)action["started_tick"];var windup=Math.Max(1,(long)action["active_tick"]-(long)action["started_tick"]);var phase=(string)action["phase"];
                degrees=Mathf.Atan2((float)action["aim_y"],(float)action["aim_x"])*Mathf.Rad2Deg;
                if(phase=="active")degrees+=((float)action["sweep_start"]+(float)action["sweep_end"])/2;
                else if(phase=="windup"||phase=="prepare")degrees-=Mathf.Lerp(20,100,Mathf.Clamp01(age/(float)windup));
                else degrees+=Mathf.Lerp(90,0,Mathf.Clamp01(((long)snapshot["world_frame_id"]-(long)action["recovery_tick"])/(float)Math.Max(1,(long)action["end_tick"]-(long)action["recovery_tick"])));
            }
            else if((bool)own["charging"])degrees+=Mathf.Lerp(0,110,Mathf.Clamp01((long)own["charge_ms"]/900f));
            var anchor=Position+(Vector2)(Quaternion.Euler(0,0,degrees)*Vector2.right)*0.45f+Vector2.up*0.3f;
            if(action!=null&&(bool)action["heavy"]&&(bool?)snapshot["v6_stage"]!=true)
            {
                var impact=new Vector2((float)action["impact_x"],(float)action["impact_y"]);var phase=(string)action["phase"];var age=(long)snapshot["world_frame_id"]-(long)action["started_tick"];var windup=Math.Max(1,(long)action["active_tick"]-(long)action["started_tick"]);
                if(phase=="windup"){var progress=Mathf.Clamp01(age/(float)windup);anchor=Vector2.Lerp(Position+Vector2.up*1.2f,impact,progress);degrees+=Mathf.Lerp(60,-90,progress);}
                else if(phase=="active"){anchor=impact;degrees-=90;}
                else{var progress=Mathf.Clamp01(((long)snapshot["world_frame_id"]-(long)action["recovery_tick"])/(float)Math.Max(1,(long)action["end_tick"]-(long)action["recovery_tick"]));anchor=Vector2.Lerp(impact,anchor,progress);degrees-=90*(1-progress);}
            }
            if((bool?)snapshot["v6_stage"]==true)V6WeaponPose(own,action,direction,ref anchor,ref degrees);
            var preview=ViewTime<localStart?Mathf.Clamp01((localStart-ViewTime)/0.06f):0;weapon.transform.localScale=Vector3.one*(0.55f+0.04f*preview);anchor-=direction.normalized*preview*0.04f;
            if(ViewTime>=weaponHold)weapon.transform.rotation=Quaternion.Euler(0,0,degrees-45);if((bool?)snapshot["v6_stage"]==true)V6Grip(weapon,own,ref anchor,degrees);weapon.transform.position=anchor+RoomOffset;
        }
        private void P5Occlusion()
        {
            if(V7View)return;
            var teammates=((JArray)snapshot["players"]).Where(item=>(int)item["room_id"]==room).Select(item=>Point(item)+RoomOffset).ToArray();
            foreach(var item in terrain)
            {
                if(!item.name.StartsWith("P1 wall",StringComparison.Ordinal))continue;
                var renderer=item.GetComponent<SpriteRenderer>();var obscures=teammates.Any(point=>renderer.bounds.Contains(new Vector3(point.x,point.y+0.6f,renderer.transform.position.z)));renderer.color=new Color(1,1,1,obscures?0.35f:1);
            }
        }
        private void P5Audio()
        {
            if(p5Music==null)
            {
                p5Music=gameObject.AddComponent<AudioSource>();p5Music.clip=Resources.Load<AudioClip>("DungeonContract/P5/music");p5Music.loop=true;p5Music.playOnAwake=false;p5Music.volume=DcP5Settings.Current.MusicVolume;if(p5Music.clip!=null)p5Music.Play();
                if(visualPaused)p5Music.Pause();
            }
            p5Music.volume=DcP5Settings.Current.MusicVolume;
            var outcome=(string)snapshot["outcome"];
            if(outcome!=p5PreviousOutcome&&!string.IsNullOrEmpty(outcome)){p5PreviousOutcome=outcome;p5Music.Stop();P5Sound(outcome=="success"?"success":"failure",true);}
            if(!visualPaused&&ViewTime>=p5NextPulse&&((JArray)snapshot["enemies"]).Any(item=>(int)item["room_id"]==room&&!string.IsNullOrEmpty((string)item["warning"]))){p5NextPulse=ViewTime+0.7f;P5Sound("warning",true);}
        }
        private void P5Sound(string kind,bool important=false,string actor=null)
        {
            var separate=kind=="blade_hit"||kind=="hammer_hit"||kind=="blade_block"||kind=="hammer_block";
            if(visualPaused||!separate&&!important&&ViewTime<p5NextAudio)return;
            if(separate){if(v6ContactAudioAt.TryGetValue(kind,out var until)&&ViewTime<until)return;v6ContactAudioAt[kind]=ViewTime+0.05f;}
            var name=kind=="blade_block"||kind=="hammer_block"||kind=="wind_clear"?"clear":kind=="blade_hit"?"light":kind=="hammer_hit"?"heavy":kind=="ability"||kind=="secondary"?"skill":kind=="protection"?"clear":kind;
            if(kind=="cover_hit"||kind=="ricochet")name="hit";else if(kind=="cover_break")name="clear";else if(kind=="barrage_pulse"||kind=="boss_phase")name="boss";else if(kind=="summon")name="warning";else if(kind=="kill")name="clear";
            else if(kind=="rescued"||kind=="reinforcement")name="rescue";else if(kind=="projectile_erased"||kind=="room_clear")name="clear";else if(kind=="interaction"||kind=="door")name="interact";else if(kind=="gust_dash")name="dodge";else if(kind=="room_warning"||kind=="trap_warning")name="warning";else if(kind=="reload_done"||kind=="reload_cancel"||kind=="weapon_switch")name="reload";else if(kind=="shield")name="skill";else if(kind=="environment_hit")name="hit";
            if(!new[]{"shot","hit","hurt","dodge","skill","light","heavy","charge","clear","reload","interact","warning","downed","rescue","death","boss","success","failure","turret_shot"}.Contains(name))return;
            if(kind=="shot"){var owner=((JArray)snapshot["players"]).FirstOrDefault(item=>(string)item["player_id"]==actor);name=(string)owner?["loadout_id"]=="runesmith"?"shot_rune":"shot_crossbow";}
            if(!p5Clips.TryGetValue(name,out var clip)){clip=Resources.Load<AudioClip>("DungeonContract/P5/"+name)??Resources.Load<AudioClip>("DungeonContract/P5/hit");p5Clips[name]=clip;}
            if(clip==null)return;
            var voice=p5Voices.FirstOrDefault(item=>!item.isPlaying);
            if(voice==null&&p5Voices.Count<16){voice=gameObject.AddComponent<AudioSource>();voice.playOnAwake=false;p5Voices.Add(voice);}
            if(voice==null){if(!important)return;voice=p5Voices[0];voice.Stop();}
            p5NextAudio=ViewTime+0.035f;voice.clip=clip;voice.pitch=kind=="cover_hit"||kind=="cover_break"?0.65f:kind=="ricochet"?1.4f:kind=="blade_block"?1.25f:kind=="hammer_block"?0.8f:kind=="blade_hit"?1.1f:kind=="hammer_hit"?0.9f:1;voice.volume=DcP5Settings.Current.EffectsVolume*(important?0.8f:0.55f);voice.Play();
        }
    }
}
