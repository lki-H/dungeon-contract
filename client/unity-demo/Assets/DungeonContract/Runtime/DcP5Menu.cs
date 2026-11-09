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
        private DcP1Controls p5Controls;
        private GameObject p5MenuRoot;
        private readonly List<GameObject> p5Pages=new List<GameObject>();
        private Text p5MenuStatus;
        private Text p5Bindings;
        private Text p5Contract;
        private int p5Page;
        private Text p5Help;
        private readonly List<Action> p5Refresh=new List<Action>();
        private int p5HelpUpgradeCount=-1;
        private bool p5HelpDirty=true;
        public void AttachP5Controls(DcP1Controls controls){if(DcP5Settings.Active){p5Controls=controls;StartP5UiProbe();}}

        private void ConfigureP5Menu()
        {
            v5Menu.gameObject.SetActive(false);
            var panel=DcP5Theme.Panel(UiRoot,"P5 menu",75,40,1130,640,true);p5MenuRoot=panel.gameObject;
            DcP5Theme.Text(panel.transform,"Heading","地牢契约   /   远征手册",24,12,1000,64,25);
            p5MenuStatus=DcP5Theme.Text(panel.transform,"World state","",24,80,1060,36,16);
            var tabs=new[]{"操作与目标","显示与音频","按键绑定","个人合同","资源与许可"};
            for(var index=0;index<tabs.Length;index++){var selected=index;DcP5Theme.Button(panel.transform,"Tab "+index,tabs[index],24+index*216,120,204,42,()=>ShowP5Page(selected));}
            for(var index=0;index<tabs.Length;index++)
            {
                var page=new GameObject("Page "+index,typeof(RectTransform));page.transform.SetParent(panel.transform,false);DcP5Theme.Place((RectTransform)page.transform,24,178,1080,380);p5Pages.Add(page);
            }
            p5Help=DcP5Theme.ScrollText(p5Pages[0].transform,"Controls","",1080,380,19);
            BuildP5Settings(p5Pages[1].transform);
            p5Bindings=DcP5Theme.Text(p5Pages[2].transform,"Rebind status","选择一项后按新键；Esc取消。Esc、Tab、1/2/3和滚轮保留。",0,0,1080,60,17);
            var keys=new[]{("Move",1,"向上"),("Move",2,"向下"),("Move",3,"向左"),("Move",4,"向右"),("Ranged",0,"普通攻击"),("Charge",0,"重锤蓄力"),("Dodge",0,"闪避"),("Primary",0,"第一主动"),("Secondary",0,"第二主动"),("Switch",0,"切换武器"),("Reload",0,"装填"),("Interact",0,"交互/救援"),("Mark",0,"队内标记")};
            for(var index=0;index<keys.Length;index++)
            {
                var binding=keys[index];var button=DcP5Theme.Button(p5Pages[2].transform,"Rebind "+binding.Item1+binding.Item2,binding.Item3, index%3*360,75+index/3*62,344,52,()=>p5Controls?.BeginRebind(binding.Item1,binding.Item2));
                p5Refresh.Add(()=>button.GetComponentInChildren<Text>().text=binding.Item3+"  ["+(p5Controls?.Display(binding.Item1,binding.Item2)??"—")+"]");
            }
            DcP5Theme.Button(p5Pages[2].transform,"Reset bindings","恢复默认按键",360,323,344,52,()=>{if(p5Controls==null||p5Controls.Rebinding)return;DcP5Settings.Current.ResetBindings(p5Controls.Asset);RefreshP5Settings();});
            p5Contract=DcP5Theme.ScrollText(p5Pages[3].transform,"Own contract","本次练习不推进合同，也没有奖励或成绩。",1060,380,20);
            DcP5Theme.Text(p5Pages[4].transform,"Credits","DUNGEON CONTRACT · 演示作品\n\n原创像素资源与合成音频：随包资源清单逐项记录来源与指纹。\n中文字体：Noto Sans SC，SIL Open Font License 1.1。\nUnity / NGO / Transport / Input System：对应许可随完整包提供。\n\n素材、工程验证、真人手感与跨电脑网络验收分别记录。\n固定试炼仅供好友比较，无个人总分或全球榜。",0,0,1060,360,19);
            DcP5Theme.Button(panel.transform,"Resume","返回游戏 · Esc",24,580,310,42,()=>{if(p5Controls==null||p5Controls.Rebinding)return;p5Controls.SetMenu(false);p5Controls.MenuChanged?.Invoke(false);});
            DcP5Theme.Button(panel.transform,"Exit","退出本次游戏",820,580,280,42,()=>{if(p5Controls?.Rebinding!=true)Exit?.Invoke();});
            ShowP5Page(0);panel.gameObject.SetActive(false);
            DcP5Settings.Current.Changed+=ApplyP5Settings;
            ApplyP5Settings();
        }
        private void ShowP5Page(int index)
        {
            if(p5Controls?.Rebinding==true)return;
            p5Page=index;for(var page=0;page<p5Pages.Count;page++)p5Pages[page].SetActive(page==index);RefreshP5Settings();
        }
        private void RefreshP5Settings(){foreach(var refresh in p5Refresh)refresh();}
        private void BuildP5Settings(Transform page)
        {
            var settings=DcP5Settings.Current;
            P5Option(page,"字号",0,0,()=>Mathf.RoundToInt(settings.TextScale*100)+"%",()=>{settings.TextScale=settings.TextScale>=1.5f?1:settings.TextScale+0.25f;settings.Save();});
            P5Option(page,"攻击提示大小",0,78,()=>Mathf.RoundToInt(settings.WarningScale*100)+"%",()=>{settings.WarningScale=settings.WarningScale>=2?1:settings.WarningScale+0.5f;settings.Save();});
            P5Option(page,"准心",0,156,()=>new[]{"十字","圆点","高对比"}[settings.Reticle],()=>{settings.Reticle=(settings.Reticle+1)%3;settings.Save();});
            P5Option(page,"减少闪白",0,234,()=>settings.ReducedFlash?"开启":"关闭",()=>{settings.ReducedFlash=!settings.ReducedFlash;settings.Save();});
            P5Option(page,"伤害数字",0,312,()=>settings.DamageNumbers?"显示":"隐藏",()=>{settings.DamageNumbers=!settings.DamageNumbers;settings.Save();});
            P5Option(page,"音效音量",550,0,()=>Mathf.RoundToInt(settings.EffectsVolume*100)+"%",()=>{settings.EffectsVolume=settings.EffectsVolume>=0.99f?0:Mathf.Min(1,settings.EffectsVolume+0.1f);settings.Save();});
            P5Option(page,"氛围音量",550,78,()=>Mathf.RoundToInt(settings.MusicVolume*100)+"%",()=>{settings.MusicVolume=settings.MusicVolume>=0.99f?0:Mathf.Min(1,settings.MusicVolume+0.1f);settings.Save();});
            P5Option(page,"镜头震动",550,156,()=>settings.Shake==0?"关闭":Mathf.RoundToInt(settings.Shake*100)+"%",()=>{settings.Shake=settings.Shake>=1?0:settings.Shake+0.25f;settings.Save();});
            P5Option(page,"显示模式",550,234,()=>Screen.fullScreen?"全屏":"窗口",()=>Screen.fullScreen=!Screen.fullScreen);
            DcP5Theme.Text(page,"Saved","设置自动保存在本机，不保存密码或令牌。\n世界视野固定，窗口缩放不增加可见范围。",550,306,510,72,16);
        }
        private void P5Option(Transform parent,string caption,float horizontal,float vertical,Func<string> value,Action click)
        {
            var button=DcP5Theme.Button(parent,caption,"",horizontal,vertical,510,62,()=>{click();RefreshP5Settings();});p5Refresh.Add(()=>button.GetComponentInChildren<Text>().text=caption+"    "+value());
        }
        private void ApplyP5Settings()
        {
            var settings=DcP5Settings.Current;shake=settings.Shake>0;showNumbers=settings.DamageNumbers;source.volume=settings.EffectsVolume;
            p5HelpDirty=true;
            foreach(var voice in p5Voices)voice.volume=settings.EffectsVolume*0.65f;if(p5Music!=null)p5Music.volume=settings.MusicVolume;
            if(p5MenuRoot!=null)foreach(var label in p5MenuRoot.GetComponentsInChildren<Text>(true)){var baseSize=label.name=="Heading"?25:label.name=="Own contract"?20:label.name=="Controls"?19:label.name=="Credits"?19:16;label.fontSize=Mathf.RoundToInt(baseSize*settings.TextScale);}
            crosshair.fontSize=settings.Reticle==2?30:22;crosshair.text=settings.Reticle==1?"·":"+";crosshair.alignment=TextAnchor.MiddleCenter;var outline=crosshair.GetComponent<Outline>()??crosshair.gameObject.AddComponent<Outline>();outline.effectColor=Color.black;outline.effectDistance=Vector2.one*1.5f;outline.enabled=settings.Reticle==2;RefreshP5Settings();
        }
        private void UpdateP5Menu()
        {
            if(p5MenuRoot==null)ConfigureP5Menu();
            p5MenuRoot.SetActive(V5MenuVisible);
            if(!V5MenuVisible)return;
            p5MenuRoot.transform.SetAsLastSibling();
            p5MenuStatus.text=visualPaused?"已暂停 · 世界、冷却、选择期限与场景音频冻结":"世界仍在继续 · 多人、试炼及等待服务确认时菜单不提供暂停保护";
            if(p5Controls!=null)
            {
                var upgrades=(JArray)snapshot["players"].First(item=>(string)item["player_id"]==ownID)["upgrades"];
                if(p5HelpDirty||p5HelpUpgradeCount!=upgrades.Count)
                {
                    p5HelpDirty=false;p5HelpUpgradeCount=upgrades.Count;
                    p5Help.text="移动  "+string.Join(" / ",Enumerable.Range(1,4).Select(index=>p5Controls.Display("Move",index)))+"     鼠标完全手动瞄准\n\n"+p5Controls.Display("Ranged")+"：当前武器普攻，按住连续     "+p5Controls.Display("Charge")+"：仅符文锤蓄力\n"+p5Controls.Display("Switch")+" / 滚轮：切换；切换后松开再按攻击\n"+p5Controls.Display("Dodge")+"：闪避     "+p5Controls.Display("Primary")+" / "+p5Controls.Display("Secondary")+"：两主动\n"+p5Controls.Display("Reload")+"：装填     "+p5Controls.Display("Interact")+"：机关、补给、救援（按住）\nTab：成长选择    1 / 2 / 3：选择    "+p5Controls.Display("Mark")+"：队内标记\n\n完成清敌与机关后开放出口；最后必需目标完成即结算。\n\n已安装成长（每项上限1次）\n"+(upgrades.Count==0?"暂无":string.Join("\n\n",upgrades.Select(item=>ModuleName((string)item)+"\n"+ModuleDescription((string)item))));
                    var ranger=(string)snapshot["players"].First(item=>(string)item["player_id"]==ownID)["loadout_id"]=="ranger";p5Help.text+="\n\n方案技能\n"+(ranger?"齐射：继承弩的多矢爆发，并附独立于Space的短闪。\n风缚：跟随推开普通敌人并清敌弹；不免除精英/Boss本体或陷阱。":DcV8Profile.Active?"炮塔：脚下部署；双塔分两次按E，第二次后冷却。\n死亡锤风：2秒旋转伤敌清弹，地形遮挡；暂停普攻蓄力，过门/倒地结束。":"炮塔：部署在脚下、自主索敌，继承适用符弹强化；切房或临时倒地后继续剩余寿命。\n护场：跟随授予临时团队盾，每人每次至多一份，反复进出不补盾。")+"\n\n临时护盾会到期。两武器恢复分别计算，切换不刷新资源。真实损伤中断机关/救援，受伤不新增硬直。";
                }
                if(p5Page==2&&!string.IsNullOrEmpty(p5Controls.RebindStatus))p5Bindings.text=p5Controls.RebindStatus;
                foreach(var button in p5MenuRoot.GetComponentsInChildren<Button>(true))button.interactable=!p5Controls.Rebinding;
            }
            if(p3Contract!=null){p3Contract.enabled=false;p5Contract.text=p3Contract.text;}
        }
    }
}
