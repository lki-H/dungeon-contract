using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract.Editor {
 public static partial class DcV7Checks {
  private static void ReviewFlightWindow(float fraction){
   var w=World("runesmith");
   var y=9f-.072f-.3f+.25f;
   Change(w,s=>{
    s["enemies"][0]["Position"]=Point(8,9.4f);
    s["enemies"][0]["TickStart"]=Point(8,9);
    s["projectiles"]=new JArray(Bullet(x:6,y:y));
   });
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var shots=(IList)typeof(DcP1World).GetField("projectiles",flags).GetValue(w);
   var args=new object[]{shots[0],new Vector2(8,y),fraction,32};
   typeof(DcP1World).GetMethod("StepV7Flight",flags).Invoke(w,args);
   var state=w.Capture();
   Require((int)state["enemies"][0]["Health"]==9984,"contact sampled motion outside its actual time window");
   Require(state["projectiles"][0]["Inside"].ToString().Contains("foe_1"),"entry state used a later target position");
  }
  private static void ReviewChecks(){
   Case("network door probe advances through delayed authoritative body feedback",()=>{
    var w=World();Change(w,s=>{s["players"][0]["Room"]=0;s["players"][0]["Position"]=Point(3,6);});var delayed=new System.Collections.Generic.Queue<JObject>();
    for(var tick=0;tick<240&&(int)Own(w)["room_id"]==0;tick++){delayed.Enqueue(Own(w));var own=delayed.Count>9?delayed.Dequeue():delayed.Peek();Step(w,1,DcV7Pilot.EastDoorMove((JObject)w.Definition["rooms"][0],own));}
    Require((int)Own(w)["room_id"]!=0,"delayed vertical-only steering never made a legal east-door crossing");
   });
   Case("lobby practice child filters credentials preserves parent and caps Unity workers",()=>{
    var names=new[]{"DC_PRACTICE_REVIEW_PASSWORD","DC_PRACTICE_REVIEW_SECRET","DC_PRACTICE_REVIEW_TOKEN","DC_PRACTICE_REVIEW_API_KEY","DC_ADMISSION_KEY"};var prior=names.ToDictionary(n=>n,n=>Environment.GetEnvironmentVariable(n));
    try{foreach(var n in names)Environment.SetEnvironmentVariable(n,"isolated-practice-test");var method=typeof(DcP5Lobby).GetMethod("PracticeStartInfo",BindingFlags.Static|BindingFlags.NonPublic);var info=(System.Diagnostics.ProcessStartInfo)method.Invoke(null,new object[]{"D:/isolated/DungeonContract.exe","ranger","v7"});
     Require(names.All(n=>info.EnvironmentVariables[n]==null),"practice child inherited credential variables");
     Require(names.All(n=>Environment.GetEnvironmentVariable(n)=="isolated-practice-test"),"building child environment mutated parent credentials");
     var words=info.Arguments.Split(' ');var at=Array.IndexOf(words,"-job-worker-count");Require(!info.UseShellExecute&&at>=0&&at+1<words.Length&&words[at+1]=="2","practice child missed its worker cap");
     var rejected=false;try{method.Invoke(null,new object[]{"D:/isolated/DungeonContract.exe","ranger -dc-user invalid","v7"});}catch(TargetInvocationException error){rejected=error.InnerException is ArgumentException;}Require(rejected,"practice child accepted a non-enumerated loadout");
    }finally{foreach(var n in names)Environment.SetEnvironmentVariable(n,prior[n]);}
   });
   Case("trap rescue exercise exits the mechanism gate with the actual body footprint",()=>{
    var w=World();var room=(JObject)w.Definition["rooms"][2];
    room["obstacles"]=new JArray(new JArray(18,7,1,2),new JArray(18,11,1,2),new JArray(19,7,7,1),new JArray(19,12,7,1),new JArray(25,8,1,4));room["trap_points"]=new JArray{new JArray(14.5f,13.75f)};
    Change(w,s=>{s["players"][0]["Room"]=2;s["players"][0]["Position"]=Point(19.3431149f,10.3947325f);s["players"][0]["BodyFacing"]="right";s["rooms"][2]["Status"]="cleared";s["rooms"][2]["Mechanism"]=true;});
    for(var tick=1;tick<=90;tick++){var snapshot=w.Snapshot();var input=Input(w);var handled=DcP3Pilot.Rescue(snapshot,w.Definition,"1",ref input,true);var actions=Array.Empty<string>();DcV7Pilot.Adapt(snapshot,w.Definition,"1",ref input,ref actions,handled);w.Input("1",input);w.Tick((tick*1000+29)/30);w.DrainV6Lifecycle();}
    Require((float)Own(w)["x"]<17.5f,"legacy trap-exercise navigation kept the full body at the inner gate wall");
   });
   Case("autoplay rescues from a safe body position outside the rectangular trap",()=>{
    var w=World();Change(w,s=>{s["players"][0]["Room"]=2;s["players"][0]["Position"]=Point(11.35f,9.4f);s["rooms"][2]["Status"]="cleared";s["rooms"][2]["Mechanism"]=true;});
    var snapshot=w.Snapshot();var target=(JObject)snapshot["players"][0].DeepClone();target["player_id"]="2";target["slot"]=1;target["health"]=0;target["life_state"]="downed";target["x"]=10;target["y"]=9.4f;target["body_center_x"]=(float)target["body_center_x"]-1.35f;((JArray)snapshot["players"]).Add(target);((JArray)snapshot["traps"]).Add(new JObject{["id"]="review_rescue_trap",["room_id"]=2,["x"]=10,["y"]=10});
    var input=Input(w);var actions=Array.Empty<string>();DcV7Pilot.Adapt(snapshot,w.Definition,"1",ref input,ref actions,true);
    Require(input.Interact&&input.Move==Vector2.zero&&!input.Attack&&!input.Melee,"safe rescuer never issued the ordinary held interaction");
    snapshot["players"][0]["x"]=10.3f;snapshot["players"][0]["body_center_x"]=(float)snapshot["players"][0]["body_center_x"]-1.05f;input=Input(w);DcV7Pilot.Adapt(snapshot,w.Definition,"1",ref input,ref actions,true);
    Require(!input.Interact&&input.Move.sqrMagnitude>.01f,"exposed rescuer kept holding interaction inside the damaging trap");
   });
   Case("autoplay clears residual enemies before committing an interruptible mechanism",()=>{
    var w=World();Change(w,s=>{s["players"][0]["Room"]=2;s["players"][0]["Position"]=Point(21.281f,9.943f);s["rooms"][2]["Status"]="active";s["rooms"][2]["Mechanism"]=false;var e=s["enemies"][0];e["Kind"]="guard";e["Room"]=2;e["Position"]=Point(21.19f,8.751f);e["Health"]=8;});
    var input=Input(w);input.Interact=true;var actions=Array.Empty<string>();
    DcV7Pilot.Adapt(w.Snapshot(),w.Definition,"1",ref input,ref actions,false);
    Require(!input.Interact&&input.Attack,"inherited mechanism intent suppressed combat with a live nearby enemy");
    input=Input(w);DcV7Pilot.Adapt(w.Snapshot(),w.Definition,"1",ref input,ref actions,false);
    Require(!input.Interact&&input.Attack,"inherited approach suppressed all attacks while the mechanism remained unfinished");
    var snapshot=w.Snapshot();var target=snapshot["enemies"][0];var player=snapshot["players"][0];
    var toward=new Vector2((float)target["body_center_x"]-(float)player["body_center_x"],(float)target["body_center_y"]-(float)player["body_center_y"]).normalized;
    Require(Vector2.Dot(DcP1World.Aim(input.Aim),toward)>.99f,"resumed attacks kept aiming at the mechanism instead of the remaining enemy");
    Change(w,s=>s["players"][0]["ReleaseRequired"]=true);input=Input(w);input.Interact=true;
    DcV7Pilot.Adapt(w.Snapshot(),w.Definition,"1",ref input,ref actions,false);
    Require(!input.Interact&&!input.Attack&&!input.Melee,"resuming combat bypassed the release-required input fence");
    Change(w,s=>s["players"][0]["ReleaseRequired"]=false);
    input=Input(w);input.Interact=true;
    var rescue=w.Snapshot();var casualty=(JObject)rescue["players"][0].DeepClone();casualty["player_id"]="2";casualty["slot"]=1;casualty["health"]=0;casualty["life_state"]="downed";casualty["x"]=(float)casualty["x"]+1;((JArray)rescue["players"]).Add(casualty);
    DcV7Pilot.Adapt(rescue,w.Definition,"1",ref input,ref actions,true);
    Require(input.Interact,"rescue interaction was overridden by the mechanism guard");
    var layout=w.Definition["rooms"][2];var gateID=(string)layout["mandatory_cover_groups"][0]["cover_ids"][0];
    layout["covers"]=new JArray(new JObject{["id"]=gateID,["x_milli"]=18500,["y_milli"]=9500});
    Change(w,s=>{s["players"][0]["Position"]=Point(16,10);s["covers"]=new JArray(new JObject{["ID"]="review_gate",["Room"]=2,["Position"]=Point(18.5f,9.5f),["Health"]=30});});
    input=Input(w);DcV7Pilot.Adapt(w.Snapshot(),w.Definition,"1",ref input,ref actions,false);
    Require(input.Attack&&!input.Interact&&input.Move==Vector2.zero,"remaining enemies prevented opening the authored mandatory gate");
    Change(w,s=>{s["enemies"]=new JArray();s["covers"]=new JArray();s["players"][0]["Position"]=Point(21.281f,9.943f);});input=Input(w);input.Interact=true;
    DcV7Pilot.Adapt(w.Snapshot(),w.Definition,"1",ref input,ref actions,false);
    Require(input.Interact,"clearing residual enemies disabled legitimate mechanism interaction");
   });
   Case("actual movement across inspect-zone edge counts with both endpoints outside",()=>{
    var w=World();var y=10.999f;var x=3-7f/60;
    Change(w,s=>{s["players"][0]["Room"]=4;s["players"][0]["Position"]=Point(x,y);s["players"][0]["ProtectedUntil"]=0;});
    Step(w,1,Vector2.right);var p=Own(w);
    Require(Vector2.Distance(new Vector2((float)p["x"],(float)p["y"]),new Vector2(3,9))>2,"fixture endpoint must remain outside");
    Require(w.Capture()["players"][0]["Reached"].Any(r=>(string)r=="inspect_zone"),"continuous region crossing lost between authority frames");
   });
   Case("restored position does not create an inspect-zone movement sweep",()=>{
    var w=World();Change(w,s=>{s["players"][0]["Room"]=4;s["players"][0]["Position"]=Point(6,9);s["players"][0]["TickStart"]=Point(1,9);});
    Step(w,1);Require(!w.Capture()["players"][0]["Reached"].Any(r=>(string)r=="inspect_zone"),"position restoration fabricated region travel");
    Require(w.Capture()["players"][0]["V7InspectCrossed"]==null,"transient movement evidence leaked into checkpoint state");
   });
   Case("partial-lifetime flight clips target motion and inside state to the same subframe",()=>ReviewFlightWindow(.5f));
   Case("ordinary tick expires a near-deadline rune after its valid moving-target contact",()=>{
    var w=World("runesmith");var body=DcV7Geometry.Profile(parameters,"elite","up");
    var corner=body.Vertices.Where(v=>Math.Abs(v.y-body.Min.y)<.00001f).Min(v=>v.x);
    var extent=Mathf.Sqrt(.3f*.3f-.2f*.2f);var x=8+corner-extent-.001f;var y=9+body.Min.y-.2f;
    Change(w,s=>{var e=s["enemies"][0];e["Position"]=Point(8,9);e["BodyFacing"]="up";e["Aim"]=Point(0,1);e["ChargeUntil"]=10000;e["ReadyAt"]=0;e["AttackAction"]="review_charge";var p=Bullet(x:x,y:y);p["BornGameMS"]=16;p["ExpiresGameMS"]=6016;s["projectiles"]=new JArray(p);});
    w.AdvanceTime(5966);w.Input("1",Input(w));w.Tick(6000);var events=w.DrainV6Lifecycle();
    Require((int)w.Capture()["enemies"][0]["Health"]==9984,"normal flight path missed valid contact near expiry");
    Require(!w.Capture()["projectiles"].Any()&&events.Any(e=>(string)e["reason"]=="expired"),"contact extended the six-second lifetime");
   });
   Case("preview phase calculation includes weapon growth without changing authority cycles",()=>{
    foreach(var kit in new[]{"ranger","runesmith"}){
     var definition=((JArray)parameters["loadouts"]).First(k=>(string)k["id"]==kit)["melee"];
     var plain=DcP1World.MeleePhaseTicks(definition,false,false);var grown=DcP1World.MeleePhaseTicks(definition,kit=="ranger",kit=="runesmith");
     Require(plain.Sum()==(kit=="ranger"?9:21)&&grown.Sum()==(kit=="ranger"?8:19),"growth cycle changed");
     var w=World(kit,true);Change(w,s=>s["players"][0]["Upgrades"]=new JArray(kit=="ranger"?"ranger_tension":"runesmith_twin"));
     w.Action("1","attack_press",Input(w));Step(w,1);var a=Own(w)["melee_action"];
     Require((long)a["active_tick"]-(long)a["started_tick"]==grown[0]&&(long)a["recovery_tick"]-(long)a["active_tick"]==grown[1]&&(long)a["end_tick"]-(long)a["recovery_tick"]==grown[2],"preview and accepted authority phases diverged");
    }
   });
  }
 }
}
