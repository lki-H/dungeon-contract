using System;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonContract
{
    public static class DcP5Theme
    {
        public static readonly Color Stone=new Color32(27,28,44,245);
        public static readonly Color Slate=new Color32(52,52,71,255);
        public static readonly Color Teal=new Color32(67,135,122,255);
        public static readonly Color Gold=new Color32(184,155,103,255);
        public static readonly Color Ink=new Color32(238,229,205,255);
        public static readonly Color Muted=new Color32(170,175,181,255);
        public static string ResultTitle(string reason)=>reason switch {"success"=>"远征完成   /   团队成功","team_wipe"=>"远征失败   /   队伍全灭","timeout"=>"远征结束   /   时间已到","team_abandoned"=>"本次远征已退出","system_abort"=>"本次远征技术中止","loading_failed"=>"入场未完成",_=>"本次远征结束"};
        public static void Place(RectTransform rect,float horizontal,float vertical,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(horizontal,-vertical);rect.sizeDelta=new Vector2(width,height);
        }
        public static RectTransform Reference(Transform parent)
        {
            var reference=new GameObject("P5 interface reference",typeof(RectTransform)).GetComponent<RectTransform>();reference.SetParent(parent,false);reference.anchorMin=reference.anchorMax=reference.pivot=Vector2.one*0.5f;reference.anchoredPosition=Vector2.zero;reference.sizeDelta=new Vector2(1280,720);return reference;
        }
        public static Image Panel(Transform parent,string name,float horizontal,float vertical,float width,float height,bool capture=false)
        {
            var image=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();image.transform.SetParent(parent,false);image.color=Stone;image.raycastTarget=capture;Place(image.rectTransform,horizontal,vertical,width,height);
            var border=new GameObject("Rune edge",typeof(RectTransform),typeof(Image)).GetComponent<Image>();border.transform.SetParent(image.transform,false);border.color=Gold;border.raycastTarget=false;Place(border.rectTransform,0,0,width,2);
            return image;
        }
        public static Text Text(Transform parent,string name,string value,float horizontal,float vertical,float width,float height,int size=18)
        {
            var text=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();text.transform.SetParent(parent,false);Place(text.rectTransform,horizontal,vertical,width,height);text.font=Resources.Load<Font>("DungeonContract/Fonts/NotoSansSC");text.fontSize=Mathf.RoundToInt(size*DcP5Settings.Current.TextScale);text.text=value;text.color=Ink;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.supportRichText=false;text.fontStyle=FontStyle.Bold;return text;
        }
        public static Button Button(Transform parent,string name,string caption,float horizontal,float vertical,float width,float height,Action clicked)
        {
            var panel=Panel(parent,name,horizontal,vertical,width,height,true);panel.color=Slate;var button=panel.gameObject.AddComponent<Button>();var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(0.65f,0.95f,0.85f);colors.selectedColor=colors.highlightedColor;colors.pressedColor=new Color(0.4f,0.7f,0.6f);colors.disabledColor=new Color(0.4f,0.4f,0.4f);button.colors=colors;button.targetGraphic=panel;button.onClick.AddListener(()=>clicked());
            var text=Text(panel.transform,"Caption",caption,8,3,width-16,height-6,16);text.alignment=TextAnchor.MiddleCenter;
            return button;
        }
        public static Text ScrollText(Transform parent,string name,string value,float width,float height,int size=18)
        {
            var area=new GameObject(name+" scroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));area.transform.SetParent(parent,false);Place((RectTransform)area.transform,0,0,width,height);area.GetComponent<Image>().color=Color.clear;
            var text=Text(area.transform,name,value,0,0,width-22,height,size);text.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=area.GetComponent<ScrollRect>();scroll.viewport=(RectTransform)area.transform;scroll.content=text.rectTransform;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
            return text;
        }
    }
}
