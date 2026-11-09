using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private void StyleP5Projectile(SpriteRenderer renderer,JToken projectile)
        {
            if((bool)projectile["enemy"]){renderer.color=new Color32(235,100,72,255);return;}
            if((bool?)projectile["rune_bolt"]==true&&(bool?)snapshot["v6_stage"]==true){renderer.sprite=disk;renderer.color=new Color32(92,205,184,245);renderer.transform.localScale=Vector3.one*((float)projectile["radius"]*2);return;}
            if((bool?)projectile["rune_bolt"]==true){renderer.sprite=DcP5Art.Get("icon.rune_bolt");renderer.color=Color.white;renderer.transform.localScale=Vector3.one*((float)projectile["radius"])*((bool?)snapshot["v6_stage"]==true?2:1);}
            else{var wind=(bool?)projectile["wind_volley"]==true;renderer.sprite=square;renderer.color=wind?new Color32(141,196,160,255):DcP5Theme.Gold;renderer.transform.localScale=new Vector3(wind?0.65f:0.45f,wind?0.12f:0.08f,1);renderer.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2((float)projectile["direction_y"],(float)projectile["direction_x"])*Mathf.Rad2Deg);}
        }
    }
}
