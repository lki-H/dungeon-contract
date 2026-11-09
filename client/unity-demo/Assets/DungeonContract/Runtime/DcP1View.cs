using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed partial class DcP1View : MonoBehaviour
    {
        private readonly Dictionary<string,SpriteRenderer> sprites=new Dictionary<string,SpriteRenderer>();
        private readonly HashSet<string> effects=new HashSet<string>();
        private readonly Queue<string> effectOrder=new Queue<string>();
        private readonly Dictionary<string,float> flashes=new Dictionary<string,float>();
        private readonly List<GameObject> terrain=new List<GameObject>();
        private readonly List<GameObject> numbers=new List<GameObject>();
        private readonly Queue<SpriteRenderer> spritePool=new Queue<SpriteRenderer>();
        private Camera cameraView;
        private Sprite square;
        private Sprite disk;
        private GameObject root;
        private Canvas canvas;
        private RectTransform p5UiRoot;
        private Transform UiRoot=>p5UiRoot!=null?p5UiRoot:canvas.transform;
        private Font font;
        private Material uiMaterial;
        private Text title;
        private Text resources;
        private Text objective;
        private Text notice;
        private Text result;
        private string p3ResultText;
        private Text p3Contract;
        private Image p3ResultPanel;
        private Text crosshair;
        private JObject geometry;
        private JObject snapshot;
        private string ownID;
        private int room=-1;
        private bool shake=true;
        private bool showNumbers=true;
        private float shakeUntil;
        private float noticeUntil;
        private float transition;
        private Vector3 from;
        private Vector3 to;
        private AudioSource source;
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private readonly Dictionary<int,Sprite> sectors=new Dictionary<int,Sprite>();
        private float weaponHold;
        private float localStart;
        public Vector2 Position {get;private set;}
        public Action Exit;
        private bool foundation;
        private bool weapons;
        private bool roomCameraStage;
        public bool RoomCameraStage{get=>roomCameraStage;set{roomCameraStage=value;if(value)ConfigureP21View();}}
        public bool Weapons{get=>weapons;set{weapons=value;RefreshControls();}}
        private readonly Queue<GameObject> trailPool=new Queue<GameObject>();
        private readonly List<(GameObject item,float until)> trails=new List<(GameObject,float)>();
        private float nextTrail;
        private Image reloadBar;
        private Text controlHint;
        public bool Foundation{get=>foundation;set{foundation=value;RefreshControls();}}
        private void RefreshControls()
        {
            if(controlHint==null)return;
            controlHint.text=Weapons?"WASD移动  左键当前武器  Q/滚轮切换  Space闪避  E/F主动  R装填  J机关  Esc真暂停/恢复":Foundation?"S1阶段：WASD  左键远程  右键近战  Space闪避  Q/F主动  R装填  E机关  Esc真暂停/恢复":"WASD移动  左键弩/符弹  右键轻击/蓄力  Space闪避  Q/F主动  R装填  E机关  Esc菜单（世界继续）";
        }
        private bool visualPaused;
        private float visualPauseAt;
        private float visualPauseTotal;
        private float ViewTime=>Foundation?(visualPaused?visualPauseAt:Time.unscaledTime)-visualPauseTotal:Time.unscaledTime;

        public void Configure(JObject world,string id)
        {
            geometry=DcP1Definition.NormalizeGeometry(world);ownID=id;font=Resources.Load<Font>("DungeonContract/Fonts/NotoSansSC")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Application.targetFrameRate=60;
            uiMaterial=new Material(Shader.Find("UI/Default"));
            var texture=new Texture2D(1,1);texture.SetPixel(0,0,Color.white);texture.Apply();square=Sprite.Create(texture,new Rect(0,0,1,1),Vector2.one*0.5f,1);
            var circle=new Texture2D(64,64);for(var horizontal=0;horizontal<64;horizontal++)for(var vertical=0;vertical<64;vertical++)circle.SetPixel(horizontal,vertical,Vector2.Distance(new Vector2(horizontal,vertical),new Vector2(31.5f,31.5f))<31?Color.white:Color.clear);circle.Apply();disk=Sprite.Create(circle,new Rect(0,0,64,64),Vector2.one*0.5f,64);
            root=new GameObject("P1 authoritative view");root.transform.SetParent(transform);
            cameraView=new GameObject("P1 room camera").AddComponent<Camera>();cameraView.transform.SetParent(transform);cameraView.orthographic=true;cameraView.orthographicSize=6.75f;cameraView.backgroundColor=new Color(0.045f,0.06f,0.08f);cameraView.clearFlags=CameraClearFlags.SolidColor;
            cameraView.gameObject.AddComponent<AudioListener>();source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;
            if(EventSystem.current==null)new GameObject("P1 UI events",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(transform);
            canvas=new GameObject("P1 UGUI",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();canvas.transform.SetParent(transform);canvas.renderMode=RenderMode.ScreenSpaceOverlay;var scale=canvas.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);
            if(DcP5Settings.Active){scale.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;p5UiRoot=DcP5Theme.Reference(canvas.transform);}
            title=Label("Title",18,12,1000,28,22);objective=Label("Objective",18,42,1200,26,18);resources=Label("Resources",18,650,1160,28,20);
            controlHint=Label("Controls",18,686,1160,24,16);RefreshControls();
            notice=Label("Notice",330,96,800,50,20);result=Label("Result",300,240,800,200,24);crosshair=Label("Aim",0,0,24,24,22);crosshair.text="+";
            reloadBar=new GameObject("Reload progress",typeof(RectTransform),typeof(Image)).GetComponent<Image>();reloadBar.transform.SetParent(UiRoot,false);reloadBar.material=uiMaterial;reloadBar.raycastTarget=false;reloadBar.color=new Color(1,0.75f,0.25f);Rect(reloadBar.rectTransform,18,635,0,6);
            Button("震动",1050,14,70,28,()=>shake=!shake);Button("数字",1130,14,70,28,()=>showNumbers=!showNumbers);Button("退出",1210,14,55,28,()=>Exit?.Invoke());
        }
        private Text Label(string name,float horizontal,float vertical,float width,float height,int size)
        {
            var text=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();text.transform.SetParent(UiRoot,false);Rect(text.rectTransform,horizontal,vertical,width,height);text.font=font;text.material=uiMaterial;text.fontSize=size;text.color=new Color(0.92f,0.95f,1);text.raycastTarget=false;text.verticalOverflow=VerticalWrapMode.Overflow;return text;
        }
        private void Button(string text,float horizontal,float vertical,float width,float height,Action clicked)
        {
            var button=new GameObject(text,typeof(RectTransform),typeof(Image),typeof(Button)).GetComponent<Button>();button.transform.SetParent(UiRoot,false);Rect(button.GetComponent<RectTransform>(),horizontal,vertical,width,height);button.GetComponent<Image>().color=new Color(0.13f,0.2f,0.28f);button.onClick.AddListener(()=>clicked());
            var label=new GameObject("Caption",typeof(RectTransform),typeof(Text)).GetComponent<Text>();label.transform.SetParent(button.transform,false);label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=Vector2.zero;label.rectTransform.offsetMax=Vector2.zero;label.font=font;label.material=uiMaterial;label.fontSize=16;label.color=Color.white;label.text=text;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
        }
        private static void Rect(RectTransform rect,float horizontal,float vertical,float width,float height){rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(horizontal,-vertical);rect.sizeDelta=new Vector2(width,height);}
        public Vector2 PointerWorld(Vector2 screen)=>cameraView==null?Vector2.zero:(Vector2)cameraView.ScreenToWorldPoint(screen)-RoomOffset;
        private Vector2 ScreenToUi(Vector2 point){if(p5UiRoot==null)return new Vector2(point.x*1280/Math.Max(1,Screen.width),(Screen.height-point.y)*720/Math.Max(1,Screen.height));RectTransformUtility.ScreenPointToLocalPointInRectangle(p5UiRoot,point,null,out var local);return new Vector2(local.x+640,360-local.y);}
        private static Vector2 Point(JToken item)=>new Vector2((float)item["x"],(float)item["y"]);
        private SpriteRenderer Draw(string id,Vector2 point,Color color,Vector2 size,int order,bool round=false)
        {
            if(!sprites.TryGetValue(id,out var renderer)){renderer=spritePool.Count>0?spritePool.Dequeue():new GameObject(id).AddComponent<SpriteRenderer>();renderer.gameObject.SetActive(true);renderer.enabled=true;renderer.transform.rotation=Quaternion.identity;renderer.transform.SetParent(root.transform);sprites[id]=renderer;}
            renderer.sprite=round?disk:square;renderer.transform.position=point+RoomOffset;renderer.transform.localScale=size;renderer.sortingOrder=order;renderer.color=flashes.TryGetValue(id,out var until)&&ViewTime<until?Color.white:color;return renderer;
        }
        public void Receive(JObject value)
        {
            if(!Foundation&&snapshot!=null&&(long)value["world_frame_id"]<=(long)snapshot["world_frame_id"])return;
            snapshot=value;var own=((JArray)value["players"]).First(item=>(string)item["player_id"]==ownID);if(!Foundation)Position=Point(own);var next=(int)own["room_id"];
            var fullMetrics=GetComponent<DcS1Metrics>();if((bool?)value["result_confirmed"]==true&&value["outcome"]?.Type==JTokenType.String&&fullMetrics!=null)fullMetrics.Flush();
            var metrics=GetComponent<DcS1Metrics>();if(metrics!=null)metrics.Observe(next,((JArray)value["enemies"]).Count(item=>(int)item["room_id"]==next),((JArray)value["projectiles"]).Count(item=>(int)item["room_id"]==next));
            if(room!=next){if(V7View)V7ClearPreviews();var initial=room<0;room=next;from=cameraView.transform.position;to=roomCameraStage?CameraTarget(Point(own)):new Vector3(9,5,-10);transition=ViewTime;BuildRoom();if(initial&&roomCameraStage)cameraView.transform.position=to;}
            var present=new HashSet<string>();
            if(roomCameraStage)DrawP21State(value,present);
            foreach(var item in ((JArray)value["players"]).Where(item=>(int)item["room_id"]==room)){var id="player:"+item["player_id"];present.Add(id);Draw(id,Point(item),(int)item["health"]<=0?Color.gray:(string)item["loadout_id"]=="ranger"?new Color(0.25f,0.85f,0.6f):new Color(0.35f,0.65f,1),Vector2.one*0.6f,8,true);}
            foreach(var item in ((JArray)value["enemies"]).Where(item=>(int)item["room_id"]==room)){var id="enemy:"+item["id"];present.Add(id);var kind=(string)item["kind"];var radius=kind=="boss"?1.8f:kind=="elite"?1.2f:0.84f;Draw(id,Point(item),kind=="archer"?new Color(0.95f,0.3f,0.65f):kind=="charger"?new Color(1,0.4f,0.2f):kind=="boss"?new Color(0.65f,0.25f,0.9f):new Color(0.85f,0.65f,0.3f),Vector2.one*radius,7,true);if((string)item["warning"]!="")Warning(item,present);}
            foreach(var group in new[]{"projectiles","turrets","covers","fields","traps"})foreach(var item in ((JArray)value[group]).Where(item=>(int)item["room_id"]==room))
            {
                if(group=="covers"&&(int)item["health"]<=0)continue;
                var id=group+":"+item["id"];present.Add(id);var color=group=="projectiles"?((bool)item["enemy"]?Color.red:Color.cyan):group=="turrets"?Color.blue:group=="covers"?new Color(0.55f,0.35f,0.15f):group=="fields"?((string)item["kind"]=="wind_bind"?new Color(0.3f,0.9f,0.6f,0.25f):new Color(0.25f,0.55f,1,0.25f)):(string)item["phase"]=="active"?Color.red:(string)item["phase"]=="warning"?Color.yellow:new Color(0.25f,0.25f,0.28f);
                var diameter=group=="projectiles"?(weapons?((float?)item["radius"]??0.1f)*2:0.18f):group=="covers"?0.9f:group=="fields"?(float)item["radius"]*2:group=="traps"?1.4f:0.6f;
                var renderer=Draw(id,Point(item),color,Vector2.one*diameter,group=="projectiles"?10:group=="fields"||group=="traps"?3:6,true);if(DcP5Settings.Active&&group=="projectiles")StyleP5Projectile(renderer,item);
            }
            foreach(var door in geometry["doors"]){if((int)door["from"]==room)present.Add("door:"+door["side"]);else if((int)door["to"]==room)present.Add("door:"+RoomGeometry.Opposite((string)door["side"]));}
            foreach(var id in sprites.Keys.Where(id=>!present.Contains(id)&&!id.StartsWith("weapon:")).ToArray()){var renderer=sprites[id];renderer.gameObject.SetActive(false);spritePool.Enqueue(renderer);sprites.Remove(id);}
            foreach(var effect in (JArray)value["effects"])if(((int)effect["room"]==room||roomCameraStage&&(string)effect["actor"]==ownID)&&effects.Add((string)effect["id"])){if(!visualPaused)Play(effect);}
            var state=value["rooms"][room];title.text=((bool)value["practice"]?"练习场（无奖励）":roomCameraStage?"P2.1 灰盒（无奖励）":Weapons?"2.1 武器/技能阶段":Foundation?"2.1 同步/暂停阶段（战斗仍为P1规则）":"P1 单人阶段演示")+" · "+geometry["rooms"][room]["id"];
            objective.text=(bool)value["practice"]?"练习移动、手动瞄准、短按/蓄力、闪避、Q/F技能与装填；练习目标会重置":"清敌 "+state["enemies_remaining"]+" · 波次 "+state["wave"]+" · "+((string)state["status"]=="cleared"?"房间完成，向右出口前进":(string)state["status"]=="warning"?"开战预警 "+state["warning_remaining_ms"]+"ms":room==2||room==3?((bool)state["mechanism_complete"]?"机关已完成，继续清敌":"清敌＋按住E完成机关"):"清理本房有限敌人");
            resources.text="生命 "+own["health"]+"  护盾 "+own["shield"]+"  弹匣 "+((int)own["magazine_max"]==0?"无限":own["magazine"]+"/"+own["magazine_max"])+"  装填 "+own["reload_remaining_ms"]+"ms  闪避 "+own["dodge_remaining_ms"]+"ms  Q/F "+own["ability_remaining_ms"]+"/"+own["secondary_remaining_ms"]+"ms  蓄力 "+own["charge_ms"]+"ms";
            if(Weapons)
            {
                var ranger=(string)own["loadout_id"]=="ranger";var melee=(bool)own["melee_equipped"];var weapon=ranger?(melee?"遗迹短刃":"连发弩"):(melee?"符文重锤":"符文弹");
                resources.text="当前 "+weapon+"  生命 "+own["health"]+"  护盾 "+own["shield"]+"  弹匣 "+((int)own["magazine_max"]==0?"无弹匣":own["magazine"]+"/"+own["magazine_max"])+"  装填 "+Seconds(own["reload_remaining_ms"])+"  闪避 "+Seconds(own["dodge_remaining_ms"])+"  E/F "+Seconds(own["ability_remaining_ms"])+"/"+Seconds(own["secondary_remaining_ms"])+(melee?"  蓄力 "+Seconds(own["charge_ms"]):"");
                objective.text=objective.text.Replace("Q/F","E/F").Replace("按住E","按住J");var reload=(long)own["reload_remaining_ms"];var reloadDuration=(int)own["magazine_max"]>8?700:900;Rect(reloadBar.rectTransform,18,635,reload>0?200*Mathf.Clamp01(1-reload/(float)reloadDuration):0,6);
            }
            if(!string.IsNullOrEmpty((string)value["outcome"]))result.text=(bool?)value["result_confirmed"]!=true&&!(bool)value["practice"]?"正在确认结果……":(string)value["outcome"]=="success"?"关卡演示完成\n可关闭窗口后重新开始":"本次尝试结束\n可关闭窗口后重新开始";
            if((bool?)value["p3_stage"]==true){title.text="P3 合作与合同 · "+geometry["rooms"][room]["id"];resources.text+="  共享增援剩余 "+value["reinforcement_remaining"];var life=(string)own["life_state"];if(life=="downed")objective.text="已倒地，队友可在1.5格内按住J救援 · 剩余 "+Seconds(own["downed_remaining_ms"]);else if(life=="dead")objective.text="已死亡，等待服务分配共享增援；预算不足时可观战";else if(life=="awaiting_respawn")objective.text="增援已预留，等待合法落点和可靠落地确认";else if(own["rescue_target"]?.Type==JTokenType.String)objective.text="正在救援 · 剩余 "+Seconds(own["rescue_remaining_ms"]);if(p3ResultText!=null)result.text=p3ResultText;}
            ReceiveP4(own);
            foreach(var door in (JArray)geometry["doors"]){var forward=(int)door["from"]==room;var backward=(int)door["to"]==room;if(!forward&&!backward)continue;var side=forward?(string)door["side"]:RoomGeometry.Opposite((string)door["side"]);var point=RoomGeometry.DoorPoint((JObject)geometry["rooms"][room],side);var id="door:"+side;present.Add(id);Draw(id,point,(string)state["status"]=="cleared"?Color.green:Color.red,side=="east"||side=="west"?new Vector2(0.15f,1.2f):new Vector2(1.2f,0.15f),4);}
        }
        private static string Seconds(JToken value)=>((long)value/1000f).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        private void BuildRoom()
        {
            foreach(var item in terrain)Destroy(item);terrain.Clear();
            var definition=geometry["rooms"][room];
            var width=(float)definition["width"];var height=(float)definition["height"];
            foreach(var rect in new[]{new[]{0f,0f,width,1f},new[]{0f,height-1f,width,1f},new[]{0f,0f,1f,height},new[]{width-1f,0f,1f,height}}.Concat(((JArray)definition["obstacles"]).Select(item=>item.Select(value=>(float)value).ToArray())))
            {var sprite=new GameObject("P1 wall").AddComponent<SpriteRenderer>();sprite.transform.SetParent(root.transform);sprite.sprite=square;sprite.color=new Color(0.2f,0.24f,0.3f);sprite.transform.position=new Vector2(rect[0]+rect[2]/2,rect[1]+rect[3]/2)+RoomOffset;sprite.transform.localScale=new Vector2(rect[2],rect[3]);terrain.Add(sprite.gameObject);}
            if(definition["objective"] is JArray point){var sprite=new GameObject("机关：按住J").AddComponent<SpriteRenderer>();sprite.transform.SetParent(root.transform);sprite.sprite=disk;sprite.color=new Color(0.2f,0.7f,0.8f);sprite.transform.position=RoomGeometry.Point(point)+RoomOffset;sprite.transform.localScale=Vector2.one*0.8f;terrain.Add(sprite.gameObject);}
            if(DcP5Settings.Active)BuildP5Terrain();
        }
        private void Warning(JToken enemy,HashSet<string> present)
        {
            var kind=(string)enemy["warning"];var origin=new Vector2((float)enemy["warning_x"],(float)enemy["warning_y"]);var direction=new Vector2((float)enemy["aim_x"],(float)enemy["aim_y"]);var id="warning:"+enemy["id"];present.Add(id);
            if(roomCameraStage)
            {
                DrawExclamation(id,Point(enemy)+(DcP5Settings.Active?new Vector2(0.7f,1.35f):new Vector2(0.5f,0.5f)),present);return;
            }
            if(kind=="stomp")Draw(id,origin,new Color(1,0.2f,0.2f,0.22f),Vector2.one*6,2,true);
            else if(kind=="charge"||kind=="shot"||kind=="fan")
            {
                var length=kind=="charge"?3.6f:8;var angles=kind=="fan"?new[]{-25f,0f,25f}:new[]{0f};
                foreach(var angle in angles){var ray=Quaternion.Euler(0,0,angle)*(Vector3)direction;var rayID=id+":"+angle;present.Add(rayID);var renderer=Draw(rayID,origin+(Vector2)ray*length/2,new Color(1,0.55f,0.1f,0.3f),new Vector2(length,kind=="charge"?1.44f:0.2f),2);renderer.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(ray.y,ray.x)*Mathf.Rad2Deg);}
            }
            else {var diameter=(string)enemy["kind"]=="elite"?3:2.4f;var renderer=Draw(id,origin,new Color(1,0.6f,0.1f,0.3f),Vector2.one*diameter,2);renderer.sprite=Sector(120);renderer.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);}
        }
        private Sprite Sector(int arc)
        {
            if(sectors.TryGetValue(arc,out var result))return result;var texture=new Texture2D(128,128);var pixels=new Color[128*128];for(var horizontal=0;horizontal<128;horizontal++)for(var vertical=0;vertical<128;vertical++){var offset=new Vector2(horizontal-63.5f,vertical-63.5f);pixels[vertical*128+horizontal]=offset.magnitude<=63&&Vector2.Dot(offset.normalized,Vector2.right)>=Mathf.Cos(arc*Mathf.Deg2Rad/2)?Color.white:Color.clear;}texture.SetPixels(pixels);texture.Apply();result=Sprite.Create(texture,new Rect(0,0,128,128),Vector2.one*0.5f,128);sectors[arc]=result;return result;
        }
        private void Play(JToken effect)
        {
            effectOrder.Enqueue((string)effect["id"]);while(effectOrder.Count>512)effects.Remove(effectOrder.Dequeue());
            var kind=(string)effect["kind"];if((kind=="hit"||kind=="environment_hit")&&effect["target"].Type!=JTokenType.Null)flashes["enemy:"+effect["target"]]=ViewTime+0.07f;
            if((string)effect["actor"]==ownID&&(kind=="hit"||kind=="shot"||kind=="light"||kind=="heavy"))weaponHold=ViewTime+0.035f;
            if(kind=="hurt"&&(string)effect["actor"]==ownID)shakeUntil=ViewTime+0.12f;
            AudioClip clip=null;if(!DcP5Settings.Active&&!clips.TryGetValue(kind,out clip)){clip=AudioClip.Create("P1 "+kind,2400,1,24000,false);var samples=new float[2400];var frequency=kind=="hurt"?150:kind=="shot"?650:400;for(var index=0;index<samples.Length;index++)samples[index]=Mathf.Sin(index*frequency*Mathf.PI*2/24000)*0.08f*(1-index/(float)samples.Length);clip.SetData(samples,0);clips[kind]=clip;}
            if(DcP5Settings.Active)P5Sound(kind,kind=="hurt"||kind=="downed"||kind=="death"||kind=="rescue",(string)effect["actor"]);else if(!source.isPlaying)source.PlayOneShot(clip);
            if(DcP5Settings.Active)P5CombatFeedback(effect);
            else if(showNumbers&&(kind=="hit"||kind=="hurt")&&(int)effect["amount"]>0&&numbers.Count<16&&(!roomCameraStage||(int)effect["room"]==room&&Visible(Point(effect)))){var point=cameraView.WorldToScreenPoint(Point(effect)+RoomOffset+new Vector2(0,0.5f));var label=Label("Damage",point.x*1280/Screen.width,(Screen.height-point.y)*720/Screen.height,60,30,18);label.text=effect["amount"].ToString();numbers.Add(label.gameObject);Destroy(label.gameObject,0.4f);numbers.RemoveAll(item=>item==null);}else if(roomCameraStage&&(string)effect["actor"]==ownID&&(kind=="hit"||kind=="kill")){crosshair.color=Color.yellow;p21HitUntil=ViewTime+0.12f;}
        }
        public void Feedback(string reason){if(reason=="accepted"||reason=="buffered")return;notice.text=reason=="skill_active"?"旋转技能中无法普攻或蓄力，结束后请重新按下":reason=="budget"?"输入队列暂满，本次未消耗资源":reason=="capacity"?"弹体容量暂满，本次未消耗弹量或冷却":reason=="charge_unsupported"?"当前武器不支持蓄力":reason=="stale_episode"?"输入已失效，请松开后重新按下":reason=="technical_wait"?"正在等待战斗同步，请稍候":reason=="cooldown"?"动作冷却中":reason=="blocked"?"落点被墙、掩体或敌人阻挡":reason=="out_of_range"?"落点超过3格":reason=="not_reloadable"?"当前武器无需装填":reason=="stale_weapon_revision"?"武器状态已更新，请松开左键重新攻击":reason=="life_state"?"当前生命状态不能行动":reason=="paused"?"暂停期间不能出招":reason=="canceled"||reason=="expired"?"缓冲动作已取消，请重新按下":"当前状态无法执行该动作";noticeUntil=ViewTime+2;}
        public void Status(string value){result.text=value;}
        public void ClearStatus(){result.text="";}
        public void P3Result(JToken state,JToken cards,string playerID)
        {
            if(DcP5Settings.Active){P5Result(state,cards,playerID);return;}
            var own=state["participants"].First(item=>(string)item["player_id"]==playerID);var grants=state["reward_grants"] as JArray??new JArray();
            if(p3ResultPanel==null){p3ResultPanel=new GameObject("P3 result backdrop",typeof(RectTransform),typeof(Image)).GetComponent<Image>();p3ResultPanel.transform.SetParent(canvas.transform,false);p3ResultPanel.color=new Color(0.025f,0.04f,0.065f,0.95f);p3ResultPanel.raycastTarget=false;Rect(p3ResultPanel.rectTransform,140,185,1000,350);result.transform.SetAsLastSibling();Rect(result.rectTransform,160,200,960,310);result.fontSize=20;}
            var elapsed=(long?)state["elapsed_ms"]??0;
            p3ResultText=((string)state["end_reason"]=="success"?"团队成功":"本次尝试结束")+"  "+(elapsed/60000)+":"+((elapsed/1000)%60).ToString("D2")+"\n本人合同 "+P3TaskName((string)own["task_key"])+" · "+((bool?)own["task_completed"]==true?"已完成":"未完成")+"\n本人奖励 "+grants.Sum(item=>(long)item["amount"])+"\n";
            if(cards is JArray values)foreach(var card in values){var participant=state["participants"].First(item=>(string)item["player_id"]==(string)card["player_id"]);p3ResultText+="槽位 "+card["slot"]+"  "+P3TaskName((string)(participant["task_category"]??participant["task_key"]))+"  伤害 "+card["effective_damage"]+"  击杀 "+(card["kills"]??card["eligible_kills"])+"  助攻 "+card["assists"]+"  救援 "+card["rescues"]+((bool?)card["totals_complete"]==false?"（仅确认前缀）":"")+"\n";}
            result.text=p3ResultText;
            objective.text="战斗已结束，个人结算已确认";notice.text="";
            var capture=DcBootstrap.Argument("-dc-p3-result-capture");if(!string.IsNullOrEmpty(capture))StartCoroutine(CaptureP3Result(capture));
        }
        public void P3Progress(JToken state,JObject configuration,string playerID)
        {
            if(!(state["participants"] is JArray participants))return;
            var own=participants.First(item=>(string)item["player_id"]==playerID);var key=(string)own["task_key"];
            if(p3Contract==null)p3Contract=Label("P3Contract",18,556,800,70,16);
            var task=((JArray)configuration["tasks"]).FirstOrDefault(item=>(string)item["id"]==key);
            if(task==null){p3Contract.text="本局未选择个人合同";return;}
            p3Contract.text="本人合同："+(task["display_name"]??task["id"])+"\n";
            foreach(var objective in task["objectives"])p3Contract.text+=P3TaskName((string)objective["kind"])+" "+(own["task_progress"]?[(string)objective["id"]]??new JValue(0))+"/"+objective["required"]+"  ";
            if((bool?)task["no_knockdowns"]==true)p3Contract.text+="\n未倒地："+((int?)own["knockdowns"]==0?"保持":"本次已失效");
        }
        private static string P3TaskName(string key)=>key=="dc_hunter"?"协作猎手":key=="dc_precision"?"有效出手":key=="dc_scout"?"遗迹勘察":key=="dc_technician"?"机关维护":key=="dc_inspector"?"枢纽巡检":key=="dc_survivor"?"稳健作战":key=="combat"?"战斗合同":key=="exploration"?"探索合同":key=="mechanism"?"机关合同":key=="survival"?"生存合同":key=="damage"?"有效伤害动作":key=="kill"?"合法击杀":key=="reach"?"到达目标区":key=="interact"?"机关完成":"未选择";
        private System.Collections.IEnumerator CaptureP3Result(string output){var path=System.IO.Path.GetFullPath(output);if(!path.StartsWith(System.IO.Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))yield break;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(path);}
        public void InputStart(string action){if(action=="ranged_press"||action=="melee_begin"||action=="attack_press")localStart=ViewTime+0.06f;}
        public void VisualPosition(Vector2 position){Position=position;if(sprites.TryGetValue("player:"+ownID,out var renderer))renderer.transform.position=position+RoomOffset;}
        public void VisualEntity(string key,Vector2 position){var id=key.StartsWith("players:")?key.Replace("players:","player:"):key.StartsWith("enemies:")?key.Replace("enemies:","enemy:"):key;if(sprites.TryGetValue(id,out var renderer))renderer.transform.position=position+RoomOffset;}
        public void PauseVisuals(bool pause)
        {
            if(pause==visualPaused)return;if(pause){if(V7View)V7ClearPreviews();visualPauseAt=Time.unscaledTime;source.Pause();p5Music?.Pause();}else{visualPauseTotal+=Time.unscaledTime-visualPauseAt;source.UnPause();p5Music?.UnPause();}PauseP5Audio(pause);visualPaused=pause;Time.timeScale=pause?0:1;
        }
        private void LateUpdate()
        {
            if((bool?)snapshot?["v5_stage"]==true)DrawV5Hud();
            if(snapshot==null||visualPaused)return;numbers.RemoveAll(item=>item==null);var pointer=UnityEngine.InputSystem.Mouse.current?.position.ReadValue()??Vector2.zero;var cursorPoint=ScreenToUi(pointer);Rect(crosshair.rectTransform,cursorPoint.x-(DcP5Settings.Active?12:10),cursorPoint.y-12,24,24);
            if(roomCameraStage){UpdateP21Camera();}else cameraView.transform.position=Vector3.Lerp(from,to,Mathf.Clamp01((ViewTime-transition)/0.2f));cameraView.transform.position+=(shake&&ViewTime<shakeUntil?new Vector3(Mathf.Sin(ViewTime*100)*0.025f*(DcP5Settings.Active?DcP5Settings.Current.Shake:1),0,0):Vector3.zero);
            var direction=(PointerWorld(pointer)-Position).normalized;var weapon=Draw("weapon:own",Position+direction*0.4f,Color.white,new Vector2(ViewTime<localStart?0.7f:0.55f,0.12f),9);if(ViewTime>=weaponHold)weapon.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            var point=PointerWorld(pointer);var layout=(JObject)geometry["rooms"][room];var valid=Vector2.Distance(point,Position)<=3&&!RoomGeometry.Blocked(layout,point,0.3f)&&!((JArray)snapshot["covers"]).Any(item=>(int)item["room_id"]==room&&(int)item["health"]>0&&Vector2.Distance(point,Point(item))<0.75f);
            var preview=Draw("weapon:preview",point,valid?new Color(0.2f,1,0.4f,0.3f):new Color(1,0.2f,0.2f,0.3f),Vector2.one*0.6f,3,true);preview.enabled=!Weapons&&((JArray)snapshot["players"]).Any(item=>(string)item["player_id"]==ownID&&(int)item["health"]>0);
            if(Weapons){var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==ownID);if((long)own["dodge_duration_remaining_ms"]>0&&ViewTime>=nextTrail){nextTrail=ViewTime+0.035f;var ghost=trailPool.Count>0?trailPool.Dequeue():new GameObject("Dodge afterimage");ghost.SetActive(true);var renderer=ghost.GetComponent<SpriteRenderer>()??ghost.AddComponent<SpriteRenderer>();renderer.sprite=disk;renderer.color=new Color(0.5f,1,1,0.3f);renderer.sortingOrder=5;ghost.transform.SetParent(root.transform);ghost.transform.position=Position+RoomOffset;ghost.transform.localScale=Vector2.one*0.6f;trails.Add((ghost,ViewTime+0.12f));}foreach(var trail in trails.Where(item=>item.until<=ViewTime).ToArray()){trail.item.SetActive(false);trailPool.Enqueue(trail.item);trails.Remove(trail);}}
            if(Weapons){var own=((JArray)snapshot["players"]).First(item=>(string)item["player_id"]==ownID);var remaining=(long)own["reload_remaining_ms"];var duration=(bool?)snapshot["v6_stage"]==true?Math.Max(1,(int)own["reload_duration_ms"]):(int)own["magazine_max"]>8?700:900;if((bool?)snapshot["v6_stage"]==true)remaining=Math.Max(0,(long)own["reload_until_ms"]-(long)V6Game);var reload=Draw("weapon:reload",Position+Vector2.up*0.65f,new Color(1,0.75f,0.25f),new Vector2(Math.Max(0.01f,0.65f*(1-remaining/(float)duration)),0.07f),12);reload.enabled=remaining>0;}
            if(ViewTime>=noticeUntil)notice.text="";
            if(DcP5Settings.Active){P5Actors();P5Audio();UpdateP5Feedback();}
            if((bool?)snapshot["v6_stage"]==true)DrawV6Presentation();if(V7View)DrawV7Final();
        }
        private Vector3 CameraTarget(Vector2 position)
        {
            if(!roomCameraStage||geometry==null||room<0)return new Vector3(position.x,position.y,-10);
            var definition=(JObject)geometry["rooms"][room];var point=DcV8Profile.Active?AimOrigin:(string)definition["kind"]=="corridor"?position:DcP21Camera.Center(definition,position);point+=RoomOffset;return new Vector3(point.x,point.y,-10);
        }
        private Vector2 EdgePoint(Vector2 world)
        {
            var viewport=cameraView.WorldToViewportPoint(world+RoomOffset);var x=Mathf.Clamp(viewport.x,0.07f,0.93f);var y=Mathf.Clamp(viewport.y,0.16f,0.86f);return (Vector2)cameraView.ViewportToWorldPoint(new Vector3(x,y,10))-RoomOffset;
        }
        private void OnDestroy(){if(DcP5Settings.Active)DcP5Settings.Current.Changed-=ApplyP5Settings;CleanupP5Feedback();Time.timeScale=1;if(root!=null)Destroy(root);foreach(var clip in clips.Values)Destroy(clip);if(uiMaterial!=null)Destroy(uiMaterial);foreach(var sprite in sectors.Values){Destroy(sprite.texture);Destroy(sprite);}if(square!=null){Destroy(square.texture);Destroy(square);}if(disk!=null){Destroy(disk.texture);Destroy(disk);}}
    }
}
