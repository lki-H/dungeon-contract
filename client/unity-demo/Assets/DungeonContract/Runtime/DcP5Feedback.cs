using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private sealed class FloatingP5
        {
            public Text Label;
            public string Target;
            public long Frame;
            public int Amount;
            public float Until;
            public Vector2 Position;
        }
        private readonly List<FloatingP5> p5Numbers=new List<FloatingP5>();
        private readonly List<AudioSource> p5Voices=new List<AudioSource>();
        private readonly Dictionary<string,AudioClip> p5Clips=new Dictionary<string,AudioClip>();
        private sealed class FxP5 {public SpriteRenderer Renderer;public Vector2 Position;public float Radius;public float Begin;public float Until;public bool Heavy;public bool Trace;public Vector2 From;}
        private readonly List<FxP5> p5Fx=new List<FxP5>();
        private Sprite p5Ring;
        private void P5CombatFeedback(JToken effect)
        {
            var kind=(string)effect["kind"];var actor=(string)effect["actor"];
            if(new[]{"blade_hit","hammer_hit","blade_block","hammer_block","wind_clear"}.Contains(kind)&&(int)effect["room"]==room){var contact=(JObject)effect.DeepClone();contact["kind"]=kind=="hammer_hit"?"heavy":"light";contact["visual_radius"]=kind.EndsWith("block",StringComparison.Ordinal)?0.25f:0.4f;P5AttackFx(contact);}
            if(new[]{"cover_hit","cover_break","ricochet"}.Contains(kind)&&(int)effect["room"]==room){var contact=(JObject)effect.DeepClone();contact["kind"]="light";contact["visual_radius"]=kind=="cover_break"?0.4f:0.2f;P5AttackFx(contact);}
            if(kind=="rescue_blocked"&&actor==ownID){notice.text="队友已在救援此人，请保护或救援其他队友";noticeUntil=ViewTime+2;}
            if((kind=="light"&&(bool?)snapshot["v6_stage"]!=true||kind=="heavy")&&(int)effect["room"]==room)P5AttackFx(effect);
            if(actor==ownID&&(kind=="hit"||kind=="kill")){crosshair.color=DcP5Theme.Gold;p21HitUntil=ViewTime+0.12f;}
            if(kind=="hurt")flashes["player:"+actor]=ViewTime+0.07f;
            if(!showNumbers||(kind!="hit"&&kind!="hurt"&&kind!="environment_hit")||(int)effect["amount"]<=0||(int)effect["room"]!=room||!Visible(Point(effect)))return;
            var target=(string)effect["target"]??actor;var frame=(long)snapshot["world_frame_id"];
            var number=p5Numbers.FirstOrDefault(item=>item.Until>ViewTime&&item.Target==target&&item.Frame==frame);
            if(number!=null){number.Amount+=(int)effect["amount"];number.Label.text=number.Amount.ToString();return;}
            number=p5Numbers.FirstOrDefault(item=>item.Until<=ViewTime);
            if(number==null&&p5Numbers.Count<16){number=new FloatingP5{Label=Label("P5 damage",0,0,90,36,18)};number.Label.fontStyle=FontStyle.Bold;number.Label.transform.SetAsFirstSibling();p5Numbers.Add(number);}
            if(number==null)return;
            var point=cameraView.WorldToScreenPoint(Point(effect)+RoomOffset+Vector2.up*1.3f);number.Position=ScreenToUi(point);number.Target=target;number.Frame=frame;number.Amount=(int)effect["amount"];number.Until=ViewTime+0.45f;number.Label.text=number.Amount.ToString();number.Label.color=kind=="hurt"?new Color32(235,154,82,255):DcP5Theme.Ink;number.Label.gameObject.SetActive(true);
        }
        private void UpdateP5Feedback()
        {
            foreach(var effect in p5Fx){effect.Renderer.enabled=ViewTime<effect.Until;if(!effect.Renderer.enabled)continue;var progress=Mathf.Clamp01((ViewTime-effect.Begin)/(effect.Heavy?0.3f:0.15f));if(effect.Trace){var delta=effect.Position-effect.From;effect.Renderer.transform.position=(effect.Position+effect.From)/2+RoomOffset;effect.Renderer.transform.localScale=new Vector3(delta.magnitude,0.025f,1);effect.Renderer.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);effect.Renderer.color=new Color(0.65f,0.8f,0.72f,(1-progress)*0.18f);continue;}effect.Renderer.transform.position=effect.Position+RoomOffset;effect.Renderer.transform.localScale=Vector3.one*(effect.Heavy?effect.Radius*2*Mathf.Lerp(0.2f,1,progress):effect.Radius*2);effect.Renderer.color=effect.Heavy?new Color(0.92f,0.6f,0.4f,(1-progress)*0.7f):new Color(0.55f,0.77f,0.63f,(1-progress)*0.35f);}
            foreach(var number in p5Numbers)
            {
                number.Label.gameObject.SetActive(showNumbers&&ViewTime<number.Until);
                if(ViewTime>=number.Until)continue;
                Rect(number.Label.rectTransform,number.Position.x,number.Position.y-(0.45f-(number.Until-ViewTime))*30,90,36);
            }
        }
        private void P5AttackFx(JToken effect)
        {
            if(V8View&&(string)effect["kind"]=="heavy"&&(float?)effect["visual_radius"]>1)return;
            if(V7View&&(string)effect["kind"]=="light"&&effect["actor"]?.Type==JTokenType.String)return;
            var item=p5Fx.FirstOrDefault(value=>value.Until<=ViewTime);
            if(item==null&&p5Fx.Count<32){item=new FxP5{Renderer=new GameObject("P5 confirmed attack FX").AddComponent<SpriteRenderer>()};item.Renderer.transform.SetParent(root.transform);item.Renderer.sortingOrder=4;p5Fx.Add(item);}
            if(item==null)return;
            if(p5Ring==null){var texture=new Texture2D(64,64);texture.filterMode=FilterMode.Point;var pixels=new Color[4096];for(var horizontal=0;horizontal<64;horizontal++)for(var vertical=0;vertical<64;vertical++){var radius=Vector2.Distance(new Vector2(horizontal,vertical),new Vector2(31.5f,31.5f));pixels[vertical*64+horizontal]=radius>=28&&radius<=31?Color.white:Color.clear;}texture.SetPixels(pixels);texture.Apply();p5Ring=Sprite.Create(texture,new Rect(0,0,64,64),Vector2.one*0.5f,64);}
            item.Trace=false;item.Heavy=(string)effect["kind"]=="heavy";item.Radius=(float?)effect["visual_radius"]??(item.Heavy?2.3f:1.2f);item.Position=Point(effect);item.Begin=ViewTime+0.035f;item.Until=item.Begin+(item.Heavy?0.3f:0.15f);item.Renderer.sprite=item.Heavy?p5Ring:Sector(180);item.Renderer.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2((float?)effect["visual_aim_y"]??0,(float?)effect["visual_aim_x"]??1)*Mathf.Rad2Deg);
        }
        private void V6ConfirmedTrace(Vector2 from,Vector2 end){if(Vector2.Distance(from,end)>2)return;var fx=p5Fx.FirstOrDefault(f=>f.Until<=ViewTime);if(fx==null&&p5Fx.Count<32){fx=new FxP5{Renderer=new GameObject("Confirmed short path only").AddComponent<SpriteRenderer>()};fx.Renderer.transform.SetParent(root.transform);fx.Renderer.sortingOrder=9;p5Fx.Add(fx);}if(fx==null)return;fx.Trace=true;fx.Heavy=false;fx.From=from;fx.Position=end;fx.Begin=ViewTime;fx.Until=ViewTime+0.09f;fx.Renderer.sprite=square;}
        private void PauseP5Audio(bool paused){foreach(var voice in p5Voices){if(paused)voice.Pause();else voice.UnPause();}}
        private void CleanupP5Feedback(){CleanupV8Critical();if(p5Ring!=null){Destroy(p5Ring.texture);Destroy(p5Ring);}}
    }
}
