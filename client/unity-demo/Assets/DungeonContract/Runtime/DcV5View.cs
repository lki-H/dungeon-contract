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
        public bool V5MenuVisible;
        private bool v5HudReady;
        private bool v5CombatCaptured;
        private Image v5BossBackground;
        private Image v5BossFill;
        private Text v5BossLabel;
        private Image v5Menu;
        private Text v5WeaponLabel;
        private Image v5WeaponHead;
        private Image v5WeaponHandle;
        private readonly Image[] v5Cooldowns=new Image[3];
        private readonly Text[] v5CooldownLabels=new Text[3];
        private readonly Text[] v5GrowthLabels=new Text[3];
        private readonly HashSet<string> v5Bars=new HashSet<string>();

        private Image V5Image(string name,float horizontal,float vertical,float width,float height,Color color)
        {
            var image=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();image.transform.SetParent(UiRoot,false);image.material=uiMaterial;image.raycastTarget=false;image.color=color;Rect(image.rectTransform,horizontal,vertical,width,height);return image;
        }

        private void ConfigureV5Hud()
        {
            v5HudReady=true;
            var panel=V5Image("V5 resources",18,12,300,206,new Color(0.06f,0.1f,0.12f,0.88f));panel.transform.SetAsFirstSibling();
            Rect(title.rectTransform,340,12,650,28);Rect(objective.rectTransform,340,42,650,48);Rect(resources.rectTransform,32,24,275,56);resources.fontSize=18;
            Rect(notice.rectTransform,340,662,660,40);notice.fontSize=18;
            controlHint.enabled=false;if(p4Status!=null)p4Status.enabled=false;
            for(var index=0;index<3;index++)
            {
                V5Image("Cooldown base "+index,32+index*88,92,42,42,new Color(0.15f,0.3f,0.31f));
                var image=V5Image("Cooldown "+index,32+index*88,92,42,42,new Color(0.8f,0.67f,0.3f,0.8f));image.sprite=disk;image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Radial360;image.fillOrigin=2;v5Cooldowns[index]=image;
                v5CooldownLabels[index]=Label("Cooldown text "+index,30+index*88,99,82,58,15);
                V5Image("Growth base "+index,32+index*62,169,48,32,new Color(0.12f,0.27f,0.27f));
                v5GrowthLabels[index]=Label("Growth icon "+index,36+index*62,173,46,24,15);
            }
            v5BossBackground=V5Image("Boss health background",390,119,510,16,new Color(0.12f,0.08f,0.13f,0.95f));v5BossFill=V5Image("Boss health",390,119,510,16,new Color(0.7f,0.22f,0.35f));v5BossLabel=Label("Boss name",390,92,510,24,18);
            V5Image("Current weapon panel",1038,616,224,88,new Color(0.06f,0.1f,0.12f,0.88f));
            v5WeaponHead=V5Image("Weapon image head",1054,635,36,12,Color.cyan);v5WeaponHandle=V5Image("Weapon image grip",1068,641,6,36,new Color(0.8f,0.68f,0.4f));v5WeaponLabel=Label("Current weapon",1100,627,158,66,17);
            v5Menu=V5Image("Esc help",300,180,690,335,new Color(0.045f,0.08f,0.095f,0.97f));v5Menu.raycastTarget=true;
            var help=Label("Key help",324,205,640,245,19);help.text="操作与说明\nWASD移动 · 鼠标手动瞄准\n左键：当前武器连续攻击 · 右键：仅符文锤蓄力\nQ/滚轮切换两武器 · Space闪避 · E/F两主动\nR装填 · J机关/救援/补给 · Tab成长 · 1/2/3选择\n原武器恢复保留，另一把就绪可攻击\n单人普通局暂停；多人/试炼菜单中世界继续";help.transform.SetParent(v5Menu.transform,true);
            var settings=canvas.GetComponentsInChildren<Button>(true).Where(button=>new[]{"震动","数字","退出"}.Contains(button.name)).ToArray();
            for(var index=0;index<settings.Length;index++){settings[index].transform.SetParent(v5Menu.transform,false);Rect(settings[index].GetComponent<RectTransform>(),24+index*110,285,100,30);}
            v5Menu.gameObject.SetActive(false);
        }

        private void DrawV5Hud()
        {
            if(!v5HudReady)ConfigureV5Hud();
            var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==ownID);
            var practice=(bool)snapshot["practice"];title.text=(practice?"2.2 练习（无奖励） · ":"2.2 灰盒 · ")+geometry["rooms"][room]["id"];resources.text="生命 "+own["health"]+"/"+own["max_health"]+"   护盾 "+own["shield"]+"\n弹量 "+((int)own["magazine_max"]==0?"无限":own["magazine"]+"/"+own["magazine_max"])+((long)own["reload_remaining_ms"]>0?"  装填中":"");
            if((bool?)snapshot["p3_stage"]==true)resources.text+="  增援 "+snapshot["reinforcement_remaining"];
            if(practice)objective.text="安全练习 · 左键普攻，右键仅符文锤蓄力\n目标自动重置，不推进合同";
            var times=new[]{(long)own["dodge_remaining_ms"],(long)own["ability_remaining_ms"],(long)own["secondary_remaining_ms"]};
            var ranger=(string)own["loadout_id"]=="ranger";var cooldowns=new[]{((JArray)own["upgrades"]).Any(item=>(string)item=="swift")?2400:3000,ranger?4000:12000,ranger?10000:16000};var names=new[]{"Space","E","F"};
            for(var index=0;index<3;index++){v5Cooldowns[index].fillAmount=Mathf.Clamp01(times[index]/(float)cooldowns[index]);v5CooldownLabels[index].text=names[index]+"\n"+(times[index]>0?Math.Ceiling(times[index]/1000d).ToString():"就绪");}
            var growth=((JArray)own["upgrades"]).Select(item=>ModuleName((string)item)).ToArray();
            for(var index=0;index<3;index++)v5GrowthLabels[index].text=index<growth.Length?growth[index].Substring(0,Math.Min(2,growth[index].Length)):"—";
            var melee=(bool)own["melee_equipped"];var weapon=ranger?(melee?"遗迹短刃":"连发弩"):(melee?"符文锤":"符文弹");
            var ready=melee?(long)own["melee_ready_ms"]:(long)own["ranged_ready_ms"];
            v5WeaponLabel.text=weapon+"\n"+(ready>(long)snapshot["elapsed_ms"]?"恢复中":"已就绪")+" · Q切换";
            v5WeaponHead.sprite=!melee&&!ranger?disk:square;v5WeaponHead.color=ranger?new Color(0.4f,0.9f,0.65f):new Color(0.5f,0.65f,1);v5WeaponHead.rectTransform.sizeDelta=melee&&!ranger?new Vector2(38,20):new Vector2(34,9);
            v5WeaponHandle.gameObject.SetActive(melee||ranger);
            v5Menu.gameObject.SetActive(V5MenuVisible&&!DcP5Settings.Active);
            if(DcP5Settings.Active)UpdateP5Menu();
            foreach(var button in p4Buttons)if(button!=null)button.gameObject.SetActive(p4Open&&!V5MenuVisible);
            if(V5MenuVisible)v5Menu.transform.SetAsLastSibling();
            var boss=((JArray)snapshot["enemies"]).FirstOrDefault(item=>(int)item["room_id"]==room&&(string)item["kind"]=="boss");var showBoss=boss!=null;
            v5BossBackground.enabled=v5BossFill.enabled=v5BossLabel.enabled=showBoss;
            if(showBoss){var ratio=(int)boss["health"]/(float)(int)boss["maximum"];Rect(v5BossFill.rectTransform,390,119,510*Mathf.Clamp01(ratio),16);v5BossLabel.text="遗迹镇守者 · 阶段 "+boss["phase"]+" · "+Math.Ceiling(ratio*100)+"%";}
            var evidence=DcBootstrap.Argument("-dc-p4-evidence");
            if(showBoss&&!v5CombatCaptured&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null&&(int)boss["health"]<(int)boss["maximum"]*0.8f&&!string.IsNullOrEmpty(evidence))
            {
                var path=System.IO.Path.GetFullPath(evidence+"-boss.png");
                if(path.StartsWith(System.IO.Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){v5CombatCaptured=true;ScreenCapture.CaptureScreenshot(path);}
            }
            var present=new HashSet<string>();
            foreach(var enemy in ((JArray)snapshot["enemies"]).Where(item=>(int)item["room_id"]==room&&(string)item["kind"]!="boss"&&Visible(Point(item))))
            {
                var id="weapon:v5.hp:"+enemy["id"];var point=sprites.TryGetValue("enemy:"+enemy["id"],out var actor)?(Vector2)actor.transform.position-RoomOffset:Point(enemy);point+=Vector2.up*(DcP5Settings.Active?((string)enemy["kind"]=="elite"?1.85f:1.6f):((string)enemy["kind"]=="elite"?0.85f:0.6f));
                present.Add(id);present.Add(id+":fill");var fraction=Mathf.Clamp01((int)enemy["health"]/(float)(int)enemy["maximum"]);Draw(id,point,new Color(0.1f,0.08f,0.1f),new Vector2(0.8f,0.09f),14);Draw(id+":fill",point+Vector2.left*(0.4f*(1-fraction)),new Color(0.8f,0.25f,0.3f),new Vector2(Math.Max(0.01f,0.8f*fraction),0.055f),15);
            }
            foreach(var id in v5Bars.Where(id=>!present.Contains(id)).ToArray())if(sprites.TryGetValue(id,out var renderer)){renderer.gameObject.SetActive(false);spritePool.Enqueue(renderer);sprites.Remove(id);}
            v5Bars.Clear();v5Bars.UnionWith(present);
            var charging=(bool)own["charging"];var chargeMS=(long)own["charge_ms"];
            var chargeOrigin=Position+Vector2.up*(DcP5Settings.Active?1.65f:0.9f);
            var chargeBackground=Draw("weapon:v5.charge.base",chargeOrigin,new Color(0.08f,0.1f,0.1f),new Vector2(0.82f,0.1f),14);chargeBackground.enabled=charging;
            var chargeFraction=Mathf.Clamp01(chargeMS/900f);var charge=Draw("weapon:v5.charge",chargeOrigin+Vector2.left*(0.4f*(1-chargeFraction)),chargeMS>=900?Color.white:chargeMS>=250?new Color(0.4f,1,0.7f):new Color(0.9f,0.72f,0.3f),new Vector2(Math.Max(0.01f,0.8f*chargeFraction),0.06f),15);charge.enabled=charging;
            var chargeMinimum=Draw("weapon:v5.charge.minimum",chargeOrigin+Vector2.right*(-0.4f+0.8f*250/900),Color.cyan,new Vector2(0.025f,0.13f),16);chargeMinimum.enabled=charging;
            var action=own["melee_action"] as JObject;var active=action!=null&&(string)action["phase"]=="active";
            var heavy=action!=null&&(bool)action["heavy"];var start=(float?)action?["sweep_start"]??-90;var end=(float?)action?["sweep_end"]??90;
            var wind=Draw("weapon:v5.wind",heavy?new Vector2((float)action["impact_x"],(float)action["impact_y"]):Position,heavy?new Color(1,0.3f,0.25f,0.18f):new Color(0.6f,0.9f,0.8f,0.2f),Vector2.one*Math.Max(0.1f,((float?)action?["range"]??1.2f)*2),5);wind.enabled=active;wind.sprite=heavy?disk:Sector(Mathf.RoundToInt(end-start+12));
            if(active)wind.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2((float)action["aim_y"],(float)action["aim_x"])*Mathf.Rad2Deg+(heavy?0:(start+end)/2));
            if(DcP5Settings.Active)DrawP5Hud(own);
        }
    }
}
