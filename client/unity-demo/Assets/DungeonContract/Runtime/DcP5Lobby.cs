using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed class DcP5Lobby : MonoBehaviour
    {
        internal static bool ShouldStart=>DcP5Settings.Active&&DcBootstrap.Argument("-dc-p5-authority-probe","false")!="true"&&DcBootstrap.Argument("-dc-role","client")=="client"&&string.IsNullOrEmpty(DcBootstrap.Argument("-dc-run"))&&DcBootstrap.Argument("-dc-practice","false")!="true"&&DcBootstrap.Argument("-dc-v5-controls-check","false")!="true";
        private Canvas canvas;
        private RectTransform reference;
        private Transform content;
        private Text status;
        private InputField username;
        private InputField password;
        private InputField peer;
        private PveControlClient control;
        private JObject config;
        private JObject party;
        private JObject proposal;
        private JObject regroup;
        private JObject invitation;
        private string selectedTask="";
        private string loadout="ranger";
        private string mode="normal";
        private bool busy;
        private bool playing;
        private bool queued;
        private int page;
        private int revision;
        private float nextRefresh;
        private string lastRun;
        private int boardPlayers=1;
        private DcP0V3 combat;
        private readonly List<string> previousMembers=new List<string>();
        private readonly Queue<string> departedRuns=new Queue<string>();
        private readonly List<string> taskIDs=new List<string>{"","dc_hunter","dc_precision","dc_scout","dc_technician","dc_inspector","dc_survivor"};
        private readonly List<string> taskNames=new List<string>{"不选择合同","协作猎手","精确打击（有效动作）","遗迹勘察","机关维护","枢纽巡检","稳健作战（未倒地）"};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize(){if(ShouldStart&&FindFirstObjectByType<DcP5Lobby>()==null)new GameObject("P5 expedition lobby").AddComponent<DcP5Lobby>();}
        private void Start()
        {
            foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables()){var name=(string)variable.Key;if(name.IndexOf("PASSWORD",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("SECRET",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("TOKEN",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("API_KEY",StringComparison.OrdinalIgnoreCase)>=0||name=="DC_ADMISSION_KEY")Environment.SetEnvironmentVariable(name,null,EnvironmentVariableTarget.Process);}
            Application.runInBackground=true;Application.targetFrameRate=60;
            config=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"..","Content",DcV8Profile.Active?"demo-v8-p5.json":DcV7Profile.Active?"demo-v7-p5.json":DcV6Profile.Active?"demo-v6-p5.json":"demo-v5-p5.json")));
            CreateControl();
            canvas=new GameObject("P5 Lobby UI",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();canvas.transform.SetParent(transform,false);canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var background=new GameObject("P5 full canvas background",typeof(RectTransform),typeof(Image)).GetComponent<Image>();background.transform.SetParent(canvas.transform,false);background.rectTransform.anchorMin=Vector2.zero;background.rectTransform.anchorMax=Vector2.one;background.rectTransform.offsetMin=background.rectTransform.offsetMax=Vector2.zero;background.color=DcP5Theme.Stone;background.raycastTarget=false;reference=DcP5Theme.Reference(canvas.transform);
            if(EventSystem.current==null)new GameObject("P5 lobby events",typeof(EventSystem),typeof(InputSystemUIInputModule)).transform.SetParent(transform,false);
            DcP5Theme.Panel(reference,"Background",0,0,1280,720);
            var art=new GameObject("Ruins lobby backdrop",typeof(RectTransform),typeof(Image));art.transform.SetParent(reference,false);DcP5Theme.Place((RectTransform)art.transform,0,0,1280,720);var artImage=art.GetComponent<Image>();artImage.sprite=DcP5Art.Backdrop;artImage.color=new Color(1,1,1,0.55f);artImage.raycastTarget=false;artImage.enabled=artImage.sprite!=null;
            DcP5Theme.Text(reference,"Title","DUNGEON CONTRACT",45,14,1150,78,31);DcP5Theme.Text(reference,"Subtitle","地牢契约   ·   遗迹远征",48,94,1150,44,20);
            status=DcP5Theme.Text(reference,"Status","登录后选择方案、合同和出发方式。好友使用同一受限服务器。",48,141,1180,46,17);
            var tabs=new[]{"远征准备","好友与队伍","个人合同","试炼与记录","设置"};
            for(var index=0;index<tabs.Length;index++){var selected=index;DcP5Theme.Button(reference,"Lobby tab "+index,tabs[index],48+index*237,194,225,42,()=>{if(!busy){page=selected;_=Render();}});}
            var area=new GameObject("Lobby content",typeof(RectTransform));area.transform.SetParent(reference,false);DcP5Theme.Place((RectTransform)area.transform,48,256,1180,420);content=area.transform;
            DcP5Theme.Text(reference,"Footer","完全手动瞄准 · 无限总弹药 · 两把固定武器 · 无永久战斗属性",48,676,1100,32,15);
            _=Render();
            if(DcBootstrap.Argument("-dc-p5-lobby-probe","false")=="true")_=P5LobbyProbe();
            if(!string.IsNullOrEmpty(DcBootstrap.Argument("-dc-p5-lobby-capture")))StartCoroutine(CaptureLobby());
        }
        private async Task Run(Func<Task> work)
        {
            if(busy||playing)return;busy=true;
            try{await work();}
            catch(Exception error){status.text=error is PveControlClient.CommandRejectedException rejection?"操作被拒绝（"+rejection.Code+"），请刷新状态后重试":"暂时无法完成操作。检查登录、服务器和网络后重试。";}
            finally{busy=false;if(!playing)await Render();}
        }
        private void ClearContent(){foreach(Transform child in content){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        private Button AddButton(string id,string caption,float horizontal,float vertical,float width,Func<Task> action)=>DcP5Theme.Button(content,id,caption,horizontal,vertical,width,44,()=>_=Run(action));
        private InputField Field(string id,string hint,float horizontal,float vertical,float width,bool secret=false)
        {
            var panel=DcP5Theme.Panel(content,id,horizontal,vertical,width,48,true);panel.color=DcP5Theme.Slate;var input=panel.gameObject.AddComponent<InputField>();var text=DcP5Theme.Text(panel.transform,"Value","",12,5,width-24,38,18);input.textComponent=text;input.characterLimit=secret?128:64;
            if(secret)input.contentType=InputField.ContentType.Password;
            var placeholder=DcP5Theme.Text(panel.transform,"Placeholder",hint,12,5,width-24,38,17);placeholder.color=DcP5Theme.Muted;input.placeholder=placeholder;return input;
        }
        private async Task Render()
        {
            if(playing||content==null)return;
            ClearContent();var current=++revision;
            if(!control.HasSession)
            {
                username=Field("Username","账号",0,10,470);password=Field("Password","密码（仅保留在当前输入框）",0,78,470,true);
                AddButton("Login","登录",0,148,226,async()=>{var user=username.text;var secret=password.text;password.text="";await control.Login(user,secret);status.text="已登录 · 本人ID "+control.PlayerId;await RefreshActivity();});
                AddButton("Register","创建试玩账号",244,148,226,Register);
                DcP5Theme.Text(content,"Login note","账号保存长期合同。密码不写入客户端设置、日志或便携包。\n\n无需安装Unity。先启动专用Host，再连接本机、局域网或Radmin地址。\n网络配置与防火墙步骤见随包真人联机教程。",530,12,620,270,20);return;
            }
            if(!control.IsConnected)
            {
                DcP5Theme.Text(content,"Disconnected","大厅连接已中断。队伍、排队与结果以服务当前状态为准。",0,0,1140,110,22);
                AddButton("Reconnect lobby","恢复大厅连接",0,142,550,async()=>{await control.Connect();await RefreshActivity();status.text="大厅已恢复";});
                AddButton("Relogin","重新登录并恢复状态",610,142,530,async()=>{await control.Disconnect();CreateControl();party=null;proposal=null;queued=false;status.text="请登录原账号以恢复服务状态";});return;
            }
            if(page==0){RenderExpedition();return;}
            if(page==1){await RenderSocial(current);return;}
            if(page==2){await RenderContracts(current);return;}
            if(page==3){await RenderRecords(current);return;}
            RenderSettings();
        }
        private JObject Plan(string fill="no_fill")
        {
            var plan=new JObject{["operation"]="dc_ruins",["difficulty"]="standard",["rule_version"]=config["control_rule_version"],["fill_policy"]=fill,["allow_partial"]=true,["mode"]=mode};
            if(mode=="trial"){plan["trial_id"]="dc_ruins_trial";plan["trial_version"]=config["trial"]["version"];}return plan;
        }
        private JObject Selection()=>new JObject{["loadout_id"]=loadout,["task_key"]=selectedTask,["task_version"]=string.IsNullOrEmpty(selectedTask)?"":"dc_tasks.v5.p4.1"};
        private void RenderExpedition()
        {
            DcP5Theme.Text(content,"Loadout",loadout=="ranger"?"游侠   /   机动与精准\n连发弩＋遗迹短刃\n齐射＋风缚":DcV8Profile.Active?"工匠   /   部署与旋击\n符文弹＋短柄符文锤\n分次炮塔＋死亡锤风":"工匠   /   部署与控场\n符文弹＋符文锤\n炮塔＋队伍护场",0,0,510,144,22);
            AddButton("Ranger","选择游侠",0,148,232,async()=>{loadout="ranger";if(party!=null)await SubmitSelection();});AddButton("Runesmith","选择工匠",250,148,232,async()=>{loadout="runesmith";if(party!=null)await SubmitSelection();});
            AddButton("Mode",mode=="normal"?"模式：普通随机":"模式：固定试炼",0,212,482,async()=>{if(party!=null&&(long)party["owner_id"]!=control.PlayerId){status.text="队伍模式由队长设置";return;}mode=mode=="normal"?"trial":"normal";if(party!=null){await control.Request("v2.party.plan_update",new{party_id=(string)party["id"],plan=Plan()});await RefreshParty();}});
            DcP5Theme.Text(content,"Mode note",mode=="normal"?"单人Esc/失焦可真正暂停。多人世界继续。":"固定种子和内容；Esc不会暂停。\n成绩只和同版本、同初始人数的本人/好友比较。",0,274,500,90,17);
            if(proposal!=null)
            {
                DcP5Theme.Text(content,"Proposal","已找到关卡 · 所有成员须在服务期限内确认。",545,0,610,100,21);
                AddButton("Confirm","确认进入",545,118,600,Confirm);return;
            }
            if(queued){DcP5Theme.Text(content,"Queued","正在等待其他玩家与服务分配。\n人数不足时按照匹配规则开始。",545,0,600,120,22);AddButton("Cancel","取消排队",545,140,600,async()=>{await control.Request("v2.match.cancel",new{});queued=false;});return;}
            if(party==null)
            {
                AddButton("Solo","单人出发",545,0,600,()=>Enqueue("no_fill"));AddButton("Public","匹配1—4人",545,64,600,()=>Enqueue("public"));AddButton("Create party","创建好友队伍",545,128,600,CreateParty);
            }
            else
            {
                var members=(JArray)party["members"];var labels=members.Select((member,index)=>"["+(index+1)+"] "+((string)(member["task_selection"] as JObject)?["loadout_id"]=="ranger"?"游侠":(string)(member["task_selection"] as JObject)?["loadout_id"]=="runesmith"?"工匠":"未选方案")+((bool)member["ready"]?" · 已准备":" · 待准备")).ToArray();
                var roster=string.Join("\n",Enumerable.Range(0,(labels.Length+1)/2).Select(index=>string.Join("    ",labels.Skip(index*2).Take(2))));DcP5Theme.Text(content,"Party","好友队伍 · "+members.Count+"/4\n"+roster,545,0,600,108,15);
                AddButton("Party selection","提交本人方案/合同",545,112,290,SubmitSelection);
                AddButton("Ready","准备",855,112,290,Ready);
                if((long)party["owner_id"]==control.PlayerId)
                {
                    AddButton("Party enqueue","整队出发 · 需全员准备",545,174,600,async()=>{await control.Request("v2.match.enqueue",new{party_id=(string)party["id"]});queued=true;});
                }
                AddButton("Leave party","离开队伍",545,236,600,async()=>{await control.Request("v2.party.leave",new{party_id=(string)party["id"]});party=null;});
            }
            AddButton("Practice","安全练习（不改合同）",545,310,290,()=>{LaunchPractice();return Task.CompletedTask;});
            AddButton("Logout","切换账号",855,310,290,async()=>{if(queued||party!=null)throw new InvalidOperationException();await control.Disconnect();CreateControl();status.text="请输入账号密码";});
        }
        private async Task Register()
        {
            var user=username.text;var secret=password.text;password.text="";
            if(user.Length<3||secret.Length<8){status.text="账号至少3字符，密码至少8字符";return;}
            using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=65536};using var body=new StringContent(new JObject{["username"]=user,["password"]=secret,["nickname"]=user}.ToString(Newtonsoft.Json.Formatting.None),Encoding.UTF8,"application/json");using var response=await http.PostAsync(control.ServerUrl+"/api/register",body);if(!response.IsSuccessStatusCode)throw new InvalidOperationException();await control.Login(user,secret);status.text="账号创建成功 · 本人ID "+control.PlayerId;
        }
        private async Task Enqueue(string fill){await control.Request("v2.match.enqueue",new{plan=Plan(fill),task=Selection()});queued=true;status.text="正在排队，等待确认";}
        private async Task Confirm(){var selected=proposal;var response=await control.Request("v2.match.proposal_confirm",new{proposal_id=(string)(selected["proposal_id"]??selected["id"]),revision=(long)selected["revision"]});proposal=null;var run=(string)response["data"]?["run_id"];if(!string.IsNullOrEmpty(run))await BeginRun(run);else status.text="已确认，等待其余队友";}
        private async Task CreateParty(){party=(JObject)(await control.Request("v2.party.create",new{}))["data"];await control.Request("v2.party.plan_update",new{party_id=(string)party["id"],plan=Plan()});await RefreshParty();}
        private async Task RefreshParty(){if(party!=null){party=(JObject)(await control.Query("/api/v2/parties/"+(string)party["id"]))["data"];mode=(string)party["plan"]?["mode"]??"normal";}}
        private async Task SubmitSelection(){var selection=Selection();selection["party_id"]=party["id"];await control.Request("v2.party.selection",selection);await RefreshParty();}
        private async Task Ready(){await SubmitSelection();var own=party["members"].First(item=>(long)item["player_id"]==control.PlayerId);await control.Request("v2.party.ready",new{party_id=(string)party["id"],ready=true,roster_version=(long)party["roster_version"],plan_version=(long)party["plan_version"],selection_version=(long)own["selection_version"]});await RefreshParty();}
        private async Task RenderContracts(int current)
        {
            var data=(await control.Query("/api/v2/tasks"))["data"];if(current!=revision)return;
            var rows=data as JArray??data["items"] as JArray??data["tasks"] as JArray??new JArray();
            for(var index=0;index<taskIDs.Count;index++)
            {
                var selected=index;var task=rows.FirstOrDefault(item=>(string)(item["task"]?["key"]??item["task"]?["id"]??item["task_key"]??item["id"])==taskIDs[index]);var progress=task?["progress"];var valid=index==0||task!=null&&(bool?)progress?["unlocked"]!=false&&(bool?)progress?["completed"]!=true;
                var button=AddButton("Contract "+index,(selectedTask==taskIDs[index]?"✓ ":"")+taskNames[index]+(valid?"":" · 未解锁/已完成"),0,index*50,550,async()=>{selectedTask=taskIDs[selected];if(party!=null)await SubmitSelection();status.text="已选择："+taskNames[selected];});button.interactable=valid;
            }
            var selectedRow=rows.FirstOrDefault(item=>(string)item["task"]?["key"]==selectedTask);var details="每局最多一个或不选。完整条件、进度和奖励仅本人可见。\n\n普通同语义进度跨局保留，链的后续合同下局才激活。";
            if(selectedRow!=null)
            {
                var definition=selectedRow["task"];var progress=selectedRow["progress"];details=taskNames[taskIDs.IndexOf(selectedTask)]+"\n\n";
                foreach(var objective in definition["objectives"]){var type=(string)objective["type"];var scope=(string)objective["scope"];var label=type=="damage"?"有效伤害动作":type=="kill"?"合法击杀":type=="interact"?"机关完成":"主线指定区域到达";var prerequisite=((JArray)objective["depends_on"]??new JArray()).Count>0;details+=label+"  "+(progress["counts"]?[(string)objective["key"]]??new JValue(0))+" / "+objective["required"]+"\n"+(scope=="team"?"全队合法贡献":scope=="eligible"?"操作者或具备真实防守证据":"本人有效行为")+(prerequisite?" · 须先完成前置目标":"")+"\n\n";}
                if((bool?)definition["requires_success"]==true)details+="需要本局成功。\n";if((bool?)definition["no_knockdowns"]==true)details+="本人本局未倒地，且有有效参与。\n";
                if(selectedTask=="dc_precision")details+="按整次有效普攻/施放合并统计，不代表命中率或伤害金额。\n";
                details+="\n完成奖励："+definition["reward"]+"\n"+((bool?)progress["paused"]==true?"合同已暂停，不兼容时不推进，但可参战并获得合法基础奖。":"奖励由服务在合法完成后可靠结算，不重复发放。");
            }
            var detailRoot=new GameObject("Contract details",typeof(RectTransform));detailRoot.transform.SetParent(content,false);DcP5Theme.Place((RectTransform)detailRoot.transform,610,0,530,408);DcP5Theme.ScrollText(detailRoot.transform,"Private contract details",details,530,408,18);
        }
        private async Task RenderSocial(int current)
        {
            var friends=(await control.Query("/api/v2/friends"))["data"] as JArray??new JArray();var requests=(await control.Query("/api/v2/friends/requests"))["data"] as JArray??new JArray();if(current!=revision)return;
            peer=Field("Friend ID","对方玩家ID",0,0,330);AddButton("Add friend","申请好友",350,0,200,async()=>{if(!long.TryParse(peer.text,out var id)||id<=0)throw new InvalidOperationException();await control.Request("v2.social.friend_request",new{player_id=id});status.text="好友申请已发送";});
            var line=0;foreach(var friend in friends.Take(4)){var id=(long)friend["id"];DcP5Theme.Text(content,"Friend "+id,Safe((string)friend["nickname"])+" · "+id,0,74+line*62,320,50,17);AddButton("Invite "+id,"邀请入队",350,70+line*62,200,async()=>{if(party==null)await CreateParty();await control.Request("v2.party.invite",new{party_id=(string)party["id"],player_id=id});status.text="已发送邀请";});line++;}
            line=0;foreach(var request in requests.Where(item=>(long)item["recipient_id"]==control.PlayerId&&(string)item["status"]=="pending").Take(3)){var id=(long)request["id"];var sender=(long)request["requester_id"];AddButton("Accept friend "+id,"接受好友申请 · "+sender,610,line++*62,530,async()=>{await control.Request("v2.social.friend_response",new{request_id=id,accept=true});});}
            if(invitation!=null){var token=(string)invitation["token"];AddButton("Accept party","接受队伍邀请",610,210,530,async()=>{party=(JObject)(await control.Request("v2.party.accept_invite",new{token}))["data"];invitation=null;page=0;});}
            if(regroup!=null){var selected=regroup;AddButton("Accept regroup","同意上局队伍续组",610,272,530,async()=>{await control.Request("v2.party.regroup_respond",new{proposal_id=(string)selected["proposal_id"],revision=(long)selected["revision"],accept=true});regroup=null;});}
            if(!string.IsNullOrEmpty(lastRun)&&previousMembers.Count>1)AddButton("Regroup","邀请上局队友续组",0,334,550,async()=>{await control.Request("v2.party.regroup_propose",new{run_id=lastRun,owner_id=control.PlayerId,player_ids=previousMembers.Select(long.Parse).ToArray()});status.text="续组邀请已发送";});
            DcP5Theme.Text(content,"Social note","更多好友、私聊和招募管理沿用现有控制面。",610,346,530,52,15);
        }
        private async Task RenderRecords(int current)
        {
            var data=(await control.Query("/api/v2/me/run-records?page=1&page_size=4"))["data"];var count=boardPlayers;var board=(await control.Query("/api/v2/trials/dc_ruins_trial/leaderboard?trial_version="+(string)config["trial"]["version"]+"&initial_player_count="+count+"&page_size=4"))["data"];if(current!=revision)return;
            DcP5Theme.Text(content,"Record title","本人近期远征",0,0,550,60,22);var line=0;foreach(var record in data["items"] as JArray??new JArray()){var elapsed=(long?)record["elapsed_ms"]??0;DcP5Theme.Text(content,"Record "+line,((string)record["reason"]=="success"?"成功":"结束")+" · "+elapsed/60000+":"+(elapsed/1000%60).ToString("D2")+" · "+((string)record["mode"]=="trial"?"试炼":"普通")+"\n"+((bool?)record["trial_eligible"]==true?"成绩符合资格":"非有效试炼成绩"),0,70+line++*76,550,70,16);}
            DcP5Theme.Text(content,"Trial title","好友固定试炼 · "+count+"人",610,0,380,60,22);AddButton("Compare players",count+"人 · 切换",1000,0,144,()=>{boardPlayers=boardPlayers%4+1;return Task.CompletedTask;});line=0;foreach(var record in board["items"] as JArray??new JArray()){var elapsed=(long)record["elapsed_ms"];DcP5Theme.Text(content,"Friend record "+line,elapsed/60000+":"+(elapsed/1000%60).ToString("D2")+" · "+Safe((string)record["visible_names"])+"\n"+Safe((string)record["completed_at"]),610,70+line++*76,530,70,16);}
            if(line==0)DcP5Theme.Text(content,"No records","暂无已可靠结算的有效成绩。\n工程测试、失败及技术中止不进入好友比较。",610,60,530,170,19);
        }
        private void RenderSettings()
        {
            var settings=DcP5Settings.Current;
            AddButton("Text scale","字号 "+Mathf.RoundToInt(settings.TextScale*100)+"%",0,0,550,()=>{settings.TextScale=settings.TextScale>=1.5f?1:settings.TextScale+0.25f;settings.Save();return Task.CompletedTask;});
            AddButton("Effect volume","音效 "+Mathf.RoundToInt(settings.EffectsVolume*100)+"%",610,0,530,()=>{settings.EffectsVolume=settings.EffectsVolume>=0.99f?0:Mathf.Min(1,settings.EffectsVolume+0.1f);settings.Save();return Task.CompletedTask;});
            AddButton("Reduced flash","减少闪白 · "+(settings.ReducedFlash?"开":"关"),0,70,550,()=>{settings.ReducedFlash=!settings.ReducedFlash;settings.Save();return Task.CompletedTask;});
            AddButton("Music volume","氛围音量 "+Mathf.RoundToInt(settings.MusicVolume*100)+"%",610,70,530,()=>{settings.MusicVolume=settings.MusicVolume>=0.99f?0:Mathf.Min(1,settings.MusicVolume+0.1f);settings.Save();return Task.CompletedTask;});
            AddButton("Shake","镜头震动 · "+(settings.Shake==0?"关":"开"),0,140,550,()=>{settings.Shake=settings.Shake==0?0.5f:0;settings.Save();return Task.CompletedTask;});
            AddButton("Fullscreen","窗口 / 全屏",610,140,530,()=>{Screen.fullScreen=!Screen.fullScreen;return Task.CompletedTask;});
            DcP5Theme.Text(content,"Settings note","完整改键、准心、攻击提示大小与伤害数字设置在游戏Esc菜单。\n设置保存在本机，音量分别独立。\n\n原创像素与合成音频许可、Noto Sans SC OFL及第三方许可随包提供。",0,230,1140,160,19);
        }
        private static string Safe(string value)=>string.IsNullOrEmpty(value)?"":value.Substring(0,Math.Min(48,value.Length)).Replace("\n"," ").Replace("\r"," ");
        private void CreateControl()
        {
            if(control!=null){control.Received-=Notification;control.ConnectionStateChanged-=LobbyConnectionChanged;Destroy(control);party=null;proposal=null;invitation=null;regroup=null;queued=false;selectedTask="";lastRun=null;previousMembers.Clear();departedRuns.Clear();}
            control=gameObject.AddComponent<PveControlClient>();control.ServerUrl="http://"+DcLanAddress.Validate(DcBootstrap.Argument("-dc-server-host","127.0.0.1"))+":12880";control.Received+=Notification;control.ConnectionStateChanged+=LobbyConnectionChanged;
        }
        private void LobbyConnectionChanged(string state)
        {
            if(playing||status==null||control.IsConnected)return;
            status.text=control.HasSession?"大厅连接中断，可恢复连接后核对服务状态":"登录已失效，请重新登录";
            if(!busy)_=Render();
        }
        private async Task RefreshActivity()
        {
            var data=(await control.Query("/api/v2/me/activity"))["data"];var locked=(string)(data["activity_lock"] as JObject)?["type"];queued=locked=="ticket";
            var active=data["run"] as JObject;var activeRun=(string)active?["id"];
            var member=(active?["participants"] as JArray)?.FirstOrDefault(item=>(string)item["player_id"]==control.PlayerId.ToString());var left=(string)member?["status"]=="left";
            if(!string.IsNullOrEmpty(activeRun)&&!left&&!departedRuns.Contains(activeRun)&&((string)active["status"]=="running"||(string)active["status"]=="loading")){await BeginRun(activeRun);return;}
            if(!string.IsNullOrEmpty((string)data["settlement_run_id"])||active!=null)status.text="上一局结果仍在可靠确认，稍后可查看记录。";
            proposal=data["proposal"] as JObject;
            if(data["party"] is JObject team){party=team;mode=(string)team["plan"]?["mode"]??"normal";}else party=null;
        }
        private async Task PollActivity()
        {
            if(busy||playing)return;busy=true;
            var before=(party?.ToString()??"")+(proposal?.ToString()??"")+queued;
            try{await RefreshActivity();var after=(party?.ToString()??"")+(proposal?.ToString()??"")+queued;if(before!=after&&!playing)await Render();}
            catch(Exception){status.text="大厅状态暂时不可用，请检查连接。";}
            finally{busy=false;}
        }
        private void Update(){if(!playing&&!busy&&control?.IsConnected==true&&Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+3;_=PollActivity();}}
        private void Notification(JObject message)
        {
            var type=(string)message["type"];var data=message["data"] as JObject;
            if(data==null||playing)return;
            if(type=="v2.match.proposal"){proposal=data;queued=true;page=0;_=Render();}
            else if(type=="v2.match.assigned"){var run=(string)data["run_id"];if(!string.IsNullOrEmpty(run))_=BeginRun(run);}
            else if(type=="v2.party.invited"){invitation=data;status.text="收到好友队伍邀请，请打开好友与队伍";}
            else if(type=="v2.party.regroup_proposed"){regroup=data;status.text="收到续组邀请，请打开好友与队伍";}
            else if(type=="v2.party.regroup_committed"){party=new JObject{["id"]=data["party_id"]};_=Run(RefreshParty);}
            else if(type.StartsWith("v2.party.",StringComparison.Ordinal)&&party!=null)_=Run(RefreshParty);
        }
        private async Task BeginRun(string run)
        {
            if(playing||string.IsNullOrEmpty(run)||departedRuns.Contains(run))return;playing=true;queued=false;proposal=null;lastRun=run;status.text="正在连接战斗服务……";
            await control.Disconnect();canvas.gameObject.SetActive(false);combat=new GameObject("P5 combat session").AddComponent<DcP0V3>();combat.AdoptP5Session(this,run,control.SessionToken,control.PlayerId.ToString());
        }
        internal void RememberRoster(JArray members){previousMembers.Clear();previousMembers.AddRange(members.Select(item=>(string)item["player_id"]));}
        internal async Task ReturnFromRun(bool confirmed)
        {
            if(combat==null)return;
            try
            {
                // Quiesce the old client before the lobby takes over its account
                // connection. Otherwise its reconnect can re-enter a departed Run.
                combat.PrepareP5Return();
                await control.Connect();if(!confirmed)await control.Request("v2.run.leave",new{run_id=lastRun});
                if(!departedRuns.Contains(lastRun)){departedRuns.Enqueue(lastRun);while(departedRuns.Count>8)departedRuns.Dequeue();}
                var old=combat;combat=null;Destroy(old.gameObject);Time.timeScale=1;playing=false;canvas.gameObject.SetActive(true);status.text="已返回大厅，服务保存确认进度与结果。";await RefreshActivity();await Render();
            }
            catch(Exception){status.text="退出尚未确认，请检查连接后重试";}
        }
        private void LaunchPractice()
        {
            if(queued||party!=null){status.text="请先退出排队或队伍，再进入本机安全练习";return;}
            var executable=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            System.Diagnostics.Process.Start(PracticeStartInfo(executable,loadout,DcV8Profile.Active?"v8":DcV7Profile.Active?"v7":DcV6Profile.Active?"v6":"v5"));
        }
        private static System.Diagnostics.ProcessStartInfo PracticeStartInfo(string executable,string selected,string runtime)
        {
            if((selected!="ranger"&&selected!="runesmith")||(runtime!="v5"&&runtime!="v6"&&runtime!="v7"&&runtime!="v8"))throw new ArgumentException("Unsupported practice profile");
            var info=new System.Diagnostics.ProcessStartInfo(executable,"-job-worker-count 2 -dc-runtime "+runtime+" -dc-p5 true -dc-practice true -dc-loadout "+selected+" -screen-fullscreen 0 -screen-width 1280 -screen-height 720"){UseShellExecute=false};
            foreach(var key in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
            {
                var name=key.ToUpperInvariant();
                if(name.Contains("PASSWORD")||name.Contains("SECRET")||name.Contains("TOKEN")||name.Contains("API_KEY")||name=="DC_ADMISSION_KEY")info.EnvironmentVariables.Remove(key);
            }
            return info;
        }
        private System.Collections.IEnumerator CaptureLobby(){yield return new WaitForSecondsRealtime(2);yield return new WaitForEndOfFrame();var path=Path.GetFullPath(DcBootstrap.Argument("-dc-p5-lobby-capture"));if(path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))ScreenCapture.CaptureScreenshot(path);yield return new WaitForSecondsRealtime(0.4f);Application.Quit(0);}
        private async Task P5LobbyProbe()
        {
            try
            {
                await Task.Delay(500);username.text=DcBootstrap.Argument("-dc-user");password.text=File.ReadAllText(DcBootstrap.Argument("-dc-password-file")).Trim();content.Find("Login").GetComponent<Button>().onClick.Invoke();
                await P5ProbeWait(()=>control.HasSession&&!busy,"login");
                page=2;await Render();if(content.GetComponentsInChildren<Button>().Count(button=>button.name.StartsWith("Contract ",StringComparison.Ordinal))!=7)throw new InvalidOperationException("P5 contract page incomplete");
                page=3;await Render();page=0;await Render();var runs=new JArray();var generations=new JArray();var assignments=new JArray();var loadouts=new JArray();
                for(var attempt=0;attempt<2;attempt++)
                {
                    loadout=attempt==0?"ranger":"runesmith";await Render();content.Find("Solo").GetComponent<Button>().onClick.Invoke();await P5ProbeWait(()=>proposal!=null&&!busy,"proposal");content.Find("Confirm").GetComponent<Button>().onClick.Invoke();await P5ProbeWait(()=>combat!=null&&combat.P5Loaded,"real admission");runs.Add(lastRun);generations.Add(combat.P5Generation);assignments.Add(combat.P5Assignment);loadouts.Add(combat.P5Loadout);if(combat.P5Loadout!=loadout)throw new InvalidOperationException("P5 previous world loadout leaked into new assignment");
                    if(!await combat.ProbeP5Markers(attempt==0?null:(string)runs[0]))throw new InvalidOperationException("P5 marker or old Run rejection failed");
                    await Task.Delay(500);await ReturnFromRun(false);await P5ProbeWait(()=>!playing&&combat==null,"return to lobby");await Task.Delay(2000);await RefreshActivity();
                }
                if((string)runs[0]==(string)runs[1]||(string)assignments[0]==(string)assignments[1]||generations.Any(value=>(long)value<1))throw new InvalidOperationException("P5 reused old Run/assignment identity");
                var path=Path.GetFullPath(DcBootstrap.Argument("-dc-p5-lobby-output"));if(!path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("P5 lobby evidence path");
                File.WriteAllText(path,new JObject{["profile"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":DcV6Profile.Active?"dc_ruins.v6.p5.2":"dc_ruins.v5.p5.1",["human"]=false,["same_client"]=true,["runs"]=runs,["source_generations"]=generations,["assignment_ids"]=assignments,["loadouts"]=loadouts,["login_button"]=true,["contracts_page"]=true,["records_page"]=true,["proposal_buttons"]=true,["two_admissions"]=true,["public_leave"]=true,["returned_to_lobby"]=true,["status"]="PASS"}.ToString());Application.Quit(0);
            }
            catch(Exception failure){Debug.LogError("P5 lobby probe: "+failure.GetType().Name+" "+(failure is InvalidOperationException?failure.Message:"request failed"));Application.Quit(2);}
        }
        private static async Task P5ProbeWait(Func<bool> ready,string stage)
        {
            var end=Time.realtimeSinceStartupAsDouble+35;while(!ready()&&Time.realtimeSinceStartupAsDouble<end)await Task.Delay(50);if(!ready())throw new InvalidOperationException("P5 lobby timed out at "+stage);
        }
    }
}
