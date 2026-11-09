using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonContract
{
    public sealed class DcP5Settings
    {
        public const string Version="p5.1";
        public static bool Active=>DcBootstrap.Argument("-dc-p5","false")=="true";
        public static readonly DcP5Settings Current=new DcP5Settings();
        public float TextScale=1;
        public float EffectsVolume=0.65f;
        public float MusicVolume=0.35f;
        public float Shake=0.5f;
        public float WarningScale=1;
        public bool ReducedFlash;
        public bool DamageNumbers=true;
        public int Reticle;
        public Action Changed;
        private JObject bindings=new JObject();
        private static string Storage=>"DungeonContract.P5.Settings.1"+(DcBootstrap.Argument("-dc-settings-profile","")=="validation"?".Validation":"");

        private DcP5Settings(){Load();}
        private static float Bounded(JToken value,float fallback,float minimum,float maximum)
        {
            var number=(float?)value??fallback;
            return float.IsNaN(number)||float.IsInfinity(number)?fallback:Mathf.Clamp(number,minimum,maximum);
        }
        private void Load()
        {
            var stored=PlayerPrefs.GetString(Storage,"");
            if(stored.Length==0||stored.Length>16384)return;
            try
            {
                var value=JObject.Parse(stored);
                TextScale=Bounded(value["text"],1,1,1.5f);EffectsVolume=Bounded(value["effects"],0.65f,0,1);MusicVolume=Bounded(value["music"],0.35f,0,1);Shake=Bounded(value["shake"],0.5f,0,1);WarningScale=Bounded(value["warnings"],1,1,2);
                ReducedFlash=(bool?)value["reduced_flash"]??false;DamageNumbers=(bool?)value["numbers"]??true;Reticle=(int)Mathf.Clamp(Bounded(value["reticle"],0,0,2),0,2);
                bindings=value["bindings"] as JObject??new JObject();
            }
            catch(Exception failure)when(failure is Newtonsoft.Json.JsonException||failure is FormatException||failure is InvalidCastException||failure is OverflowException){bindings=new JObject();}
        }
        public void Save()
        {
            var value=new JObject{["text"]=TextScale,["effects"]=EffectsVolume,["music"]=MusicVolume,["shake"]=Shake,["warnings"]=WarningScale,["reduced_flash"]=ReducedFlash,["numbers"]=DamageNumbers,["reticle"]=Reticle,["bindings"]=bindings};
            PlayerPrefs.SetString(Storage,value.ToString(Newtonsoft.Json.Formatting.None));PlayerPrefs.Save();Changed?.Invoke();
        }
        public static bool AllowedPath(string path)
        {
            if(string.IsNullOrEmpty(path)||path.Length>80)return false;
            if(path=="<Mouse>/leftButton"||path=="<Mouse>/rightButton"||path=="<Mouse>/middleButton")return true;
            if(!path.StartsWith("<Keyboard>/",StringComparison.Ordinal))return false;
            var key=path.Substring(11);
            var named=key.Length==1&&char.IsDigit(key[0])?"Digit"+key:key;
            return key.Length>0&&Enum.TryParse<Key>(named,true,out var parsed)&&parsed!=Key.None&&key.All(character=>character>='a'&&character<='z'||character>='A'&&character<='Z'||character>='0'&&character<='9')&&!new[]{"escape","tab","1","2","3","f1","f2","f3"}.Contains(key.ToLowerInvariant());
        }
        public void Apply(InputActionAsset asset)
        {
            if(bindings.Properties().Count()>24)return;
            foreach(var entry in bindings.Properties())
            {
                if(entry.Value.Type!=JTokenType.String)continue;
                var parts=entry.Name.Split(':');
                if(parts.Length!=2||!int.TryParse(parts[1],out var index))continue;
                var action=asset.FindAction("Gameplay/"+parts[0],false);
                if(action==null||index<0||index>=action.bindings.Count||action.bindings[index].isComposite||!AllowedPath((string)entry.Value))continue;
                if(Conflict(asset,action,index,(string)entry.Value))continue;
                action.ApplyBindingOverride(index,(string)entry.Value);
            }
        }
        public static bool Conflict(InputActionAsset asset,InputAction action,int index,string path)
        {
            foreach(var candidate in asset.FindActionMap("Gameplay").actions)
                for(var binding=0;binding<candidate.bindings.Count;binding++)
                    if(!(candidate==action&&binding==index)&&!candidate.bindings[binding].isComposite&&string.Equals(candidate.bindings[binding].effectivePath,path,StringComparison.OrdinalIgnoreCase))return true;
            return false;
        }
        public void StoreBinding(InputAction action,int index,string path){bindings[action.name+":"+index]=path;Save();}
        public void ResetBindings(InputActionAsset asset){bindings.RemoveAll();asset.FindActionMap("Gameplay").RemoveAllBindingOverrides();Save();}
    }
}
