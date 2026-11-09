using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View
    {
        private bool p5HudReady;
        private Image p5Health;
        private Image p5Shield;
        private Text p5Team;
        private Text p5GrowthNotice;
        private Image p5StowedWeapon;
        private Text p5StowedText;
        private bool p5ResultReady;
        private readonly Image[] p5Growth=new Image[3];
        private GameObject p5ResultRoot;
        private readonly System.Collections.Generic.Dictionary<string,JToken> p5Roster=new System.Collections.Generic.Dictionary<string,JToken>();
        internal void P5Roster(JArray members){p5Roster.Clear();foreach(var member in members)p5Roster[(string)member["player_id"]]=member;}
        private static JToken[] P5TeamEntries(JObject value)=>((JArray)value["players"]).Concat(value["team_rooms"] as JArray??new JArray()).GroupBy(item=>(string)item["player_id"]).Select(group=>group.First()).Take(4).ToArray();

        private void ConfigureP5Hud()
        {
            p5HudReady=true;
            foreach(var image in canvas.GetComponentsInChildren<Image>())
                if(image.name=="V5 resources"||image.name=="Current weapon panel")
                {
                    image.color=DcP5Theme.Stone;var border=DcP5Theme.Panel(image.transform,"Stone inset",0,0,image.rectTransform.sizeDelta.x,2);border.color=DcP5Theme.Gold;
                }
            resources.fontSize=17;resources.color=DcP5Theme.Ink;objective.color=DcP5Theme.Ink;title.color=DcP5Theme.Gold;
            var heading=DcP5Theme.Panel(UiRoot,"P5 heading",330,0,710,108);heading.transform.SetAsFirstSibling();heading.color=new Color(0.07f,0.08f,0.11f,0.9f);
            p5Health=V5Image("P5 health",32,78,256,7,new Color32(188,75,72,255));p5Shield=V5Image("P5 shield",32,86,0,4,new Color32(141,196,160,255));
            for(var index=0;index<3;index++)
            {
                Rect(v5CooldownLabels[index].rectTransform,31+index*88,138,84,27);v5CooldownLabels[index].fontSize=14;v5CooldownLabels[index].color=DcP5Theme.Ink;
                var parent=UiRoot.Find("Growth base "+index).GetComponent<Image>();parent.color=DcP5Theme.Slate;Rect(parent.rectTransform,32+index*86,172,38,32);
                p5Growth[index]=V5Image("P5 installed "+index,32+index*86,172,32,32,Color.white);v5GrowthLabels[index].enabled=false;
            }
            v5WeaponHandle.enabled=false;Rect(v5WeaponHead.rectTransform,1046,626,58,58);Rect(v5WeaponLabel.rectTransform,1110,625,150,70);v5WeaponLabel.fontSize=15;
            p5StowedWeapon=V5Image("P5 stowed weapon",1000,646,32,32,Color.white);p5StowedText=Label("P5 stowed state",920,680,120,22,12);
            var team=DcP5Theme.Panel(UiRoot,"P5 party",18,480,300,145);team.transform.SetAsFirstSibling();p5Team=Label("P5 teammates",30,491,280,132,15);
            p5GrowthNotice=Label("P5 growth notice",340,614,660,52,17);p5GrowthNotice.color=DcP5Theme.Gold;
            if(dangerText!=null){Rect(dangerText.rectTransform,340,150,660,70);dangerText.fontSize=17;}
            if(p4Status!=null)p4Status.enabled=false;
            foreach(var button in canvas.GetComponentsInChildren<Button>(true))
            {
                if(button.name=="成长(Tab)"){Rect(button.GetComponent<RectTransform>(),340,670,190,34);button.GetComponent<Image>().color=DcP5Theme.Slate;}
                else if(button.name.StartsWith("成长 ",StringComparison.Ordinal))
                {
                    var index=Array.IndexOf(p4Buttons,button);if(index>=0){Rect(button.GetComponent<RectTransform>(),150+index*330,350,316,240);button.GetComponent<Image>().color=DcP5Theme.Stone;var label=button.GetComponentInChildren<Text>();label.fontSize=19;label.horizontalOverflow=HorizontalWrapMode.Wrap;}
                }
            }
            Rect(reloadBar.rectTransform,1048,612,200,4);
        }
        private void DrawP5Hud(JToken own)
        {
            if(!p5HudReady)ConfigureP5Hud();
            var textScale=DcP5Settings.Current.TextScale;resources.fontSize=Mathf.RoundToInt(17*textScale);objective.fontSize=Mathf.RoundToInt(18*textScale);title.fontSize=Mathf.RoundToInt(22*textScale);Rect(objective.rectTransform,340,48,660,60);
            resources.fontStyle=objective.fontStyle=title.fontStyle=FontStyle.Bold;
            title.text=((bool)snapshot["practice"]?"无奖励练习":"地牢契约")+"  /  "+RoomP5Name(room);
            resources.text="生命 "+own["health"]+"/"+own["max_health"]+"  护盾 "+own["shield"]+"\n"+((int)own["magazine_max"]==0?"符文弹 · 无限":own["magazine"]+"/"+own["magazine_max"]+" 发")+((long)own["reload_remaining_ms"]>0?" · 装填中":"");
            var reload=(long)own["reload_remaining_ms"];Rect(reloadBar.rectTransform,1048,612,reload>0?200*Mathf.Clamp01(1-reload/((int)own["magazine_max"]>8?700f:900f)):0,4);
            var life=(string)own["life_state"];var active=life=="alive";
            if((bool)snapshot["practice"])objective.text="安全练习 · "+(p5Controls?.Display("Ranged")??"左键")+"普攻 · "+(p5Controls?.Display("Charge")??"右键")+"仅锤蓄力\n练习目标不推进合同或奖励";else if(life=="downed")objective.text=objective.text.Replace("按住J","按住其交互键");
            if(active&&!(bool)snapshot["practice"])
            {
                var state=snapshot["rooms"][room];objective.text=(string)state["status"]=="cleared"?"房间完成 · 沿已开放门探索；地面陷阱仍有效":(string)state["status"]=="warning"?"战斗即将开始 · 观察出生标记":"剩余威胁 "+state["enemies_remaining"]+((bool?)state["mechanism_complete"]==true?" · 机关完成":geometry["rooms"][room]["objective"] is JArray?" · 靠近机关按住 "+(p5Controls?.Display("Interact")??"J"):"");
            }
            if(room==5&&active)objective.text="击败镇守者完成远征 · 不必清空召唤物";
            var hp=Mathf.Clamp01((int)own["health"]/(float)Math.Max(1,(int)own["max_health"]));Rect(p5Health.rectTransform,32,78,256*hp,7);Rect(p5Shield.rectTransform,32,86,256*Mathf.Clamp01((int)own["shield"]/30f),4);
            var expanded=textScale>1.25f;var shift=expanded?36:0;Rect(resources.rectTransform,32,24,275,expanded?84:56);Rect(p5Health.rectTransform,32,78+shift,256*hp,7);Rect(p5Shield.rectTransform,32,86+shift,256*Mathf.Clamp01((int)own["shield"]/30f),4);UiRoot.Find("V5 resources").GetComponent<RectTransform>().sizeDelta=new Vector2(300,expanded?286:206);
            var ranger=(string)own["loadout_id"]=="ranger";var ids=new[]{"dodge",ranger?"volley":"turret",ranger?"wind":"field"};var actions=new[]{"Dodge","Primary","Secondary"};var times=new[]{(long)own["dodge_remaining_ms"],(long)own["ability_remaining_ms"],(long)own["secondary_remaining_ms"]};
            for(var index=0;index<3;index++)
            {
                var background=UiRoot.Find("Cooldown base "+index).GetComponent<Image>();background.sprite=DcP5Art.Get("icon."+ids[index]);background.color=Color.white;v5Cooldowns[index].color=new Color(0.07f,0.08f,0.11f,0.85f);
                Rect(v5WeaponHead.rectTransform,1046,626,58,58);
                v5CooldownLabels[index].text=(p5Controls?.Display(actions[index])??new[]{"Space","E","F"}[index])+" · "+(times[index]>0?Math.Ceiling(times[index]/1000d)+"s":"就绪");
                Rect(background.rectTransform,32+index*88,92+shift,42,42);Rect(v5Cooldowns[index].rectTransform,32+index*88,92+shift,42,42);Rect(v5CooldownLabels[index].rectTransform,31+index*88,138+shift,84,expanded?64:27);v5CooldownLabels[index].fontSize=Mathf.RoundToInt(14*textScale);if(expanded)v5CooldownLabels[index].text=v5CooldownLabels[index].text.Replace(" · ","\n");Rect(p5Growth[index].rectTransform,32+index*86,expanded?248:172,32,32);Rect(UiRoot.Find("Growth base "+index).GetComponent<RectTransform>(),32+index*86,expanded?248:172,38,32);
                var upgrades=(JArray)own["upgrades"];p5Growth[index].enabled=index<upgrades.Count;if(index<upgrades.Count)p5Growth[index].sprite=DcP5Art.Get("icon."+(string)upgrades[index]);
            }
            var weapon=(string)own["active_weapon_id"];v5WeaponHead.sprite=DcP5Art.Weapon(weapon);v5WeaponHead.color=Color.white;v5WeaponHandle.enabled=false;
            var melee=(bool)own["melee_equipped"];var other=ranger?(melee?"repeating_crossbow":"short_blade"):(melee?"rune_bolt":"rune_hammer");p5StowedWeapon.sprite=DcP5Art.Get("icon."+other);var ready=(long)own[melee?"ranged_ready_ms":"melee_ready_ms"]-(long)snapshot["elapsed_ms"];p5StowedText.text=ready>0?"收起 · "+(ready/1000f).ToString("0.0")+"s":"收起 · 就绪";
            v5WeaponLabel.text=v5WeaponLabel.text.Replace("Q切换",(p5Controls?.Display("Switch")??"Q")+"切换");
            var teamEntries=P5TeamEntries(snapshot);p5Team.text="队伍   /   共享增援 "+snapshot["reinforcement_remaining"]+"\n"+string.Join("\n",teamEntries.OrderBy(item=>(int?)item["slot"]??(p5Roster.TryGetValue((string)item["player_id"],out var member)?(int)member["slot"]:4)).Select(item=>
            {
                var id=(string)item["player_id"];var slot=(int?)item["slot"]??(p5Roster.TryGetValue(id,out var member)?(int)member["slot"]:-1);var prefix="["+(slot>=0?(slot+1).ToString():"?")+"] ";
                if(item["health"]==null)return prefix+RoomP5Name((int)item["room_id"])+" · "+((string)item["life_state"]=="downed"?"倒地待救":"队友");
                return prefix+(id==ownID?"本人":(string)item["loadout_id"]=="ranger"?"游侠":"工匠")+"  "+((string)item["life_state"]=="alive"?item["health"]+"HP":(string)item["life_state"]=="downed"?"倒地待救":(string)item["life_state"]=="awaiting_respawn"?"增援已预留":"死亡等待")+((bool?)item["connected"]==false?" · 断线":"");
            }));
            var panelHeight=Mathf.RoundToInt((36+teamEntries.Length*26)*textScale);var teamPanel=UiRoot.Find("P5 party").GetComponent<RectTransform>();Rect(teamPanel,18,626-panelHeight,300,panelHeight);Rect(p5Team.rectTransform,30,637-panelHeight,280,panelHeight-12);p5Team.fontSize=Mathf.RoundToInt(15*textScale);
            var pending=(int?)own["choice_node"]>=0;p5GrowthNotice.text=pending?"成长待选 · Tab打开 · "+Math.Ceiling((long)own["choice_remaining_ms"]/1000d)+"s后默认第一项，世界继续":(int?)own["pending_shield"]>0?"支路护盾已领取 · 下次进入战斗时触发":"";
            if(p3Contract!=null)p3Contract.enabled=false;
            if(p5ResultReady&&p5ResultRoot!=null)p5ResultRoot.transform.SetAsLastSibling();
            if(V5MenuVisible){p5MenuRoot?.transform.SetAsLastSibling();if(!visualPaused&&dangerText!=null){Rect(dangerText.rectTransform,80,2,1100,36);dangerText.transform.SetAsLastSibling();}}else if(dangerText!=null)Rect(dangerText.rectTransform,340,150,660,70);
            DrawP5Interaction();if(V8View)DrawV8Hud(own);
        }
        private static string RoomP5Name(int index)=>new[]{"遗迹入口","断柱前厅","机关回廊","镇守庭","内殿","镇守者圣所","符文支路","补给支路"}.ElementAtOrDefault(index)??"连接走廊";
        private void P5Result(JToken state,JToken cards,string playerID)
        {
            if(p5ResultReady)return;p5ResultReady=true;result.text="";
            V5MenuVisible=false;p5Controls?.FinishRun();if(p5MenuRoot!=null)p5MenuRoot.SetActive(false);
            var panel=DcP5Theme.Panel(UiRoot,"P5 confirmed result",105,45,1070,620,true);p5ResultRoot=panel.gameObject;
            var successful=(string)state["end_reason"]=="success";var elapsed=(long?)state["elapsed_ms"];var timeText=elapsed.HasValue?elapsed.Value/60000+":"+(elapsed.Value/1000%60).ToString("D2"):"正在确认时间";
            DcP5Theme.Text(panel.transform,"Result title",DcV6Profile.Active?DcP5Theme.ResultTitle((string)state["end_reason"]):successful?"契约达成   /   团队成功":"本次远征结束",24,16,1000,70,27);
            var own=state["participants"].First(item=>(string)item["player_id"]==playerID);var grants=(state["reward_grants"] as JArray??new JArray()).Where(item=>item["player_id"]==null||(string)item["player_id"]==playerID);
            var taskKey=(string)own["task_key"];var taskText=string.IsNullOrEmpty(taskKey)?"未选择个人合同":P3TaskName(taskKey)+" · "+((bool?)own["task_completed"]==true?"已完成":"未完成");
            DcP5Theme.Text(panel.transform,"Own reward","作战时间 "+timeText+"     本人奖励 "+grants.Sum(item=>(long)item["amount"])+"\n"+taskText,24,100,1020,100,19);
            if(cards is JArray rows)
            {
                var index=0;foreach(var card in rows.OrderBy(item=>(int)item["slot"]))
                {
                    var background=DcP5Theme.Panel(panel.transform,"Contribution "+index,24+index*257,220,246,270);background.color=DcP5Theme.Slate;
                    var participant=state["participants"].First(item=>(string)item["player_id"]==(string)card["player_id"]);var kit=(string)card["loadout_id"]=="ranger"?"游侠":"工匠";
                    var category=(string)participant["task_category"]??((string)card["player_id"]==playerID?(string)participant["task_key"]:null);
                    DcP5Theme.Text(background.transform,"Contribution","槽位 "+((int)card["slot"]+1)+((string)card["player_id"]==playerID?" · 本人":"")+"\n"+kit+" · "+P3TaskName(category)+"\n\n伤害  "+card["effective_damage"]+"\n击杀  "+(card["kills"]??card["eligible_kills"])+"   助攻 "+card["assists"]+"\n救援  "+card["rescues"]+((bool?)card["totals_complete"]==false?"\n仅已确认贡献":""),12,12,222,246,17);index++;
                }
            }
            DcP5Theme.Text(panel.transform,"Result hint","结果由服务可靠确认。贡献按槽位排列，没有个人综合分。",24,510,1010,48,16);
            DcP5Theme.Button(panel.transform,"Close result","返回 / 结束本次游戏",746,560,290,44,()=>Exit?.Invoke());
            var capture=DcBootstrap.Argument("-dc-p3-result-capture");if(!string.IsNullOrEmpty(capture))StartCoroutine(CaptureP3Result(capture));
        }
    }
}
