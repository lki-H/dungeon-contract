using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private Text p5InteractionLabel;
        private Image p5InteractionFill;
        private Image p5InteractionBase;
        private string p5Operator;
        private int p5OperatorHealth;
        private int p5OperatorShield;
        private uint p5OperatorWeapon;
        private float p5InterruptUntil;
        private void DrawP5Interaction()
        {
            if(p5InteractionLabel==null){p5InteractionLabel=Label("P5 interaction",400,532,520,42,18);p5InteractionLabel.color=DcP5Theme.Ink;p5InteractionBase=V5Image("P5 interaction base",400,577,480,6,DcP5Theme.Slate);p5InteractionFill=V5Image("P5 interaction fill",400,577,0,6,DcP5Theme.Teal);}
            var participants=(JArray)snapshot["players"];var own=participants.First(item=>(string)item["player_id"]==ownID);var state=snapshot["rooms"][room];var objectivePoint=geometry["rooms"][room]["objective"] as JArray;
            var operatorState=participants.Where(item=>(int)item["room_id"]==room&&(int)item["health"]>0).OrderBy(item=>(int)item["slot"]).FirstOrDefault(item=>(long)item["interaction_remaining_ms"]>0);
            var rescue=(long?)own["rescue_remaining_ms"]??0;var showMechanism=objectivePoint!=null&&(bool?)state["mechanism_complete"]!=true;
            var range=Draw("weapon:p5.mechanism.range",showMechanism?RoomGeometry.Point(objectivePoint):Vector2.zero,new Color(0.26f,0.53f,0.48f,0.12f),Vector2.one*2*((float?)geometry["parameters"]?["player"]?["interaction_range_milli_tiles"]??1500)/1000,2,true);range.enabled=showMechanism;
            p5InteractionBase.enabled=p5InteractionFill.enabled=false;
            if(rescue>0)
            {
                p5InteractionLabel.text="正在救援 · "+(rescue/1000f).ToString("0.0")+"s · 真实受伤会中断";var duration=(float?)geometry["parameters"]?["player"]?["rescue_ms"]??3000;p5InteractionBase.enabled=p5InteractionFill.enabled=true;Rect(p5InteractionFill.rectTransform,400,577,480*Mathf.Clamp01(1-rescue/duration),6);return;
            }
            if(showMechanism&&operatorState!=null)
            {
                p5Operator=(string)operatorState["player_id"];p5OperatorHealth=(int)operatorState["health"];p5OperatorShield=(int)operatorState["shield"];p5OperatorWeapon=(uint)operatorState["weapon_revision"];
                var duration=(float?)geometry["parameters"]?["player"]?["interaction_ms"]??4000;var fraction=Mathf.Clamp01(1-(long)operatorState["interaction_remaining_ms"]/duration);p5InteractionLabel.text="槽位 "+((int)operatorState["slot"]+1)+" 操作机关 · "+Mathf.RoundToInt(fraction*100)+"% · 请保护操作者";p5InteractionBase.enabled=p5InteractionFill.enabled=true;Rect(p5InteractionFill.rectTransform,400,577,480*fraction,6);return;
            }
            if(p5Operator!=null)
            {
                var previous=participants.FirstOrDefault(item=>(string)item["player_id"]==p5Operator);var completed=(bool?)state["mechanism_complete"]==true;
                p5InteractionLabel.text=completed?"机关已完成 · 清敌条件仍独立裁决":previous==null?"操作者离开，操作中断":(int)previous["health"]<p5OperatorHealth||(int)previous["shield"]<p5OperatorShield?"受到真实伤害，机关操作中断":(uint)previous["weapon_revision"]!=p5OperatorWeapon?"切换武器，机关操作中断":"机关操作中断，请重新按住交互";p5Operator=null;p5InterruptUntil=ViewTime+2;
            }
            if(ViewTime>=p5InterruptUntil)p5InteractionLabel.text="";
        }
    }
}
