using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcP5Checks
    {
        public static void Run()
        {
            Require(DcP5Art.Count==926,"complete atlas");
            Require(DcP5Art.Backdrop!=null||!File.Exists("Assets/Resources/DungeonContract/P5/ruins-lobby-ai.png"),"optional backdrop loads or legal slate fallback is selected");
            foreach(var actor in new[]{"ranger","runesmith","guard","archer","charger","elite","boss"})
                foreach(var direction in new[]{Vector2.up,Vector2.down,Vector2.left,Vector2.right})
                    foreach(var state in new[]{"idle","move","prepare","attack","recover","hurt","death","charge"})
                        for(var frame=0;frame<4;frame++)Require(DcP5Art.Actor(actor,direction,state,frame)!=null,"actor sequence");
            foreach(var id in new[]{"short_blade","repeating_crossbow","rune_hammer","rune_bolt","dodge","volley","wind","turret","field","swift","frost","ward","vitality","ranger_pierce","ranger_fan","ranger_tension","runesmith_chain","runesmith_twin","runesmith_heavy"})Require(DcP5Art.Get("icon."+id)!=null,"icon "+id);
            foreach(var name in new[]{"shot","hit","hurt","dodge","skill","light","heavy","charge","clear","reload","interact","warning","downed","rescue","death","boss","success","failure","music","shot_crossbow","shot_rune","turret_shot"})Require(Resources.Load<AudioClip>("DungeonContract/P5/"+name)!=null,"audio "+name);
            Require(Resources.Load<Font>("DungeonContract/Fonts/NotoSansSC")!=null,"bundled Chinese font");
            var parameters=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/parameters-v5-p4.json"));var geometry=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/world-v5-p4.json"));var configuration=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/demo-v5-p5.json"));
            var world=new DcP1World(new JObject{["run_id"]="p5_visual_codec_model",["parameters"]=parameters,["p1_world"]=geometry,["config"]=configuration,["practice"]=true,["weapon_stage"]=true,["p2_stage"]=true,["members"]=new JArray(new JObject{["player_id"]="1",["slot"]=0,["loadout_id"]="ranger"})});
            Require(world.Action("1","ability_primary",new DcP1Input{WeaponID=world.ActiveWeapon("1"),WeaponRevision=world.WeaponRevision("1"),Aim=0}),"accepted volley");
            var value=world.Snapshot();value["protocol_version"]=5;Require(((JArray)value["projectiles"]).Count==3&&value["projectiles"].All(item=>(bool)item["wind_volley"]),"authority volley visual identity");
            var before=world.Capture();var checkpoint=world.CaptureRuntimeCheckpoint();world.Tick(34);world.Restore(checkpoint);Require(JToken.DeepEquals(before,world.Capture()),"typed projectile checkpoint deep restore");world.Tick(67);world.Restore(checkpoint);Require(JToken.DeepEquals(before,world.Capture()),"checkpoint reusable without mutable projectile aliases");
            Require(JToken.DeepEquals(JToken.Parse(world.Snapshot().ToString()),JToken.Parse(world.RuntimeSnapshot().ToString())),"P5 cold cached wire JSON matches complete projection");
            var baseline=(JObject)value.DeepClone();DcS1Replica.PackV5Baseline(baseline,true);var replica=new DcS1Replica{ProtocolVersion=5,P5Presentation=true};replica.Baseline(baseline,"1",1);Require(replica.Snapshot["projectiles"].All(item=>(bool)item["wind_volley"]),"baseline visual bit");value["world_frame_id"]=1;
            foreach(var packet in DcS1Replica.Encode(value,1,7,9,5,"1"))Require(replica.Dynamic(packet,7,9),"dynamic visual bit");Require(replica.Snapshot["projectiles"].All(item=>(bool)item["wind_volley"]),"dynamic identity retained");
            var legacyRejected=false;try{var old=new DcS1Replica{ProtocolVersion=5};old.Baseline((JObject)baseline.DeepClone(),"1",1);}catch(InvalidDataException){legacyRejected=true;}Require(legacyRejected,"old profile does not silently accept P5 flags");
            Require(!DcP5Settings.AllowedPath("<Keyboard>/escape")&&!DcP5Settings.AllowedPath("<Keyboard>/tab")&&!DcP5Settings.AllowedPath("<Mouse>/scroll"),"reserved bindings");
            Require(!DcP5Settings.AllowedPath("<Keyboard>/a;payload")&&DcP5Settings.AllowedPath("<Keyboard>/k"),"binding bounds");
            using(var controls=new DcP1Controls(true,true)){Require(DcP5Settings.Conflict(controls.Asset,controls.Asset.FindAction("Gameplay/Primary"),0,"<Keyboard>/j"),"duplicate binding");Require(!DcP5Settings.Conflict(controls.Asset,controls.Asset.FindAction("Gameplay/Primary"),0,"<Keyboard>/k"),"free binding");}
            var atlas=AssetImporter.GetAtPath("Assets/Resources/DungeonContract/P5/ruins-p5.png") as TextureImporter;
            Require(atlas!=null&&atlas.filterMode==FilterMode.Point&&!atlas.mipmapEnabled&&atlas.textureCompression==TextureImporterCompression.Uncompressed,"pixel import");
            var record=new JObject{["presentation_version"]=DcP5Settings.Version,["atlas_frames"]=DcP5Art.Count,["actors"]=7,["directions"]=4,["states"]=8,["audio_clips"]=22,["resource_import"]=true,["binding_validation"]=true,["human"]=false,["status"]="PASS"};
            File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/")+(DcV6Profile.Active?"v6-regression-":"")+"p5-model-checks.json",record.ToString());
            Debug.Log("P5 resource and settings model checks passed");
        }
        private static void Require(bool condition,string name){if(!condition)throw new InvalidOperationException("P5: "+name);}
    }
}
