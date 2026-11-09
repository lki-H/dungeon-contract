using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private bool p4View;
        private bool p4Open;
        private Text p4Status;
        private readonly Button[] p4Buttons=new Button[3];
        public bool P4Open=>p4Open;
        public Action<bool> P4VisibilityChanged;
        public void ConfigureP4(Action<int> choose)
        {
            p4View=true;p4Status=Label("Growth status",18,616,1220,18,14);
            Button("成长(Tab)",820,140,190,30,ToggleP4);
            for(var index=0;index<3;index++)
            {
                var selected=index;Button("成长 "+(index+1),220+index*290,365,280,145,()=>choose(selected));
                p4Buttons[index]=UiRoot.GetChild(UiRoot.childCount-1).GetComponent<Button>();
                p4Buttons[index].gameObject.SetActive(false);
            }
        }
        public void ToggleP4(){if(!p4Open&&(snapshot==null||(int?)snapshot["players"].First(item=>(string)item["player_id"]==ownID)["choice_node"]<0))return;p4Open=!p4Open;P4VisibilityChanged?.Invoke(p4Open);if(snapshot!=null)ReceiveP4(snapshot["players"].First(item=>(string)item["player_id"]==ownID));}
        private static string ModuleName(string id)
        {
            switch(id){case "swift":return "迅捷闪避";case "frost":return "冰霜附着";case "battle_rune":return "战意符文";case "ward":return "符文护盾";case "vitality":return "坚韧符印";case "ranger_pierce":return "穿透模块";case "ranger_fan":return "扇形模块";case "ranger_tension":return "疾速机括";case "runesmith_chain":return "连锁模块";case "runesmith_twin":return "双重线圈";case "runesmith_heavy":return "重型模块";default:return id;}
        }
        private string ModuleDescription(string id)
        {
            if(V7View){if(id=="ranger_pierce")return "弩最多命中2→3名\n短刃1.333→1.567格，不穿墙";if(id=="runesmith_heavy")return "炮塔伤害150%向下取整\n重砸地波2.875→3.313格";}
            if((bool?)snapshot?["v5_stage"]==true){if(id=="ranger_tension")return "弹匣8→10、装填900→700ms\n短刃周期名义85%，量化约267ms";if(id=="runesmith_heavy")return "炮塔伤害150%向下取整\n重砸地波半径+0.35格";if(id=="runesmith_chain")return "符弹/塔弹/锤每次出手首次正伤\n3格内连锁一敌70%，不递归";if(id=="ranger_fan")return "基础3矢/齐射9矢，各65%\n每箭独立命中；短刃角度+30°/伤80%";}
            switch(id){case "swift":return "闪避冷却 3秒→2.4秒";case "frost":return "合法命中减速20%/2秒\nBoss减速5%，不取消承诺";case "battle_rune":return "成功主动后伤害+15%/3秒\n刷新不叠加，双塔每组首次触发";case "ward":return "成功主动后本人15盾/3秒\n刷新，不叠加";case "vitality":return "本局生命上限+20\n存活时立即恢复最多20，不救倒地";case "ranger_pierce":return DcV7Profile.Active?"弩最多命中2→3名\n短刃范围约+0.233格，不穿墙":"弩最多命中2→3名\n近战范围+0.35格，不穿墙";case "ranger_fan":return "三箭扇射，各65%伤害\n近战角度+30°、伤害80%";case "ranger_tension":return DcV6Profile.Modern?"弹匣8→10、装填900→700ms\n短刃普攻周期缩短15%":"弹匣8→10、装填900→700ms\n满蓄力900→700ms";case "runesmith_chain":return "符弹/锤首次合法命中后\n3格内连锁一敌，70%伤害";case "runesmith_twin":return DcV8Profile.Active?"E分两次部署，每台9伤/8秒\n第二次后冷却12秒，近战周期90%":"一次双塔，每台9基础伤害\n近战间隔90%，可叠重型";case "runesmith_heavy":return DcV7Profile.Active?"炮塔伤害150%，最终向下取整\n重砸波半径约+0.438格":"炮塔伤害150%向下取整\n重击范围+0.35格";default:return "";}
        }
        private void ReceiveP4(JToken own)
        {
            if(!p4View)return;
            title.text="P4 成长与支路 · "+geometry["rooms"][room]["id"];
            Rect(reloadBar.rectTransform,18,639,reloadBar.rectTransform.sizeDelta.x,6);
            var candidates=own["candidates"] as JArray??new JArray();var available=(int?)own["choice_node"]>=0&&string.IsNullOrEmpty((string)snapshot["outcome"]);
            if(!available&&p4Open){p4Open=false;P4VisibilityChanged?.Invoke(false);}
            p4Status.text="已安装："+string.Join("、",((JArray)own["upgrades"]).Select(item=>ModuleName((string)item)))+(available?"  待选节点"+((int)own["choice_node"]+1)+"："+Seconds(own["choice_remaining_ms"])+"后默认第一项（Tab/1/2/3；世界继续）":"")+( (int?)own["pending_shield"]>0?"  支路护盾待下次战斗":"");
            if(room==7&&(string)snapshot["rooms"][7]["status"]=="cleared")objective.text="补给：靠近房内补给标记按J，每人恢复30；满血不消耗；原路返回主线";
            if(room==6&&(string)snapshot["rooms"][6]["status"]=="cleared")objective.text="符文支路完成：已获得待触发护盾；原路返回主线";
            for(var index=0;index<3;index++){p4Buttons[index].gameObject.SetActive(p4Open&&available);if(index<candidates.Count){var id=(string)candidates[index];p4Buttons[index].GetComponentInChildren<Text>().text=(index+1)+" · "+ModuleName(id)+"\n"+ModuleDescription(id)+(index==0?"\n默认项":"");}}
        }
    }
}
