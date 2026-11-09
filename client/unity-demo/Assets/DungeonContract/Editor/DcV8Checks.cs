using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace DungeonContract.Editor {
 public static class DcV8Checks {
  private static JObject parameters,geometry,configuration;private static readonly List<string> passed=new List<string>();private static int fixtureSerial;
  private static void Require(bool v,string m){if(!v)throw new InvalidOperationException(m);}
  private static void Case(string name,Action test){test();passed.Add(name);Debug.Log("V8 CHECK "+name);}
  private static JObject Point(float x,float y)=>new JObject{["x"]=x,["y"]=y};
  private static DcP1World World(string kit="ranger",bool melee=false,bool dense=false){
   var g=(JObject)geometry.DeepClone();if(!dense)foreach(var room in (JArray)g["rooms"]){room["obstacles_milli"]=new JArray();room["cover_points_milli"]=new JArray();room["trap_points_milli"]=new JArray();room["covers"]=new JArray();room["traps"]=new JArray();}
   var w=new DcP1World(new JObject{["run_id"]="v8_isolated_model_"+(++fixtureSerial),["seed"]=17,["parameters"]=parameters,["p1_world"]=g,["config"]=configuration,["p4_stage"]=true,["p3_stage"]=true,["p2_stage"]=true,["weapon_stage"]=true,["members"]=new JArray(new JObject{["player_id"]="1",["slot"]=0,["loadout_id"]=kit}),["task_requirements"]=new JObject{["1"]=new JObject{["damage:"]=100}}});
   Change(w,s=>{s["players"][0]["Room"]=1;s["players"][0]["Position"]=Point(5,9);s["players"][0]["MeleeEquipped"]=melee;s["rooms"][1]["Status"]="active";s["rooms"][1]["Wave"]=1;s["enemies"]=new JArray(new JObject{["ID"]="foe_1",["Kind"]="elite",["Room"]=1,["Position"]=Point(20,9),["TickStart"]=Point(20,9),["Origin"]=Point(20,9),["Health"]=10000,["Maximum"]=10000,["ReadyAt"]=long.MaxValue});});return w;
  }
  private static JObject Own(DcP1World w)=>(JObject)w.Snapshot()["players"][0];
  private static void Change(DcP1World w,Action<JObject> action){var s=w.Capture();action(s);w.Restore(s);}
  private static DcP1Input Input(DcP1World w,Vector2 move=default,bool held=false,ushort aim=0){var p=Own(w);return new DcP1Input{WeaponID=(string)p["active_weapon_id"],WeaponRevision=(uint)p["weapon_revision"],Aim=aim,Move=move,Melee=held&&(bool)p["melee_equipped"],Attack=held&&!(bool)p["melee_equipped"],AttackEpisode=(string)p["attack_episode_id"],ChargeEpisode=(string)p["charge_episode_id"]};}
  private static void Step(DcP1World w,int ticks,Vector2 move=default,bool held=false,ushort aim=0){var begin=w.GameTime;for(var i=1;i<=ticks;i++){w.Input("1",Input(w,move,held,aim));w.Tick(begin+(i*1000+29)/30);w.DrainV6Lifecycle();}}
  private static JObject Bullet(float x=8,float y=9.55f,float speed=10,bool enemy=false)=>new JObject{["ID"]="projectile_009001",["ProtocolVersion"]=8,["Action"]="projectile_009001",["Root"]="root_9001",["Owner"]=enemy?"foe_1":"1",["SourceLife"]=1,["Room"]=1,["Position"]=Point(x,y),["Direction"]=Point(1,0),["Damage"]=16,["Speed"]=speed,["CollisionRadius"]=.3f,["Enemy"]=enemy,["RuneBolt"]=!enemy,["BornGameMS"]=0,["ExpiresGameMS"]=enemy?0:6000,["Unbounded"]=true,["NetworkGroup"]=1,["Hit"]=new JArray()};
  private static void Upgrade(DcP1World w,params string[] ids)=>Change(w,s=>s["players"][0]["Upgrades"]=new JArray(ids));
  private static int EnemyHP(DcP1World w)=>(int)w.Capture()["enemies"][0]["Health"];
  private static void Nearby(DcP1World w,float x=6)=>Change(w,s=>{s["enemies"][0]["Position"]=Point(x,9);s["enemies"][0]["TickStart"]=Point(x,9);});
  private static bool Cast(DcP1World w,string kind)=>w.Action("1",kind,Input(w));
  public static void Regression(){DcV7Checks.Run();DcV7Checks.Regression();}
  private static void BroadphaseRegressions(){
   Case("cached body-pair broadphase and separation match precise contour rules",()=>{
    var random=new System.Random(19109);var profiles=((JObject)parameters["body_profiles"]).Properties().Select(property=>{var split=property.Name.LastIndexOf(':');return DcV7Geometry.Profile(parameters,property.Name.Substring(0,split),property.Name.Substring(split+1));}).ToArray();
    foreach(var a in profiles)foreach(var b in profiles)for(var sample=0;sample<8;sample++){
     var at=new Vector2(5,9);var bt=at+new Vector2((float)random.NextDouble()*4-2,(float)random.NextDouble()*4-2);var delta=(at+a.Center)-(bt+b.Center);var distance=delta.magnitude;var away=distance>.001f?delta/distance:Vector2.right;var gap=sample%2==0?.9f:1.2f;
     var support=a.Vertices.Max(v=>Vector2.Dot(v-a.Center,-away))+b.Vertices.Max(v=>Vector2.Dot(v-b.Center,away))+.01f;var minimum=Math.Max(gap,support);var expected=distance<minimum?away*(minimum-distance)*2:Vector2.zero;
     Require((expected-DcV7Geometry.Separation(a,at,b,bt,gap,true)).sqrMagnitude<1e-10f,"separation changed visible spacing");
     Require(DcV7Geometry.Overlap(a.Vertices,at,b.Vertices,bt)==DcV7Geometry.OverlapBodies(a,at,b,bt),"body broadphase dropped an overlap");
    }
   });
   Case("cached body broadphase matches exact circle contacts for all directional profiles",()=>{
    var random=new System.Random(81723);
    foreach(var property in ((JObject)parameters["body_profiles"]).Properties()){
     var split=property.Name.LastIndexOf(':');var body=DcV7Geometry.Profile(parameters,property.Name.Substring(0,split),property.Name.Substring(split+1));
     Require(body.Vertices.All(v=>v.x>=body.Min.x-.000001f&&v.x<=body.Max.x+.000001f&&v.y>=body.Min.y-.000001f&&v.y<=body.Max.y+.000001f),"cached bounds exclude visible contour");
     for(var i=0;i<128;i++){
      var a=new Vector2((float)random.NextDouble()*12-6,(float)random.NextDouble()*12-6);var b=new Vector2((float)random.NextDouble()*12-6,(float)random.NextDouble()*12-6);var radius=.01f+(float)random.NextDouble()*.72f;
      var exact=DcV7Geometry.Circle(a,b,body.Vertices,radius);var fast=DcV7Geometry.CircleBody(a,b,body,radius);
      Require(exact.HasValue==fast.HasValue&&(!exact.HasValue||Math.Abs(exact.Value-fast.Value)<.000001f),"broadphase changed a continuous contact");
     }
    }
   });
   Case("swept polygon bounds retain fast crossing and Minkowski rectangle contacts",()=>{
    var random=new System.Random(9126);var body=DcV7Geometry.Rect(-.2f,-.1f,.4f,.2f);var target=DcV7Geometry.Rect(4,4,1,1);
    for(var i=0;i<1000;i++){
     var a=new Vector2((float)random.NextDouble()*12-3,(float)random.NextDouble()*12-3);var b=new Vector2((float)random.NextDouble()*12-3,(float)random.NextDouble()*12-3);
     if(a.x>=3.8f&&a.x<=5.2f&&a.y>=3.9f&&a.y<=5.1f)continue;
     var expected=DcV6Geometry.Box(a,b,3.8f,3.9f,1.4f,1.2f,0);var actual=DcV7Geometry.Sweep(body,a,b,target);
     Require(expected.HasValue==actual.HasValue&&(!expected.HasValue||Math.Abs(expected.Value.Time-actual.Value.Time)<.0001f),"swept bounds discarded or changed a contact");
    }
    Require(DcV7Geometry.Sweep(body,new Vector2(-20,4.5f),new Vector2(20,4.5f),target).HasValue,"high-speed crossing skipped");
   });
  }
  private static void StaleSwitchRegression(){
   var world=World("runesmith");var old=Input(world);Require(Cast(world,"switch_weapon"),"setup switch");
   var game=new GameObject("isolated_stale_switch_admission");var runtime=game.AddComponent<DcP0V3>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(DcP0V3).GetField(name,flags).SetValue(runtime,value);
   try{
    const ulong peer=91;((Dictionary<ulong,JObject>)typeof(DcP0V3).GetField("identities",flags).GetValue(runtime)).Add(peer,new JObject{["player_id"]="1",["connection_generation"]=1});
    ((HashSet<string>)typeof(DcP0V3).GetField("loaded",flags).GetValue(runtime)).Add("1");
    Set("assignment",new JObject{["run_id"]="isolated",["source_generation"]=1});Set("p1World",world);Set("v5Runtime",true);
    var action=Guid.NewGuid().ToString();var request=new JObject{["protocol_version"]=8,["message"]="dc.action.v8",["run_id"]="isolated",["source_generation"]=1,["connection_generation"]=1,["operation_id"]=action,["action_id"]=action,["input_sequence"]=1,["action_sequence"]=1,["weapon_id"]=old.WeaponID,["weapon_revision"]=old.WeaponRevision,["requested_weapon_id"]="rune_hammer",["action"]="switch_weapon",["press_action_id"]=null,["aim_u16"]=0,["move_x"]=0,["move_y"]=0};
    var wire=typeof(DcP0V3).GetMethod("WireName",flags);request["message"]=(string)wire.Invoke(runtime,new object[]{"dc.action.v3"});
    typeof(DcP0V3).GetMethod("S2ActionMessage",flags).Invoke(runtime,new object[]{peer,request});Step(world,1);
    Require(world.Receipt(action)=="stale_weapon_revision"&&world.ActiveWeapon("1")=="rune_hammer"&&world.WeaponActionSequence("1")==1,"stale request must reject once without switching or disconnecting");
    var invalid=Guid.NewGuid().ToString();request["action_id"]=request["operation_id"]=invalid;request["action_sequence"]=2;request["requested_weapon_id"]="third_weapon";
    var rejected=false;try{typeof(DcP0V3).GetMethod("S2ActionMessage",flags).Invoke(runtime,new object[]{peer,request});}catch(System.Reflection.TargetInvocationException e){rejected=e.InnerException is InvalidOperationException&&e.InnerException.Message=="S2 switch target";}
    Require(rejected&&world.WeaponActionSequence("1")==1,"foreign weapon bypassed admission");
   }finally{UnityEngine.Object.DestroyImmediate(game);}
  }
  private static void PilotDodgeRegression(){
   var world=World();var state=world.Snapshot();var own=state["players"][0];var shot=Bullet((float)own["body_center_x"],(float)own["body_center_y"],10,true);shot["id"]="projectile_009001";shot["room_id"]=1;shot["x"]=shot["Position"]["x"];shot["y"]=shot["Position"]["y"];shot["direction_x"]=1;shot["direction_y"]=0;shot["speed"]=10;shot["radius"]=.3f;shot["enemy"]=true;state["projectiles"]=new JArray(shot);
   var input=Input(world,Vector2.right);var actions=Array.Empty<string>();DcV7Pilot.Adapt(state,world.Definition,"1",ref input,ref actions,false);Require(actions.Contains("dodge"),"dodge setup must face actual danger");
   input=Input(world,Vector2.right);actions=Array.Empty<string>();DcV7Pilot.Adapt(state,world.Definition,"1",ref input,ref actions,false);Require(!actions.Contains("dodge"),"unchanged delayed readiness generated a per-frame action flood");
  }
  private static void ProbeAttackEpisodeRegression(){
   var world=World();Step(world,1,held:true);Require(!world.Snapshot()["projectiles"].Any(),"held without an accepted press bypassed the attack episode");
   Require(Cast(world,"attack_press"),"first ordinary press");var first=(string)Own(world)["attack_episode_id"];Step(world,12,held:true);
   Require(!string.IsNullOrEmpty(first)&&world.Snapshot()["projectiles"].Any(p=>(string)p["attack_episode_id"]==first),"ordinary held input did not follow its reliable press");
   world.Reconnect("1");Step(world,1);Require(Cast(world,"attack_press")||world.LastActionReason=="cooldown","post-reconnect ordinary press: "+world.LastActionReason);var next=(string)Own(world)["attack_episode_id"];Step(world,12,held:true);
   Require(next!=first&&world.Snapshot()["projectiles"].Any(p=>(string)p["attack_episode_id"]==next),"reconnect reused a stale attack episode");
  }
  private static void LastEnemyNavigationRegression(){
   var fixture=JObject.Parse(File.ReadAllText("Assets/DungeonContract/Editor/Fixtures/v8-last-enemy-navigation.json"));var world=World();
   Change(world,s=>{s["players"][0]["Room"]=3;s["players"][0]["Position"]=Point((float)fixture["own"]["x"],(float)fixture["own"]["y"]);s["players"][0]["BodyFacing"]="right";s["rooms"][3]["Status"]="active";});
   var state=world.Snapshot();state["enemies"]=fixture["enemies"].DeepClone();state["covers"]=fixture["covers"].DeepClone();state["elapsed_ms"]=1200;state["dynamic_elapsed_ms"]=1200;state["traps"]=new JArray(fixture["layout"]["traps"].Select(t=>new JObject{["id"]=t["id"],["room_id"]=3,["x"]=(float)t["x_milli"]/1000,["y"]=(float)t["y_milli"]/1000,["phase"]=(string)t["group"]=="B"?"warning":"cooldown",["remaining_ms"]=(string)t["group"]=="B"?400:1800}));
   var map=(JObject)world.Definition.DeepClone();map["rooms"][3]=fixture["layout"].DeepClone();var input=Input(world,held:true);input.Aim=(ushort)fixture["input"]["aim"];var actions=Array.Empty<string>();

   DcV7Pilot.Adapt(state,map,"1",ref input,ref actions,false);Require(input.Move.sqrMagnitude>.01f,"last-enemy corner stall: no reachable firing approach selected");
  }
  private static void TrapNavigationClockRegression(){
   foreach(var phase in new[]{"warning","active","cooldown","idle"})foreach(var remaining in new[]{0,200,400,401,1800,2200}){
    var trap=new JObject{["phase"]=phase,["remaining_ms"]=remaining};var expected=phase=="warning"||phase=="active"||remaining<=400;
    Require(DcV7Pilot.TrapInMoveHorizon(trap)==expected,"trap move horizon changed "+phase+":"+remaining);
   }
   Require(DcV7Pilot.TrapInMoveHorizon(new JObject()),"unknown trap clock was treated as safe");
  }
  private static void RecordedMeleeNavigationRegression(){
   var fixture=JObject.Parse(File.ReadAllText("Assets/DungeonContract/Editor/Fixtures/v8-melee-east-navigation.json"));
   foreach(var enemy in fixture["enemies"].Where(e=>(string)e["kind"]=="guard")){
    var world=World();world.Definition["rooms"][1]=fixture["layout"].DeepClone();
    Change(world,s=>{s["players"][0]["Position"]=Point((float)fixture["own"]["x"],(float)fixture["own"]["y"]);s["players"][0]["Health"]=10000;s["covers"]=new JArray(fixture["covers"].Select(c=>new JObject{["ID"]=c["id"],["Room"]=1,["Position"]=Point((float)c["x"],(float)c["y"]),["Health"]=c["health"],["Revision"]=c["revision"]}));s["enemies"]=new JArray(new JObject{["ID"]=enemy["id"],["Kind"]="guard",["Room"]=1,["Position"]=Point((float)enemy["x"],(float)enemy["y"]),["TickStart"]=Point((float)enemy["x"],(float)enemy["y"]),["StuckPosition"]=Point((float)enemy["x"],(float)enemy["y"]),["BodyFacing"]=enemy["body_facing"],["Health"]=10000,["Maximum"]=10000,["AttackAt"]=long.MaxValue});});
    Step(world,120);var after=world.Capture()["enemies"][0];var from=new Vector2((float)enemy["x"],(float)enemy["y"]);var to=new Vector2((float)after["Position"]["x"],(float)after["Position"]["y"]);
    Require(Vector2.Distance(from,to)>1,"recorded east-side "+enemy["id"]+" made no legal movement: "+to+" recovery="+after["RecoveryCount"]);
    Require((int)after["RecoveryCount"]==0,"recorded navigation required recovery teleport");
   }
   var crowd=World();crowd.Definition["rooms"][1]=fixture["layout"].DeepClone();
   Change(crowd,s=>{s["players"][0]["Position"]=Point((float)fixture["own"]["x"],(float)fixture["own"]["y"]);s["players"][0]["Health"]=10000;s["covers"]=new JArray(fixture["covers"].Select(c=>new JObject{["ID"]=c["id"],["Room"]=1,["Position"]=Point((float)c["x"],(float)c["y"]),["Health"]=c["health"],["Revision"]=c["revision"]}));s["enemies"]=new JArray(fixture["enemies"].Select(e=>new JObject{["ID"]=e["id"],["Kind"]=e["kind"],["Room"]=1,["Position"]=Point((float)e["x"],(float)e["y"]),["TickStart"]=Point((float)e["x"],(float)e["y"]),["StuckPosition"]=Point((float)e["x"],(float)e["y"]),["BodyFacing"]=e["body_facing"],["Health"]=10000,["Maximum"]=10000,["AttackAt"]=long.MaxValue}));});
   Step(crowd,150);Require(crowd.Capture()["enemies"].All(e=>(int)e["RecoveryCount"]==0),"recorded crowd required recovery instead of ordinary avoidance");
  }
  private static void GenuineBlockedEnemyRegression(){
   var world=World();((JObject)world.Definition["rooms"][1])["obstacles"]=new JArray{new JArray(6,7,1,5),new JArray(10,7,1,5),new JArray(6,7,5,1),new JArray(6,11,5,1)};
   Change(world,s=>{var e=s["enemies"][0];e["Kind"]="guard";e["Position"]=Point(8,9);e["TickStart"]=Point(8,9);e["StuckPosition"]=Point(8,9);e["StuckAt"]=0;e["ReadyAt"]=0;e["AttackAt"]=long.MaxValue;e["RecoveryCount"]=2;});
   Step(world,149);Require(!world.Terminal,"genuine movement blockage aborted before five seconds");Step(world,2);Require(world.Terminal&&world.Outcome=="aborted","local avoidance hid a genuine third movement recovery");
  }
  private static void IdleEnemyWatchdogRegression(){
   foreach(var kind in new[]{"archer","boss"}){
    var world=World();Change(world,s=>{s["players"][0]["Room"]=0;s["enemies"][0]["Kind"]=kind;s["enemies"][0]["Position"]=Point(8,9);s["enemies"][0]["StuckPosition"]=Point(8,9);s["enemies"][0]["StuckAt"]=0;s["enemies"][0]["ReadyAt"]=0;s["enemies"][0]["AttackAt"]=long.MaxValue;s["enemies"][0]["RecoveryCount"]=2;});
    Step(world,180);var state=world.Capture();Require((long)state["enemies"][0]["StuckAt"]==(long)state["combat_now"],"idle "+kind+" aged the movement watchdog without a target");
    Change(world,s=>s["players"][0]["Room"]=1);Step(world,1);Require(!world.Terminal&&(int)world.Capture()["enemies"][0]["RecoveryCount"]==2,"new target consumed a recovery before five seconds of attempted movement");
   }
  }
  private static void CombatCornerRegression(){
   var fixture=JObject.Parse(File.ReadAllText("Assets/DungeonContract/Editor/Fixtures/v8-combat-corner.json"));var world=World();var room=(int)fixture["own"]["room"];
   Change(world,s=>{s["players"][0]["Room"]=room;s["players"][0]["Position"]=Point((float)fixture["own"]["x"],(float)fixture["own"]["y"]);s["rooms"][room]["Status"]="active";});
   var state=world.Snapshot();state["enemies"]=fixture["enemies"].DeepClone();state["covers"]=fixture["covers"].DeepClone();state["traps"]=new JArray();var map=(JObject)world.Definition.DeepClone();map["rooms"][room]=fixture["layout"].DeepClone();
   var input=Input(world,held:true);input.Aim=(ushort)fixture["input"]["aim"];var actions=Array.Empty<string>();DcV7Pilot.Adapt(state,map,"1",ref input,ref actions,false);
   Require(input.Move.sqrMagnitude>.01f,"static corner lost all legal combat directions");
  }
  private static JObject CoopDefenseSnapshot(){
   var state=World().Snapshot();var first=(JObject)state["players"][0];var second=(JObject)first.DeepClone();second["player_id"]="2";second["slot"]=1;second["x"]=(float)first["x"]+.3f;second["connected"]=true;((JArray)state["players"]).Add(second);
   state["projectiles"]=new JArray(new JObject{["id"]="projectile_008888",["room_id"]=1,["enemy"]=true,["x"]=(float)first["x"]+1.6f,["y"]=(float)first["y"],["direction_x"]=-1,["direction_y"]=0,["speed"]=6,["radius"]=.1f});return state;
  }
  private static void CoopDefenseRegression(){
   var state=CoopDefenseSnapshot();var first=DcV5Pilot.Skills(state,"1").Contains("ability_secondary");var second=DcV5Pilot.Skills(state,"2").Contains("ability_secondary");
   Require(first&&!second,"overlapping available defenders spent both fields on the same immediate threat");
   state["players"][0]["secondary_remaining_ms"]=8000;Require(DcV5Pilot.Skills(state,"2").Contains("ability_secondary"),"ready defender waited for another actor's cooldown");
  }
  private static void RescueDefenseRegression(){
   var state=CoopDefenseSnapshot();state["players"][1]["health"]=0;state["players"][1]["life_state"]="downed";
   var input=new DcP1Input{Interact=true};var actions=Array.Empty<string>();DcV8CoopPilot.PrepareRescue(state,"1",ref input,ref actions);DcV7Pilot.InteractionGuard(input,ref actions);
   Require(!input.Interact&&actions.SequenceEqual(new[]{"ability_secondary"}),"rescue input swallowed the protective cast");
   input.Interact=true;actions=Array.Empty<string>();DcV8CoopPilot.PrepareRescue(state,"1",ref input,ref actions);Require(!input.Interact&&actions.Length==0,"delayed cooldown caused a defensive action flood");
   state["players"][0]["secondary_remaining_ms"]=9000;input.Interact=true;DcV8CoopPilot.PrepareRescue(state,"1",ref input,ref actions);Require(input.Interact,"accepted defense did not allow the ordinary rescue input to resume");
  }
  private static void WallLegalityRegression(){
   var world=World();Step(world,120,Vector2.up);var own=Own(world);var body=DcV7Geometry.Profile(parameters,"ranger",(string)own["body_facing"]);var at=new Vector2((float)own["x"],(float)own["y"]);var layout=(JObject)world.Definition["rooms"][1];
   Require(body.Vertices.All(v=>at.y+v.y<=(float)layout["height"]-1+.00001f),"ordinary movement crossed the true north hull");
   Require(!DcV7Geometry.BodyTerrain(layout,body,at,Array.Empty<Vector2>()),"legal authoritative wall stop was rejected by padded body bounds");
  }
  private static void PendingSwitchPressEdgeRegression(){
   var held=false;var input=new DcP1Input{Attack=true};var actions=DcV5AutoInput.Compose(ref held,true,ref input,Array.Empty<string>());Require(!held&&!input.Attack&&!actions.Contains("attack_press"),"switch wait consumed a press that was never sent");
   input.Attack=true;actions=DcV5AutoInput.Compose(ref held,false,ref input,Array.Empty<string>());Require(held&&actions.SequenceEqual(new[]{"attack_press"}),"matching switch receipt did not allow the first ordinary press");
   actions=DcV5AutoInput.Compose(ref held,false,ref input,Array.Empty<string>());Require(actions.Length==0,"continuous held input flooded repeated presses");
  }
  private static void OrdinaryLifecycleEvidenceRegression(){
   var state=new JObject{["owner"]="1",["enemy"]=false,["root"]="shot_000123",["attack_episode_id"]=Guid.NewGuid().ToString()};var message=new JObject{["events"]=new JArray(new JObject{["kind"]="spawn",["delivery_sequence"]=1,["state"]=state},new JObject{["kind"]="terminate",["delivery_sequence"]=2,["state"]=state.DeepClone()})};
   Require(DcV6LifecycleObservation.OrdinaryBirth(message,"1",2),"same-delivery birth/termination lost a real ordinary shot");Require(!DcV6LifecycleObservation.OrdinaryBirth(message,"2",2)&&!DcV6LifecycleObservation.OrdinaryBirth(message,"1",0),"foreign owner or unapplied birth counted as ordinary input");
   state["root"]="volley_000123";Require(!DcV6LifecycleObservation.OrdinaryBirth(message,"1",2),"skill shot substituted for an ordinary shot");
  }
  private static void ProjectileSnapshotBindingRegression(){
   void Check(DcP1World w){var cached=(JArray)w.RuntimeSnapshot()["projectiles"].DeepClone();var full=(JArray)w.Snapshot()["projectiles"];Require(cached.Count==full.Count,"snapshot projectile count changed");for(var i=0;i<full.Count;i++)foreach(var property in ((JObject)full[i]).Properties()){var actual=cached[i][property.Name];Require(actual!=null,"cached projectile omitted "+property.Name);var numeric=property.Value.Type==JTokenType.Integer||property.Value.Type==JTokenType.Float;Require(numeric?Math.Abs((double)actual-(double)property.Value)<.000001:JToken.DeepEquals(actual,property.Value),"cached projectile differs: "+property.Name);}}
   foreach(var kit in new[]{"ranger","runesmith"}){
    var world=World(kit);Change(world,s=>s["projectiles"]=new JArray(Bullet()));Check(world);Check(world);Step(world,3);Check(world);
    Require(world.Capture()["projectiles"][0]["RuntimeSnapshotBound"]==null,"transient snapshot binding leaked into checkpoint");
    Change(world,s=>s["projectiles"]=new JArray());Check(world);
    var shot=Bullet(enemy:true);shot["ID"]="projectile_009002";shot["Owner"]="foe_1";shot["Root"]="new_root";shot["Damage"]=27;shot["Speed"]=4.5f;Change(world,s=>s["projectiles"]=new JArray(shot));Check(world);Check(world);
    Change(world,s=>s["projectiles"][0]["Damage"]=43);Check(world);
   }
  }
  private static void DelayedSpawnFactFrontierRegression(){
   var world=World("runesmith");var shape=DcV7Geometry.Profile(parameters,"elite","down");var entry=6.5f+shape.HullMin.x-.3f;
   Change(world,s=>{var p=s["players"][0];p["Health"]=0;p["Life"]="awaiting_respawn";p["Reinforcement"]="reinforce_model";p["SpawnAwaitingAck"]=true;s["enemies"][0]["Position"]=Point(6.5f,9);s["projectiles"]=new JArray(Bullet(entry-.5f,9+shape.Center.y));});
   world.Tick(34);var pending=new JArray(world.Tick(67));var damage=pending.FirstOrDefault(f=>(string)f["kind"]=="damage");Require(damage!=null&&(long)damage["life_generation"]==1,"fixture did not preserve an old-generation projectile fact during the spawn receipt gap");
   var spawn=new JArray(new JObject{["kind"]="reinforcement_spawned",["actor_player_id"]="1",["life_generation"]=1,["reinforcement_action_id"]="reinforce_model"});var receipt=new JObject{["status"]="applied",["reinforcements"]=new JObject{["reinforce_model"]=new JObject{["player_id"]=1,["status"]="spawned"}}};
   var frontier=new DcP3FactFrontier();frontier.Confirm(spawn,1,receipt);frontier.Align(pending,2);
   Require((long)damage["life_generation"]==2,"post-spawn reliable fact retained the obsolete actor life after Go confirmed the next generation");
   Require((long)damage["proof"]["origin_life_generation"]==1,"confirmed event-life alignment changed the projectile's real emission life");
   var life=new JArray(new JObject{["kind"]="death",["actor_player_id"]="1",["life_generation"]=1});frontier.Align(life,2);Require((long)life[0]["life_generation"]==1,"life alignment masked an invalid lifecycle transition");
   var before=new JArray(new JObject{["kind"]="damage",["actor_player_id"]="1",["life_generation"]=1});frontier.Align(before,1);Require((long)before[0]["life_generation"]==1,"life alignment rewrote events before the confirmed spawn boundary");
   frontier.Clear();frontier.Align(before,2);Require((long)before[0]["life_generation"]==1,"new Run retained the prior lifecycle frontier");
  }
  private static void ShortHammerCoverDriverRegression(){
   var world=World("runesmith");var snapshot=world.Snapshot();snapshot["covers"]=new JArray(new JObject{["id"]="cover_short_hammer",["room_id"]=1,["x"]=7,["y"]=9,["health"]=30,["revision"]=0});var input=Input(world,held:true);var actions=Array.Empty<string>();
   DcV6Pilot.Route(snapshot,world.Definition,"1",ref input,ref actions,false);Require(!actions.Contains("switch_weapon")&&input.Attack&&!input.Melee,"short-hammer driver stopped outside actual reach and switched away from a legal ranged cover shot");
  }
  private static void SupportedRescueApproachRegression(){
   var world=World();var state=world.Snapshot();var own=state["players"][0];var target=(JObject)own.DeepClone();target["player_id"]="2";target["slot"]=1;target["health"]=0;target["life_state"]="downed";target["x"]=8;target["y"]=9;((JArray)state["players"]).Add(target);state["enemies"][0]["x"]=6;state["enemies"][0]["y"]=9;var input=Input(world);
   Require(DcP3Pilot.Rescue(state,world.Definition,"1",ref input,false),"ready defensive rescuer refused every approach near an enemy");
   own["x"]=6.6f;own["rescue_target"]="2";own["rescue_remaining_ms"]=2000;own["secondary_remaining_ms"]=9000;var other=(JObject)own.DeepClone();other["player_id"]="3";other["slot"]=2;other["x"]=7;other["rescue_target"]=JValue.CreateNull();other["secondary_remaining_ms"]=0;((JArray)state["players"]).Add(other);input=Input(world);
   Require(DcP3Pilot.Rescue(state,world.Definition,"1",ref input,false),"helper election abandoned a live rescue when another cooldown became ready");
  }
  private static void SquadCoverageRouteRegression(){
   var world=World();var state=world.Snapshot();var follower=(JObject)state["players"][0].DeepClone();follower["player_id"]="2";follower["slot"]=1;follower["x"]=12;follower["body_center_x"]=(float)follower["body_center_x"]+7;((JArray)state["players"]).Add(follower);var input=Input(world,held:true);var actions=Array.Empty<string>();
   DcV7Pilot.Adapt(state,world.Definition,"2",ref input,ref actions,false);Require(input.Move.x<-.1f,"autoplay follower left the ready defender's coverage instead of regrouping");
  }
  private static void VolleyDriverRegression(){
   var world=World();var state=world.Snapshot();state["enemies"]=new JArray();var map=(JObject)world.Definition.DeepClone();var input=Input(world,Vector2.right,aim:32768);var actions=new[]{"ability_primary","ability_secondary"};
   DcV8CoopPilot.FilterCombatActions(state,map,"1",input,ref actions);Require(actions.Contains("ability_primary"),"safe movement-direction volley was rejected because of opposite aim");
   map["rooms"][1]["obstacles"]=new JArray{new JArray(7,8,1,3)};actions=new[]{"ability_primary","ability_secondary"};DcV8CoopPilot.FilterCombatActions(state,map,"1",input,ref actions);Require(actions.SequenceEqual(new[]{"ability_secondary"}),"volley crossed an intervening wall or swallowed another skill");
   state["players"][0]["dodge_duration_remaining_ms"]=100;actions=new[]{"ability_primary"};DcV8CoopPilot.FilterCombatActions(state,map,"1",input,ref actions);Require(actions.Contains("ability_primary"),"existing dash lost the legal arrows-only volley");
   state["players"][0]["dodge_duration_remaining_ms"]=0;map["rooms"][1]["obstacles"]=new JArray();actions=new[]{"ability_primary","dodge"};DcV8CoopPilot.FilterCombatActions(state,map,"1",input,ref actions);Require(actions.SequenceEqual(new[]{"dodge"}),"forward primary displaced the defensive dodge input");
  }
  private static void DefenseContourRegression(){
   var world=World();var state=world.Snapshot();var own=state["players"][0];var body=DcV7Geometry.Profile(parameters,"ranger",(string)own["body_facing"]);state["projectiles"]=new JArray(new JObject{["id"]="projectile_008899",["room_id"]=1,["enemy"]=true,["x"]=(float)own["x"]+2,["y"]=(float)own["y"]+body.HullMax.y-.08f,["direction_x"]=-1,["direction_y"]=0,["speed"]=6,["radius"]=.1f});
   Require(!DcV5Pilot.DefenseThreat(state,own),"fixture does not isolate the old foot-circle blind spot");Require(DcV8CoopPilot.ShouldUseDefense(state,"1",world.Definition),"head-contour projectile crossing did not request protection");
  }
  private static void ShortCombatHorizonRegression(){
   var world=World();Change(world,s=>{s["players"][0]["Position"]=Point(3,1.5f);s["enemies"][0]["Position"]=Point(4,1.5f);});var snapshot=world.Snapshot();var map=(JObject)world.Definition.DeepClone();map["rooms"][1]["width"]=6;map["rooms"][1]["height"]=5;var input=Input(world,held:true);var actions=Array.Empty<string>();
   DcV7Pilot.Adapt(snapshot,map,"1",ref input,ref actions,false);Require(input.Move.sqrMagnitude>.01f,"short legal combat steps were rejected with the long prediction horizon");
  }
  private static void DefenseTerrainRegression(){
   var state=CoopDefenseSnapshot();var first=state["players"][0];var second=state["players"][1];second["x"]=(float)first["x"]+2;second["body_center_x"]=(float)first["body_center_x"]+2;
   state["projectiles"][0]["x"]=(float)second["x"]+1.6f;var map=(JObject)World().Definition.DeepClone();map["rooms"][1]["obstacles"]=new JArray{new JArray(5.5f,8,1,3)};
   Require(DcV5Pilot.Skills(state,"2",map).Contains("ability_secondary"),"a defender beyond a wall incorrectly suppressed local protection");
   state["fields"]=new JArray(new JObject{["room_id"]=1,["kind"]="wind_bind",["x"]=first["body_center_x"],["y"]=first["body_center_y"]});
   Require(DcV5Pilot.Skills(state,"2",map).Contains("ability_secondary"),"a field beyond a wall was counted as coverage");
  }

  private static void ActorDeltaRegression(){
   var world=World("runesmith");var original=world.Snapshot();var state=new DcV8ActorDelta();
   var first=state.Encode(original,7);Require(first["players"].Count()==1&&first["enemies"].Count()==1&&first["rooms"]!=null,"new baseline omitted actor state");
   var stable=state.Encode(original,7);Require(stable["players"].Count()==0&&stable["enemies"].Count()==0&&stable["rooms"]==null,"unchanged properties flooded reliable queue");
   var changed=(JObject)original.DeepClone();changed["players"][0]["secondary_remaining_ms"]=11000;changed["enemies"][0]["warning"]=true;changed["players"][0]["x"]=8;
   var update=state.Encode(changed,7);Require(update["players"][0]["player_id"]!=null&&(long)update["players"][0]["secondary_remaining_ms"]==11000&&update["players"][0]["x"]==null&&(bool)update["enemies"][0]["warning"],"changed actor state or dynamic ownership lost");
   Require(System.Text.Encoding.UTF8.GetByteCount(update.ToString(Newtonsoft.Json.Formatting.None))*3<System.Text.Encoding.UTF8.GetByteCount(original.ToString(Newtonsoft.Json.Formatting.None)),"delta did not reduce actual state bytes");
   var game=new GameObject("isolated_actor_delta_receiver");game.SetActive(false);var runtime=game.AddComponent<DcP0V3>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(DcP0V3).GetField(name,flags).SetValue(runtime,value);
   try{
    var baseline=(JObject)original.DeepClone();baseline["protocol_version"]=8;baseline["interest_revision"]=1;baseline["lifecycle_watermark"]=0;DcS1Replica.PackV6Baseline(baseline,true);
    var replica=(DcS1Replica)typeof(DcP0V3).GetField("s1Replica",flags).GetValue(runtime);replica.Baseline(baseline,"1",7);
    var controls=new DcP1Controls(true,true);controls.WeaponState((string)baseline["players"][0]["active_weapon_id"],(uint)baseline["players"][0]["weapon_revision"],false);
    Set("p1Controls",controls);Set("p1Snapshot",replica.Snapshot);Set("runID","isolated");Set("sourceGeneration",1L);Set("connectionGeneration",2L);Set("v5Runtime",true);
    var frame=100L;foreach(var incomingState in new[]{first,stable,update}){
     var value=new JObject{["protocol_version"]=8,["run_id"]="isolated",["source_generation"]=1,["connection_generation"]=2,["state_revision"]=7,["frame"]=frame++,["state"]=incomingState};var bytes=System.Text.Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None));
     using var writer=new Unity.Netcode.FastBufferWriter(bytes.Length+4,Unity.Collections.Allocator.Temp);writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);using var reader=new Unity.Netcode.FastBufferReader(writer,Unity.Collections.Allocator.Temp);
     typeof(DcP0V3).GetMethod("V5ActorState",flags).Invoke(runtime,new object[]{Unity.Netcode.NetworkManager.ServerClientId,reader});
    }
    Require((long)replica.Snapshot["players"][0]["secondary_remaining_ms"]==11000&&(bool)replica.Snapshot["enemies"][0]["warning"],"real wire reader lost the delta");
    void Incoming(JObject incomingState,int revision,long at,long generation=1){
     var value=new JObject{["protocol_version"]=8,["run_id"]="isolated",["source_generation"]=generation,["connection_generation"]=2,["state_revision"]=revision,["frame"]=at,["state"]=incomingState};var bytes=System.Text.Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None));
     using var writer=new Unity.Netcode.FastBufferWriter(bytes.Length+4,Unity.Collections.Allocator.Temp);writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);using var reader=new Unity.Netcode.FastBufferReader(writer,Unity.Collections.Allocator.Temp);
     typeof(DcP0V3).GetMethod("V5ActorState",flags).Invoke(runtime,new object[]{Unity.Netcode.NetworkManager.ServerClientId,reader});
    }
    var future=(JObject)original.DeepClone();future["players"][0]["secondary_remaining_ms"]=7000;future["enemies"][0]["warning"]=true;
    Incoming(state.Encode(future,8),8,200);Incoming(state.Encode(future,8),8,201);
    var pending=(System.Collections.ICollection)typeof(DcP0V3).GetField("v8PendingActorStates",flags).GetValue(runtime);
    Require(pending.Count==2&&(long)replica.Snapshot["players"][0]["secondary_remaining_ms"]==11000,"future revision mutated the current baseline or was discarded");
    Incoming(state.Encode(future,8),8,202,9);Require(pending.Count==2,"foreign source generation entered the pending baseline queue");
    var next=(JObject)original.DeepClone();next["protocol_version"]=8;next["interest_revision"]=1;next["lifecycle_watermark"]=0;next["world_frame_id"]=150;DcS1Replica.PackV6Baseline(next,true);replica.Baseline(next,"1",8);Set("p1Snapshot",replica.Snapshot);Set("v5ActorFrame",150L);
    typeof(DcP0V3).GetMethod("FlushV8ActorStates",flags).Invoke(runtime,null);
    Require(pending.Count==0&&(long)replica.Snapshot["players"][0]["secondary_remaining_ms"]==7000&&(bool)replica.Snapshot["enemies"][0]["warning"],"actor delta preceding its chunked baseline was lost after a stable delta");
    for(var i=0;i<33;i++)Incoming(stable,9,300+i);
    Require(pending.Count==0&&replica.V6NeedsBaseline,"pending actor overflow did not stay bounded and request a fresh baseline");
   }finally{UnityEngine.Object.DestroyImmediate(game);}
   var reset=state.Encode(changed,9);Require(reset["players"].Count()==1&&reset["enemies"].Count()==1&&reset["rooms"]!=null,"new revision inherited an old delta cache");
  }
  private static void RecoveryCriticalVisualRegression(){
   var world=World("runesmith");var state=world.Snapshot();state["elapsed_ms"]=100;state["players"][0]["last_slam"]=new JObject{["root"]="missed-slam",["room_id"]=1,["at_ms"]=50,["x"]=5,["y"]=9,["radius"]=2.875f};
   var game=new GameObject("isolated_recovery_critical_visual");game.SetActive(false);var view=game.AddComponent<DcP1View>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(DcP1View).GetField(name,flags).SetValue(view,value);
   var visual=typeof(DcP1View).GetNestedType("V8CriticalVisual",System.Reflection.BindingFlags.NonPublic);var meshes=new List<Mesh>();var roots=new List<GameObject>();
   object Item(string pool){var value=Activator.CreateInstance(visual,true);var root=new GameObject(pool);root.transform.SetParent(game.transform);roots.Add(root);var mesh=new Mesh();meshes.Add(mesh);visual.GetField("Root").SetValue(value,root);visual.GetField("Mesh").SetValue(value,mesh);visual.GetField("Seen").SetValue(value,"earlier-slam");((System.Collections.IDictionary)typeof(DcP1View).GetField(pool,flags).GetValue(view)).Add("1",value);return value;}
   try{
    Set("snapshot",state);Set("geometry",world.Definition);Set("ownID","1");Set("room",1);Set("v8VisualRoom",1);var wave=Item("v8Waves");var spin=Item("v8Spins");var draw=typeof(DcP1View).GetMethod("DrawV8Critical",flags);
    view.RestoreV8Presentation();draw.Invoke(view,null);
    Require((string)visual.GetField("Seen").GetValue(wave)=="missed-slam"&&(float)visual.GetField("Until").GetValue(wave)==0&&!roots[0].activeSelf,"same-room recovery replayed a completed slam");
    state["players"][0]["last_slam"]["root"]="new-slam";draw.Invoke(view,null);Require(roots[0].activeSelf,"a new live slam was suppressed after recovery");
    state["players"][0]["death_hammer"]=new JObject{["active"]=true,["started_ms"]=0,["ends_ms"]=2000};view.RestoreV8Presentation();draw.Invoke(view,null);Require(roots[1].activeSelf&&(bool)state["players"][0]["death_hammer"]["active"],"recovery discarded an active authoritative spin");
   }finally{
    ((System.Collections.IDictionary)typeof(DcP1View).GetField("v8Waves",flags).GetValue(view)).Clear();((System.Collections.IDictionary)typeof(DcP1View).GetField("v8Spins",flags).GetValue(view)).Clear();foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(game);
   }
  }
  private static void MenuCancellationRegression(){
   foreach(var scenario in new[]{"multiplayer","trial","solo_missing_control","solo_closed_control","solo_receive_error","solo_pause_rejected","solo_quick_close_rejected","solo_quick_close_receive"}){
    var world=World("runesmith",true);Require(Cast(world,"charge_begin"),"charge setup");Step(world,1);Require((bool)Own(world)["charging"],"zero-held input alone unexpectedly canceled the charge");
    var game=new GameObject("isolated_menu_cancel_"+scenario);game.SetActive(false);var runtime=game.AddComponent<DcP0V3>();var view=game.AddComponent<DcP1View>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    void Set(string name,object value)=>typeof(DcP0V3).GetField(name,flags).SetValue(runtime,value);
    System.Net.WebSockets.ClientWebSocket socket=null;
    try{
     var label=new GameObject("menu_status",typeof(RectTransform),typeof(UnityEngine.UI.Text));label.transform.SetParent(game.transform);typeof(DcP1View).GetField("result",flags).SetValue(view,label.GetComponent<UnityEngine.UI.Text>());
     var controls=new DcP1Controls(true,true);controls.WeaponState("rune_hammer",1,true);controls.FreezeMenu=true;controls.SetMenu(true);
     Set("p1Controls",controls);Set("p1View",view);Set("p1Snapshot",world.Snapshot());Set("weaponStage",true);Set("loadedConfirmed",true);Set("s1ClientBaselineReady",true);Set("sourceGeneration",1L);Set("connectionGeneration",2L);Set("stopping",new System.Threading.CancellationTokenSource());
     Set("assignment",new JObject{["mode"]=scenario=="trial"?"trial":"normal",["members"]=scenario=="multiplayer"?new JArray(new JObject(),new JObject()):new JArray(new JObject())});
     if(scenario=="solo_closed_control"||scenario=="solo_receive_error"||scenario=="solo_quick_close_receive"){socket=new System.Net.WebSockets.ClientWebSocket();socket.Abort();Set("s1Control",socket);}
     if(scenario.StartsWith("solo_quick_close",StringComparison.Ordinal)){typeof(DcP0V3).GetMethod("BeginS1PauseRequest",flags).Invoke(runtime,new object[]{true});controls.SetMenu(false);}
     if(scenario=="solo_receive_error"||scenario=="solo_quick_close_receive"){Set("s1PausePending",true);typeof(DcP0V3).GetMethod("S1ControlUnavailable",flags).Invoke(runtime,new object[]{socket});}
     else if(scenario=="solo_pause_rejected"||scenario=="solo_quick_close_rejected"){Set("s1PausePending",true);typeof(DcP0V3).GetMethod("S1PauseRejected",flags).Invoke(runtime,new object[]{socket});}
     else ((System.Threading.Tasks.Task)typeof(DcP0V3).GetMethod("S1PauseRequest",flags).Invoke(runtime,new object[]{true,"focus_loss"})).GetAwaiter().GetResult();
     Require(!(bool)typeof(DcP0V3).GetField("s1PausePending",flags).GetValue(runtime),"unavailable pause stranded the battle input path");
     controls.SetMenu(false);var drain=typeof(DcP0V3).GetMethod("S1MenuActions",flags);var actions=(string[])drain.Invoke(runtime,new object[]{new[]{"attack_press"}});
     Require(actions.SequenceEqual(new[]{"cancel_input","attack_press"}),"menu cancellation was lost on quick close or followed a fresh action: "+scenario);
     uint sequence=0;foreach(var kind in actions){var intent=Input(world);intent.ActionSequence=++sequence;world.QueueAction("1",kind,intent,"menu_"+kind);}Step(world,1);
     Require(!(bool)Own(world)["charging"]&&(long)Own(world)["heavy_attacks"]==0&&Own(world)["last_slam"].Type==JTokenType.Null&&(long)Own(world)["ability_remaining_ms"]==0&&(long)Own(world)["secondary_remaining_ms"]==0,"menu cancel emitted a slam or consumed skill cooldown");
     Require(((string[])drain.Invoke(runtime,new object[]{Array.Empty<string>()})).Length==0,"menu cancellation repeated after acknowledgement path consumption");
     using(var oldControl=new System.Net.WebSockets.ClientWebSocket()){typeof(DcP0V3).GetMethod("S1PauseRejected",flags).Invoke(runtime,new object[]{oldControl});typeof(DcP0V3).GetMethod("S1ControlUnavailable",flags).Invoke(runtime,new object[]{oldControl});Require(((string[])drain.Invoke(runtime,new object[]{Array.Empty<string>()})).Length==0,"an old control callback canceled the current connection");}
     ((System.Threading.Tasks.Task)typeof(DcP0V3).GetMethod("S1PauseRequest",flags).Invoke(runtime,new object[]{true,"menu"})).GetAwaiter().GetResult();Set("connectionGeneration",3L);
     Require(((string[])drain.Invoke(runtime,new object[]{Array.Empty<string>()})).Length==0,"old menu canceled a replacement connection's action");
     ((System.Threading.Tasks.Task)typeof(DcP0V3).GetMethod("S1PauseRequest",flags).Invoke(runtime,new object[]{true,"menu"})).GetAwaiter().GetResult();Set("sourceGeneration",2L);
     Require(((string[])drain.Invoke(runtime,new object[]{Array.Empty<string>()})).Length==0,"old menu canceled a replacement authority's action");
     if(scenario=="solo_missing_control"){
      controls.SetMenu(true);typeof(DcP0V3).GetMethod("BeginS1PauseRequest",flags).Invoke(runtime,new object[]{true});var confirm=typeof(DcP0V3).GetMethod("ConfirmS1Pause",flags);var pending=typeof(DcP0V3).GetField("s1MenuCancelPending",flags);
      confirm.Invoke(runtime,new object[]{true,1L,"old-pause"});Require((bool)pending.GetValue(runtime),"stale pause clock discarded the pending cancellation");
      confirm.Invoke(runtime,new object[]{true,2L,null});Require((bool)pending.GetValue(runtime),"pause without an authoritative identity discarded cancellation");
      confirm.Invoke(runtime,new object[]{false,2L,null});Require((bool)pending.GetValue(runtime),"resume clock discarded a pending pause cancellation");
      confirm.Invoke(runtime,new object[]{true,2L,Guid.NewGuid().ToString()});Require(!(bool)pending.GetValue(runtime),"matching authoritative pause did not preserve frozen input state");
      typeof(DcP0V3).GetMethod("S1PauseRejected",flags).Invoke(runtime,new object[]{socket});Require(!(bool)pending.GetValue(runtime),"control error recreated cancellation in a confirmed paused world");
      confirm.Invoke(runtime,new object[]{false,3L,null});Require(((string[])drain.Invoke(runtime,new object[]{Array.Empty<string>()})).Length==0,"successful pause/resume sent an extra cancel into preserved gameplay state");
     }
    }finally{socket?.Dispose();UnityEngine.Object.DestroyImmediate(game);}
   }
  }
  private static void DelayedSwitchReceiptRegression(){
   var game=new GameObject("isolated_delayed_switch_receipt");game.SetActive(false);var runtime=game.AddComponent<DcP0V3>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(DcP0V3).GetField(name,flags).SetValue(runtime,value);
   try{
    Set("p1View",game.AddComponent<DcP1View>());var controls=new DcP1Controls(true,true);controls.WeaponState("rune_hammer",4,true);Set("p1Controls",controls);Set("runID","isolated");Set("sourceGeneration",1L);Set("connectionGeneration",2L);Set("s2SwitchPending",true);Set("s2SwitchActionID","pending-switch");
    var receipt=new JObject{["run_id"]="isolated",["source_generation"]=1,["connection_generation"]=2,["action_id"]="unrelated",["weapon_id"]="rune_bolt",["weapon_revision"]=3};
    var method=typeof(DcP0V3).GetMethod("S2Receipt",flags);method.Invoke(runtime,new object[]{receipt});Require((bool)typeof(DcP0V3).GetField("s2SwitchPending",flags).GetValue(runtime),"unrelated receipt unlocked input");
    receipt["action_id"]="pending-switch";method.Invoke(runtime,new object[]{receipt});Require(!(bool)typeof(DcP0V3).GetField("s2SwitchPending",flags).GetValue(runtime)&&controls.WeaponRevision==4&&controls.ActiveWeapon=="rune_hammer","delayed matching receipt locked input or regressed state");
   }finally{UnityEngine.Object.DestroyImmediate(game);}
  }
  private static void ConnectionRegression(DcP1World world,bool duplicate){
   var game=new GameObject("isolated_connection_readiness_regression");var runtime=game.AddComponent<DcP0V3>();
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(DcP0V3).GetField(name,flags).SetValue(runtime,value);
   try{
    const ulong peer=91;var identity=new JObject{["player_id"]="1",["connection_generation"]=2};
    ((Dictionary<ulong,JObject>)typeof(DcP0V3).GetField("identities",flags).GetValue(runtime)).Add(peer,identity);
    Set("assignment",new JObject{["run_id"]="isolated",["config_hash"]="frozen"});Set("configHash","frozen");Set("role","server");Set("p1World",world);Set("p2Stage",true);Set("syncFoundation",true);Set("v5Runtime",true);Set("running",true);
    if(!duplicate){var snapshot=world.RuntimeSnapshot();snapshot["state_revision"]=1;Set("sourceGeneration",1L);((Dictionary<ulong,DcV6LifecycleStream>)typeof(DcP0V3).GetField("v6Streams",flags).GetValue(runtime)).Add(peer,new DcV6LifecycleStream());typeof(DcP0V3).GetMethod("SendS1Baseline",flags).Invoke(runtime,new object[]{peer,snapshot,identity});Require(((DcP2BaselinePump)typeof(DcP0V3).GetField("p2BaselinePump",flags).GetValue(runtime)).MaximumPendingBytes==0,"unloaded peer queued a baseline");return;}
    void Loaded(){var value=new JObject{["protocol_version"]=8,["run_id"]="isolated",["config_hash"]="frozen",["connection_generation"]=2};var bytes=System.Text.Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None));using var writer=new Unity.Netcode.FastBufferWriter(bytes.Length+4,Unity.Collections.Allocator.Temp);writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);using var reader=new Unity.Netcode.FastBufferReader(writer,Unity.Collections.Allocator.Temp);typeof(DcP0V3).GetMethod("Loaded",flags).Invoke(runtime,new object[]{peer,reader});}
    Loaded();var input=Input(world);input.ActionSequence=1;world.QueueAction("1","cancel_input",input,Guid.NewGuid().ToString());Step(world,1);Require(world.WeaponActionSequence("1")==1,"setup action sequence missing");Loaded();Require(world.WeaponActionSequence("1")==1,"duplicate Loaded reset live action sequence");
   }finally{UnityEngine.Object.DestroyImmediate(game);}
  }
  private static void RingOcclusionRegression(){
   var layout=new JObject{["width"]=34,["height"]=20,["obstacles"]=new JArray(new JArray(8,7,1,4),new JArray(13,4,2,2))};
   var covers=new JArray(new JObject{["room_id"]=1,["health"]=30,["x"]=6,["y"]=8},new JObject{["room_id"]=2,["health"]=30,["x"]=5,["y"]=8});var prepared=new DcV8RingOcclusion();prepared.Prepare(layout,covers,1);
   foreach(var point in new[]{new Vector2(5,8),new Vector2(1.01f,1.01f),new Vector2(7.8f,9),new Vector2(12,5)})foreach(var radius in new[]{.01f,.5f,1.2f,2.875f})for(var i=0;i<=64;i++){
    var direction=new Vector2(Mathf.Cos(i*Mathf.PI*2/64),Mathf.Sin(i*Mathf.PI*2/64));var end=point+direction*radius;var t=DcV6Geometry.Terrain(layout,point,end,0)?.Time??1;
    foreach(var cover in covers.Where(c=>(int)c["room_id"]==1&&(int)c["health"]>0)){var hit=DcV6Geometry.Box(point,end,(float)cover["x"]-.4375f,(float)cover["y"]-.4375f,.875f,.875f,0);if(hit.HasValue)t=Math.Min(t,hit.Value.Time);}
    Require(Math.Abs(prepared.VisibleRadius(point,direction,radius)-radius*Math.Max(0,t-.001f))<.000001f,"prepared ring clipping differs from original contact geometry");
   }
   var revision=prepared.Revision;prepared.Prepare(layout,covers,1);Require(prepared.Revision==revision,"unchanged geometry invalidated clipping");
   covers[0]["health"]=15;prepared.Prepare(layout,covers,1);Require(prepared.Revision==revision,"still-solid cover changed clipping identity");
   var before=prepared.VisibleRadius(new Vector2(5,8),Vector2.right,2);covers[0]["health"]=0;prepared.Prepare(layout,covers,1);Require(prepared.Revision!=revision&&prepared.VisibleRadius(new Vector2(5,8),Vector2.right,2)>before,"cover destruction retained stale clipping");
   revision=prepared.Revision;prepared.Prepare(layout,covers,2);Require(prepared.Revision!=revision,"room change retained old clipping identity");
  }
  public static void RunReviewChecks(){
   parameters=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/parameters-v8-p5.json"));geometry=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/world-v8-p5.json"));configuration=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/demo-v8-p5.json"));passed.Clear();
   Case("future actor deltas survive matching chunked baseline with bounded recovery",ActorDeltaRegression);
   Case("same-room recovery skips historical slam waves and resumes live spin",RecoveryCriticalVisualRegression);
   Case("menu and focus cancellation survive missing control and quick close",MenuCancellationRegression);
   Case("matching delayed switch receipt releases input without regressing newer state",DelayedSwitchReceiptRegression);
   File.WriteAllText(DcBootstrap.DemoEvidenceRoot+"/review-v8-client-checks.json",new JObject{["profile"]="dc_ruins.v8.p5.1",["status"]="PASS",["scope"]="focused_client_recovery_models",["modified_world_state"]=true,["real_udp"]=false,["human"]=false,["full_native_suite"]=false,["count"]=passed.Count,["cases"]=new JArray(passed)}.ToString());
  }
  public static void Run(){
   parameters=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/parameters-v8-p5.json"));geometry=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/world-v8-p5.json"));configuration=JObject.Parse(File.ReadAllText("../../docs/game-demo/config/demo-v8-p5.json"));passed.Clear();Case("automatic switch waiting preserves a fresh reliable ordinary press",PendingSwitchPressEdgeRegression);Case("ordinary probe accepts actual reliable short-lived own births but not skills or foreign shots",OrdinaryLifecycleEvidenceRegression);Case("cached projectile metadata matches full snapshots across motion pooling and restore",ProjectileSnapshotBindingRegression);Case("reliable delayed spawn receipts align later event life without changing projectile origin",DelayedSpawnFactFrontierRegression);Case("autoplay uses current short-hammer reach instead of old 2.4-tile cover threshold",ShortHammerCoverDriverRegression);Case("autoplay protected rescue approaches and existing helper remain eligible",SupportedRescueApproachRegression);Case("autoplay squad follows legal routes into shared defensive coverage",SquadCoverageRouteRegression);Case("autoplay short volley checks movement path without changing existing dash",VolleyDriverRegression);Case("autoplay defense sees projectile crossings at the actual head contour",DefenseContourRegression);Case("autoplay can use short legal combat steps in a tight pocket",ShortCombatHorizonRegression);Case("genuine sealed terrain retains the five-second and two-recovery limit",GenuineBlockedEnemyRegression);Case("recorded east-side ordinary enemies retain a legal movement route",RecordedMeleeNavigationRegression);Case("cooperative defense election respects intervening terrain",DefenseTerrainRegression);Case("authoritative wall-stop hull remains legal for turning and rescue",WallLegalityRegression);Case("rescue driver casts protection before held interaction without flooding",RescueDefenseRegression);Case("cooperative driver staggers overlapping available defensive fields",CoopDefenseRegression);Case("recorded static corner retains a legal combat direction",CombatCornerRegression);Case("idle ranged enemies do not age movement recovery while players are elsewhere",IdleEnemyWatchdogRegression);Case("autoplay trap clock preserves warnings and opens only safe cooldown crossings",TrapNavigationClockRegression);Case("last enemy behind dense terrain has a reachable firing approach",LastEnemyNavigationRegression);Case("ordinary held combat requires a fresh reliable press across reconnect",ProbeAttackEpisodeRegression);Case("revision-bound actor deltas preserve changes without repeating reliable state",ActorDeltaRegression);Case("same-room recovery skips historical slam waves and resumes live spin",RecoveryCriticalVisualRegression);Case("menu and focus cancellation survive missing control and quick close",MenuCancellationRegression);Case("matching delayed switch receipt releases input without regressing newer state",DelayedSwitchReceiptRegression);BroadphaseRegressions();Case("stale switch target is rejected by weapon revision without transport failure",StaleSwitchRegression);Case("autoplay dodge waits for readiness round trip instead of flooding actions",PilotDodgeRegression);
   Case("departed lobby assignment cannot launch a new combat component",()=>{
    var game=new GameObject("departed lobby assignment regression");game.SetActive(false);try{var lobby=game.AddComponent<DcP5Lobby>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var departed=(System.Collections.Generic.Queue<string>)typeof(DcP5Lobby).GetField("departedRuns",flags).GetValue(lobby);departed.Enqueue("departed_run");typeof(DcP5Lobby).GetMethod("Notification",flags).Invoke(lobby,new object[]{new JObject{["type"]="v2.match.assigned",["data"]=new JObject{["run_id"]="departed_run"}}});Require(!(bool)typeof(DcP5Lobby).GetField("playing",flags).GetValue(lobby),"late departed notification restarted the lobby join path");}finally{UnityEngine.Object.DestroyImmediate(game);}
   });
   Case("return handoff cancels old combat before lobby control reconnects",()=>{
    var game=new GameObject("old combat handoff regression");game.SetActive(false);try{var combat=game.AddComponent<DcP0V3>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(DcP0V3).GetField("stopping",flags).SetValue(combat,new System.Threading.CancellationTokenSource());typeof(DcP0V3).GetField("loadedConfirmed",flags).SetValue(combat,true);var method=typeof(DcP0V3).GetMethod("PrepareP5Return",flags);Require(method!=null,"old combat has no quiesce boundary before control takeover");method.Invoke(combat,null);var stop=(System.Threading.CancellationTokenSource)typeof(DcP0V3).GetField("stopping",flags).GetValue(combat);Require(stop.IsCancellationRequested&&!(bool)typeof(DcP0V3).GetField("loadedConfirmed",flags).GetValue(combat),"old join and reconnect can outlive control takeover");method.Invoke(combat,null);Require(stop.IsCancellationRequested,"handoff retry restarted old combat");}finally{UnityEngine.Object.DestroyImmediate(game);}
   });
   Case("reserved hammer ring clipping preserves exact rays and invalidates on terrain changes",RingOcclusionRegression);
   Case("duplicate Loaded is idempotent after accepted actions",()=>ConnectionRegression(World("ranger"),true));
   Case("reconnected peer cannot receive state before Loaded",()=>ConnectionRegression(World("ranger"),false));
   Case("v8 authority pressure uses current flight lifetime and staged skills",()=>{var w=DcP5PressureFixture.Create(parameters,geometry,configuration,"runesmith",out var state);Require(state["projectiles"].Count()==1024&&state["projectiles"].All(p=>(int)p["ProtocolVersion"]==8&&(long)p["ExpiresGameMS"]==6000&&(int)p["Bounces"]==int.MaxValue),"inherited v6/v7 ballistic fixture");Require(state["turrets"].Count()==8&&state["players"].All(p=>(int)p["TurretCharges"]==0&&(int)p["SpinDamage"]==18&&p["Upgrades"].Count()==3),"staged/skill capacity fields missing");w.Restore(state);Require(w.RuntimeSnapshot()["players"].All(p=>(bool)p["death_hammer"]["active"]&&(int)p["turret_charges"]==0),"fixture restore lost v8 skills");});
   Case("render pressure follows independent tower lifetimes and cooldown stages",()=>{var snapshot=World("runesmith").Snapshot();var template=(JObject)snapshot["players"][0].DeepClone();snapshot["players"]=new JArray(Enumerable.Range(0,4).Select(i=>{var p=(JObject)template.DeepClone();p["player_id"]=(i+1).ToString();p["slot"]=i;return p;}));DcV8PressureFixture.Render(snapshot,parameters,0,"runesmith_twin");Require(snapshot["turrets"].Count()==4&&snapshot["players"].All(p=>(int)p["turret_charges"]==1),"first press must be one each");DcV8PressureFixture.Render(snapshot,parameters,2500,"runesmith_twin");Require(snapshot["turrets"].Count()==8&&snapshot["players"].All(p=>(int)p["turret_charges"]==0),"second deployment peak missing");DcV8PressureFixture.Render(snapshot,parameters,8500,"runesmith_twin");Require(snapshot["turrets"].Count()==4,"first towers did not expire independently");DcV8PressureFixture.Render(snapshot,parameters,10500,"runesmith_twin");Require(snapshot["turrets"].Count()==0,"second tower expiry missing");});
   Case("Go compact and Unity expansion retain identical frozen world",()=>{var manifest=JObject.Parse(File.ReadAllText((DcBootstrap.DemoEvidenceRoot+"/v8-go-manifest-model.json")));Require((string)manifest["profile"]=="dc_ruins.v8.p5.1","model identity");var compact=DcV6Profile.Compact(manifest);DcV6Profile.Expand(compact,Path.GetFullPath("../../docs/game-demo/config"));if(!JToken.DeepEquals(manifest["p1_world"],compact["p1_world"])){File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/v8-world-before-expansion.json"),manifest["p1_world"].ToString());File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/v8-world-after-expansion.json"),compact["p1_world"].ToString());throw new InvalidOperationException("v8 compact expansion changed world; exact two models retained");}var method=typeof(DcP0V3).GetMethod("Canonical",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var text=(string)method.Invoke(null,new object[]{compact["p1_world"]});using var hash=System.Security.Cryptography.SHA256.Create();var actual=string.Concat(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));Require(actual==(string)manifest["p1_world_hash"],"cross-language world fingerprint "+actual+" vs "+manifest["p1_world_hash"]);});
   Case("v8 protocol retains prior world systems",()=>{var w=World();Require(w.V8&&w.V7&&w.V6&&w.P3&&w.P4,"inheritance");Require((int)w.Snapshot()["schema_version"]==8,"schema");});
   Case("ten growth entries replace shield with battle rune",()=>{Require(((JArray)parameters["growth"]).Count==10,"count");Require(parameters["growth"].Any(x=>(string)x["id"]=="battle_rune")&&!parameters["growth"].Any(x=>(string)x["id"]=="ward"),"replacement");});
   Case("hammer reach and landing are 1.2 with original wave",()=>{var m=parameters["loadouts"][1]["melee"];Require((int)m["range_milli_tiles"]==1200&&(int)m["charge"]["landing_distance_milli_tiles"]==1200&&(int)m["charge"]["wave_radius_milli_tiles"]==2875,"dimensions");});
   Case("real alpha head hull replaces old circular double hit",()=>{var hull=(JArray)parameters["loadouts"][1]["melee"]["charge"]["head_contact_vertices_milli"];Require(hull.Count>=4&&hull.All(v=>(int)v[0]<=0&&(int)v[0]>=-600&&Math.Abs((int)v[1])<=300),"head hull");});
   Case("first twin placement retains one without cooldown",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Require(Cast(w,"ability_primary"),"first");Require((int)Own(w)["turret_charges"]==1&&(long)Own(w)["ability_remaining_ms"]==0&&w.Snapshot()["turrets"].Count()==1,"stage");});
   Case("immediate second twin is separate and nonoverlapping",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Require(Cast(w,"ability_primary")&&Cast(w,"ability_primary"),"both accepted "+w.LastActionReason);var t=(JArray)w.Snapshot()["turrets"];Require(t.Count==2&&Vector2.Distance(new Vector2((float)t[0]["x"],(float)t[0]["y"]),new Vector2((float)t[1]["x"],(float)t[1]["y"]))>=.599f,"overlap");Require((int)Own(w)["turret_charges"]==0&&(long)Own(w)["ability_remaining_ms"]==12000,"cooldown");});
   Case("twin action retry is idempotent",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");var i=Input(w);w.QueueAction("1","ability_primary",i,"retry-one");w.QueueAction("1","ability_primary",i,"retry-one");Step(w,1);Require(w.Snapshot()["turrets"].Count()==1&&(int)Own(w)["turret_charges"]==1,"retry consumed charge");});
   Case("first tower expiry does not refill pending deployment",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Cast(w,"ability_primary");Step(w,250);Require(w.Snapshot()["turrets"].Count()==0&&(int)Own(w)["turret_charges"]==1,"expiry refunded");Require(Cast(w,"ability_primary")&&(long)Own(w)["ability_remaining_ms"]==12000,"delayed second");});
   Case("twin charge survives capture restore and room change",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Cast(w,"ability_primary");var group=(string)Own(w)["turret_group"];Change(w,s=>s["players"][0]["Room"]=2);Require((int)Own(w)["turret_charges"]==1&&(string)Own(w)["turret_group"]==group,"restored resources");});
   Case("twin group triggers battle only at first deployment",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin","battle_rune");Cast(w,"ability_primary");var until=(long)Own(w)["battle_until_ms"];Step(w,12);Cast(w,"ability_primary");Require((long)Own(w)["battle_until_ms"]==until,"second proc");});
   Case("full twin cooldown restores two uses",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Cast(w,"ability_primary");Cast(w,"ability_primary");Step(w,361);Require((int)Own(w)["turret_charges"]==2&&Cast(w,"ability_primary"),"refill");});
   Case("single turret keeps original damage and cooldown",()=>{var w=World("runesmith");Require(Cast(w,"ability_primary"),"single");Require((long)Own(w)["ability_remaining_ms"]==12000&&(int)w.Capture()["turrets"][0]["BaseDamage"]==14,"single changed");});
   Case("death hammer produces exactly eight nonknockback pulses",()=>{var w=World("runesmith");Nearby(w);var old=w.Capture()["enemies"][0]["Position"].ToString();Require(Cast(w,"ability_secondary"),"spin");Step(w,60);Require(EnemyHP(w)==9872,"damage "+EnemyHP(w));Require((int)Own(w)["death_hammer"]["next_pulse"]==8&&w.Capture()["enemies"][0]["Position"].ToString()==old,"pulse count or push");});
   Case("battle applies to triggering spin once with final floor",()=>{var w=World("runesmith");Nearby(w);Upgrade(w,"battle_rune");Cast(w,"ability_secondary");Require((int)Own(w)["death_hammer"]["damage"]==18&&(long)Own(w)["battle_until_ms"]==3000,"buff snapshot");Step(w,60);Require(EnemyHP(w)==9856,"buff repeated or absent");});
   Case("spin blocks basic but permits switching and turret",()=>{var w=World("runesmith");Cast(w,"ability_secondary");Require(!Cast(w,"attack_press")&&w.LastActionReason=="skill_active","basic allowed");Require(Cast(w,"switch_weapon")&&Cast(w,"ability_primary"),"parallel utility");Require((bool)Own(w)["death_hammer"]["active"]&&(string)Own(w)["active_weapon_id"]=="rune_hammer","switch canceled spin");});
   Case("spin does not itself grant invulnerability",()=>{var w=World("runesmith");Cast(w,"ability_secondary");var p=w.Capture()["players"][0];Require((long)p["InvulnerableUntil"]==0&&(long)p["HurtUntil"]==0,"new invulnerability");Require(Cast(w,"dodge"),"space rejected");});
   Case("spin restore does not repeat a settled pulse",()=>{var w=World("runesmith");Nearby(w);Cast(w,"ability_secondary");Step(w,10);var hp=EnemyHP(w);Change(w,s=>{});Step(w,1);Require(EnemyHP(w)==hp,"restore pulse replay");});
   Case("spin exits range and returns without historical damage",()=>{var w=World("runesmith");Nearby(w);Cast(w,"ability_secondary");Step(w,1);Change(w,s=>s["enemies"][0]["Position"]=Point(20,9));Step(w,29);var hp=EnemyHP(w);Nearby(w);Step(w,1);Require(EnemyHP(w)>=hp-16,"reentry catchup");});
   Case("room crossing stops spin without returning cooldown",()=>{var w=World("runesmith");Cast(w,"ability_secondary");Change(w,s=>s["players"][0]["Room"]=2);Step(w,1);Require(!(bool)Own(w)["death_hammer"]["active"]&&(long)Own(w)["secondary_remaining_ms"]>15000,"room reset");});
   Case("death ends spin while pending twin use remains",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Cast(w,"ability_primary");Cast(w,"ability_secondary");Change(w,s=>{s["players"][0]["Health"]=0;s["players"][0]["Life"]="dead";});Step(w,1);Require(!(bool)Own(w)["death_hammer"]["active"]&&(int)Own(w)["turret_charges"]==1,"life resources");});
   Case("spin clears an enemy projectile before same-time body hit",()=>{var w=World("runesmith");Cast(w,"ability_secondary");Change(w,s=>{var b=Bullet(5,9.55f,10,true);s["projectiles"]=new JArray(b);});Step(w,1);Require((int)Own(w)["health"]==100&&!w.Snapshot()["projectiles"].Any(),"clear ordering");});
   Case("in-flight own bolts keep their original damage",()=>{var w=World("runesmith");Upgrade(w,"battle_rune");Require(Cast(w,"attack_press"),"shot");var damage=(int)w.Capture()["projectiles"][0]["Damage"];Cast(w,"ability_secondary");Require((int)w.Capture()["projectiles"][0]["Damage"]==damage,"retroactive buff");});
   Case("spin damage facts merge by cast root",()=>{var w=World("runesmith");Nearby(w);Cast(w,"ability_secondary");var count=0;for(var i=1;i<=60;i++){w.Input("1",Input(w));count+=w.Tick((i*1000+29)/30).Count(f=>(string)f["kind"]=="damage");w.DrainV6Lifecycle();}Require(count==1&&(long)Own(w)["damage"]==128,"contract facts="+count+" actual damage="+Own(w)["damage"]);});
   Case("cover break does not expose enemy in the breaking pulse",()=>{var w=World("runesmith");Nearby(w,7);Change(w,s=>s["covers"]=new JArray(new JObject{["ID"]="blocking_cover",["Room"]=1,["Position"]=Point(6.1f,9.55f),["Health"]=30}));Cast(w,"ability_secondary");Step(w,8);Require(EnemyHP(w)==10000,"pierced same-pulse cover");Step(w,8);Require(EnemyHP(w)<10000,"later open route blocked");});
   Case("terminal clears spin and turret resources",()=>{var w=World("runesmith");Cast(w,"ability_secondary");w.Leave("1");Require(!(bool)Own(w)["death_hammer"]["active"]&&!w.Snapshot()["turrets"].Any(),"exit cleanup");});
   Case("spin chain cannot refresh each pulse",()=>{var w=World("runesmith");Nearby(w);Upgrade(w,"runesmith_chain");Change(w,s=>{var e=(JObject)s["enemies"][0].DeepClone();e["ID"]="foe_2";e["Position"]=Point(7,9);e["TickStart"]=Point(7,9);((JArray)s["enemies"]).Add(e);});Cast(w,"ability_secondary");Step(w,60);Require((long)Own(w)["damage"]==267,"chain repeated: "+Own(w)["damage"]);});
   Case("spin cooldown rejects repeat without extending end",()=>{var w=World("runesmith");Cast(w,"ability_secondary");var ends=(long)Own(w)["death_hammer"]["ends_ms"];Require(!Cast(w,"ability_secondary")&&(long)Own(w)["death_hammer"]["ends_ms"]==ends,"repeat extended");});
   Case("technical recovery skips past pulses and keeps expiry",()=>{var w=World("runesmith");Nearby(w);Cast(w,"ability_secondary");Step(w,1);var hp=EnemyHP(w);w.TechnicalWait(true,w.GameTime);w.TechnicalWait(false,1100);Step(w,1);Require(EnemyHP(w)>=hp-16&&(long)Own(w)["death_hammer"]["ends_ms"]==2000,"wait catchup or extension");});
   Case("blocked placement preserves second charge and battle deadline",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin","battle_rune");Cast(w,"ability_primary");Change(w,s=>{var t=(JObject)s["turrets"][0].DeepClone();for(var i=1;i<8;i++){t=(JObject)t.DeepClone();t["ID"]="capacity_"+i;((JArray)s["turrets"]).Add(t);}});var until=(long)Own(w)["battle_until_ms"];Require(!Cast(w,"ability_primary")&&(int)Own(w)["turret_charges"]==1&&(long)Own(w)["ability_remaining_ms"]==0&&(long)Own(w)["battle_until_ms"]==until,"capacity consumes resource");});
   Case("full charge doubles only visible hammer contact",()=>{var w=World("runesmith",true);Nearby(w);Change(w,s=>{var e=(JObject)s["enemies"][0].DeepClone();e["ID"]="wave_only";e["Position"]=Point(7.8f,9);e["TickStart"]=Point(7.8f,9);((JArray)s["enemies"]).Add(e);});Require(Cast(w,"charge_begin"),"charge");w.AdvanceTime(900);var input=Input(w);input.PressActionID=(string)Own(w)["charge_episode_id"];Require(w.Action("1","charge_release",input),"release");Step(w,8);var e=w.Capture()["enemies"];Require((int)e[0]["Health"]==9872&&(int)e[1]["Health"]==9936,"direct must be exact2D "+e);Require(Own(w)["last_slam"] is JObject,"reliable impact descriptor");});
   Case("legacy ordinary FX exhaustion reproduction retained",()=>{var go=new GameObject("legacy ordinary fx pool reproduction");go.SetActive(false);var view=go.AddComponent<DcP1View>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var list=(System.Collections.IList)typeof(DcP1View).GetField("p5Fx",flags).GetValue(view);var type=typeof(DcP1View).GetNestedType("FxP5",System.Reflection.BindingFlags.NonPublic);for(var i=0;i<32;i++){var item=Activator.CreateInstance(type,true);type.GetField("Until").SetValue(item,1000f);list.Add(item);}typeof(DcP1View).GetMethod("P5AttackFx",flags).Invoke(view,new object[]{new JObject{["kind"]="heavy",["x"]=5,["y"]=5,["visual_radius"]=2.875}});Require(list.Count==32&&list.Cast<object>().All(x=>type.GetField("Renderer").GetValue(x)==null),"legacy pool reproduction changed");File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/v8-wave-defect-reproduction.json"),new JObject{["scope"]="legacy_view_pool_only_no_world",["legacy_occupied_slots"]=32,["heavy_renderer_allocated"]=false,["status"]="REPRODUCED_LEGACY_DROP_PATH",["not_claimed_as_only_live_root_cause"]=true}.ToString());UnityEngine.Object.DestroyImmediate(go);});
   Case("wind cannot clear a projectile behind intact cover",()=>{var w=World("runesmith");Cast(w,"ability_secondary");Change(w,s=>{s["covers"]=new JArray(new JObject{["ID"]="clear_occluder",["Room"]=1,["Position"]=Point(6.1f,9.55f),["Health"]=30});s["projectiles"]=new JArray(Bullet(7.1f,9.55f,10,true));});Step(w,1);Require(w.Snapshot()["projectiles"].Count()==1,"cleared through intact cover");Change(w,s=>s["covers"][0]["Health"]=0);Step(w,1);Require(w.Snapshot()["projectiles"].Count()==0,"open line did not clear");});
   Case("tower combines modifiers once and freezes each actual shot",()=>{var w=World("runesmith");Nearby(w,8);Upgrade(w,"runesmith_twin","runesmith_heavy","battle_rune");Cast(w,"ability_primary");Step(w,1);Require(w.Capture()["projectiles"].Any(x=>(int)x["Damage"]==15),"rounded intermediate multiplier");Step(w,94);var bolts=w.Capture()["projectiles"];Require(bolts.Any(x=>(long)x["BornGameMS"]>=3000&&(int)x["Damage"]==13)&&bolts.Any(x=>(long)x["BornGameMS"]<3000&&(int)x["Damage"]==15),"per-shot expiry or retroactive buff");});
   Case("early E buffer deploys one new charge rather than legacy simultaneous twins",()=>{var w=World("runesmith");Upgrade(w,"runesmith_twin");Cast(w,"ability_primary");Cast(w,"ability_primary");w.AdvanceTime(11950);Require(Cast(w,"ability_primary")&&w.LastActionReason=="buffered","early input was not buffered");Step(w,2);Require((int)Own(w)["turret_charges"]==1&&(long)Own(w)["ability_remaining_ms"]==0&&w.Snapshot()["turrets"].Count()==1,"buffer bypassed staged skill");});
   Case("protocol8 prediction keeps the complete body below the north wall",()=>{var w=World("runesmith");var state=w.Snapshot();state["protocol_version"]=8;state["state_revision"]=1;state["interest_revision"]=1;state["lifecycle_watermark"]=0;var baseline=(JObject)state.DeepClone();DcS1Replica.PackV6Baseline(baseline,true);var replica=new DcS1Replica();replica.Baseline(baseline,"1",1);var prediction=new DcS1Prediction(w.Definition);prediction.Correct(replica,"1");for(var i=0;i<120;i++)prediction.Move(new DcP1Input{Move=Vector2.up,Aim=0,Sequence=(uint)(i+1)},1f/60,false);var body=DcV7Geometry.Profile(parameters,"runesmith",prediction.BodyFacing);var layout=(JObject)w.Definition["rooms"][1];Require(prediction.Position.y+body.Vertices.Max(v=>v.y)<=(float)layout["height"]-1+.0001f&&prediction.Position.y<16,"prediction missed the actual body north extent "+prediction.Position+" facing="+prediction.BodyFacing);});
   var sample=World("runesmith");Upgrade(sample,"runesmith_twin","battle_rune");var examples=new JArray(Own(sample).DeepClone());Cast(sample,"ability_primary");examples.Add(Own(sample).DeepClone());Cast(sample,"ability_secondary");examples.Add(Own(sample).DeepClone());Step(sample,1);examples.Add(Own(sample).DeepClone());File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/v8-native-state-samples.json"),examples.ToString());
   File.WriteAllText((DcBootstrap.DemoEvidenceRoot+"/v8-model-checks.json"),new JObject{["profile"]="dc_ruins.v8.p5.1",["status"]="PASS",["scope"]="isolated_authority_models",["modified_world_state"]=true,["real_udp"]=false,["human"]=false,["count"]=passed.Count,["cases"]=new JArray(passed)}.ToString());
  }
 }
}
