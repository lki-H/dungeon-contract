using System.Linq;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public static class DcP5PressureFixture
    {
        public static DcP1World Create(JObject parameters,JObject geometry,JObject configuration,string kit,out JObject initial)
        {
            var members=new JArray(Enumerable.Range(0,4).Select(index=>new JObject{["player_id"]=(index+1).ToString(),["slot"]=index,["loadout_id"]=kit}));var manifest=new JObject{["run_id"]="isolated_p5_pressure",["seed"]=20261012,["parameters"]=parameters,["p1_world"]=geometry,["config"]=configuration,["p2_stage"]=true,["p3_stage"]=true,["p4_stage"]=true,["weapon_stage"]=true,["members"]=members};var world=new DcP1World(manifest);var practice=(JObject)manifest.DeepClone();practice["practice"]=true;var enemyTemplate=(JObject)new DcP1World(practice).Capture()["enemies"][0].DeepClone();initial=world.Capture();
            foreach(var player in initial["players"]){var slot=(int)player["Slot"];player["Room"]=slot==0?1:5;player["Position"]=Point(4+slot,8);player["InvulnerableUntil"]=long.MaxValue;if((int?)parameters["versions"]?["battle_protocol"]>=6)player["ProtectedUntil"]=long.MaxValue;player["Upgrades"]=kit=="ranger"?new JArray("ranger_fan","ranger_pierce","frost"):new JArray("runesmith_twin","runesmith_heavy","runesmith_chain");}
            foreach(var room in new[]{1,5}){initial["rooms"][room]["Status"]="active";initial["rooms"][room]["Wave"]=1;}
            initial["enemies"]=new JArray();initial["projectiles"]=new JArray();initial["turrets"]=new JArray();initial["spawn_reservations"]=new JArray();initial["serial"]=10000;
            for(var index=0;index<24;index++){var enemy=(JObject)enemyTemplate.DeepClone();enemy["ID"]=index==0?"boss":"pressure_enemy_"+index;enemy["Kind"]=index==0?"boss":index%6==0?"elite":index%3==0?"archer":index%3==1?"guard":"charger";enemy["Room"]=index<13?5:1;enemy["Summon"]=index>0&&index<13;enemy["Position"]=Point(10+index%4*2,4+index/4%3*3);enemy["Origin"]=enemy["Position"].DeepClone();enemy["Health"]=50000;enemy["Maximum"]=100000;enemy["ReadyAt"]=0;enemy["AttackAt"]=0;((JArray)initial["enemies"]).Add(enemy);}
            var protocol=(int?)parameters["versions"]?["battle_protocol"]??5;var v7=protocol>=7;var v6=(int?)parameters["versions"]?["battle_protocol"]>=6;var ranged=parameters["loadouts"].First(item=>(string)item["id"]==kit)["ranged"];
            for(var index=0;index<1024;index++)((JArray)initial["projectiles"]).Add(new JObject{["ProtocolVersion"]=protocol,["BornGameMS"]=0,["ExpiresGameMS"]=v7&&kit=="runesmith"?6000:0,["ID"]="projectile_"+(index+1).ToString("D6"),["Root"]="pressure_"+index,["Action"]="projectile_"+(index+1).ToString("D6"),["Owner"]="1",["Room"]=index%2==0?5:1,["Position"]=Point(27,3+index%10),["Direction"]=Point(-1,0),["Speed"]=v6?(float)ranged["projectile_speed_milli_tiles_per_second"]/1000:5,["Damage"]=v6?(int)ranged["damage"]:12,["Unbounded"]=true,["Enemy"]=false,["RuneBolt"]=v6&&kit=="runesmith",["Bounces"]=v6&&kit=="runesmith"?(v7?int.MaxValue:1):0,["Penetration"]=v6&&kit=="ranger"?2:0,["ChainEnabled"]=v6&&kit=="runesmith",["FrostEnabled"]=v6&&kit=="ranger",["Hit"]=new JArray(),["CollisionRadius"]=v6?(float)ranged["projectile_radius_milli_tiles"]/1000:0.1f,["NetworkGroup"]=index%43,["SourceLife"]=1});
            if(kit=="runesmith")for(var index=0;index<8;index++)((JArray)initial["turrets"]).Add(new JObject{["ID"]="pressure_turret_"+index,["Owner"]=(index/2+1).ToString(),["SourceLife"]=1,["Room"]=index<2?1:5,["Position"]=Point(4+index/2,7+index%2),["Until"]=8000,["AttackAt"]=0,["BaseDamage"]=9});
            if(v7)DcV7PressureFixture.PlaceLegalBodiesAndFlights(parameters,world.Definition,initial);
            if(protocol==8)DcV8PressureFixture.Authority(initial,kit);
            return world;
        }
        private static JObject Point(float horizontal,float vertical)=>new JObject{["x"]=horizontal,["y"]=vertical};
    }
}
