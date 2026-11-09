using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcP5Art
    {
        private static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        private static bool loaded;
        private static Sprite backdrop;
        private static bool backdropLoaded;
        public static Sprite Backdrop
        {
            get
            {
                if(!backdropLoaded){backdropLoaded=true;var texture=Resources.Load<Texture2D>("DungeonContract/P5/ruins-lobby-ai");if(texture!=null){texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;backdrop=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),16);}}
                return backdrop;
            }
        }
        public static bool HasBackdrop=>Backdrop!=null;
        public static int Count{get{Load();return sprites.Count;}}
        private static void Load()
        {
            if(loaded)return;
            var texture=Resources.Load<Texture2D>("DungeonContract/P5/ruins-p5");
            var document=Resources.Load<TextAsset>("DungeonContract/P5/ruins-p5");
            if(texture==null||document==null)throw new InvalidOperationException("P5 atlas missing from complete package");
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            var atlas=JObject.Parse(document.text);
            foreach(var property in ((JObject)atlas["frames"]).Properties())
            {
                var frame=property.Value;
                var sprite=Sprite.Create(texture,new Rect((int)frame["x"],(int)frame["y"],(int)frame["width"],(int)frame["height"]),new Vector2((float)frame["pivot_x"],(float)frame["pivot_y"]),16,0,SpriteMeshType.FullRect);
                sprite.name=property.Name;sprites.Add(property.Name,sprite);
            }
            loaded=true;
        }
        public static Sprite Weapon(string id)=>Get("icon."+(id=="ruin_blade"?"short_blade":id));
        public static Sprite Get(string id){if(id=="icon.battle_rune")id="icon.volley";Load();return sprites.TryGetValue(id,out var sprite)?sprite:null;}
        public static string Direction(Vector2 aim)=>Math.Abs(aim.x)>Math.Abs(aim.y)?aim.x<0?"left":"right":aim.y>0?"up":"down";
        public static Sprite Actor(string kind,Vector2 direction,string state,int frame)=>Get("actor."+kind+"."+Direction(direction)+"."+state+"."+(frame%4));
    }
}
