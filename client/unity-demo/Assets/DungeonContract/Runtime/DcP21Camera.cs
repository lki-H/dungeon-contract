using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class DcP21Camera
    {
        public static Vector2 Center(JObject room,Vector2 position)
        {
            var width=(float)room["width"];var height=(float)room["height"];
            return new Vector2(width<=24?width/2:Mathf.Clamp(position.x,12,width-12),height<=13.5f?height/2:Mathf.Clamp(position.y,6.75f,height-6.75f));
        }
    }
}
