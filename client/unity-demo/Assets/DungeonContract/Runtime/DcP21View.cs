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
        private readonly Dictionary<int,Vector2> roomOffsets=new Dictionary<int,Vector2>();
        private readonly Dictionary<string,Image> mapObjects=new Dictionary<string,Image>();
        private readonly HashSet<int> explored=new HashSet<int>();
        private readonly HashSet<string> knownTraps=new HashSet<string>();
        private Camera backdrop;
        private Text dangerText;
        private Text mapTitle;
        private float p21HitUntil;
        private long quietAt;
        private long previousDamage;
        private int previousEnemies;
        private Vector2 RoomOffset=>roomCameraStage&&roomOffsets.TryGetValue(room,out var offset)?offset:Vector2.zero;
        private bool Visible(Vector2 point){var projected=cameraView.WorldToViewportPoint(point+RoomOffset);return projected.x>=0&&projected.x<=1&&projected.y>=0&&projected.y<=1;}

        private void ConfigureP21View()
        {
            if(geometry==null||cameraView==null)return;
            if(backdrop==null){backdrop=new GameObject("Letterbox clear").AddComponent<Camera>();backdrop.transform.SetParent(transform);backdrop.clearFlags=CameraClearFlags.SolidColor;backdrop.backgroundColor=Color.black;backdrop.cullingMask=0;backdrop.depth=-100;cameraView.depth=-99;}
            roomOffsets[0]=Vector2.zero;
            foreach(var door in geometry["doors"])
            {
                var source=(int)door["from"];var target=(int)door["to"];var side=(string)door["side"];var sourceLayout=geometry["rooms"][source];var targetLayout=geometry["rooms"][target];var offset=roomOffsets[source];
                var change=side=="east"?new Vector2((float)sourceLayout["width"]-1,((float)sourceLayout["height"]-(float)targetLayout["height"])/2):new Vector2(((float)sourceLayout["width"]-(float)targetLayout["width"])/2,-((float)targetLayout["height"]-1));
                roomOffsets[target]=offset+change;
            }
            if(dangerText==null){dangerText=Label("Danger directions",150,80,850,50,18);dangerText.color=new Color(1,0.85f,0.25f);mapTitle=Label("Local map",1070,75,180,25,14);}
            UpdateP21Viewport();
        }

        private void UpdateP21Viewport()
        {
            var multiplier=Math.Max(1,Math.Min(Screen.width/384,Screen.height/216));var width=Math.Min(Screen.width,384*multiplier);var height=Math.Min(Screen.height,216*multiplier);
            cameraView.pixelRect=new Rect((Screen.width-width)/2f,(Screen.height-height)/2f,width,height);cameraView.aspect=16f/9;cameraView.orthographicSize=6.75f;
        }

        private void UpdateP21Camera()
        {
            UpdateP21Viewport();var cfg=DcV8Profile.Active?geometry["parameters"]?["camera"]:null;var dx=(float?)cfg?["dead_zone_half_width_milli_tiles"]/1000??.4f;var dy=(float?)cfg?["dead_zone_half_height_milli_tiles"]/1000??.25f;var smooth=(float?)cfg?["smoothing_ms"]/1000??.06f;var maximumLag=(float?)cfg?["max_lag_milli_tiles"]/1000??1.5f;var current=cameraView.transform.position;var target=CameraTarget(Position);var delta=target-current;
            target.x=current.x+Math.Sign(delta.x)*Math.Max(0,Math.Abs(delta.x)-dx);target.y=current.y+Math.Sign(delta.y)*Math.Max(0,Math.Abs(delta.y)-dy);
            cameraView.transform.position=Vector3.Lerp(current,target,1-Mathf.Exp(-Time.unscaledDeltaTime/Math.Max(.001f,smooth)));if(DcV8Profile.Active){var at=cameraView.transform.position;var lag=(Vector2)CameraTarget(Position)-(Vector2)at;if(lag.magnitude>maximumLag){at.x+=lag.x-lag.normalized.x*maximumLag;at.y+=lag.y-lag.normalized.y*maximumLag;cameraView.transform.position=at;}cameraView.backgroundColor=Color.black;}
            if(ViewTime>=p21HitUntil)crosshair.color=Color.white;
        }

        private void DrawExclamation(string id,Vector2 point,HashSet<string> present)
        {
            if(!Visible(point))return;
            present.Add(id+":stem");present.Add(id+":dot");
            if((bool?)snapshot?["v5_stage"]==true){var scale=DcP5Settings.Active?DcP5Settings.Current.WarningScale:1;Draw(id+":stem",point+Vector2.up*0.0625f*scale,Color.yellow,new Vector2(0.0625f,0.125f)*scale,16);Draw(id+":dot",point-Vector2.up*0.09375f*scale,Color.yellow,Vector2.one*0.0625f*scale,16);return;}
            Draw(id+":stem",point+Vector2.up*0.12f,Color.yellow,new Vector2(0.1f,0.3f),14);
            Draw(id+":dot",point-Vector2.up*0.12f,Color.yellow,Vector2.one*0.1f,14,true);
        }

        private void DrawP21State(JObject value,HashSet<string> present)
        {
            var own=((JArray)value["players"]).First(item=>(string)item["player_id"]==ownID);
            if(own["explored_rooms"] is JArray known)foreach(var item in known)explored.Add((int)item);explored.Add(room);
            var warnings=new Dictionary<int,int>();var incoming=new Dictionary<int,int>();var arrival=new Dictionary<int,float>();
            foreach(var item in ((JArray)value["enemies"]).Where(item=>(int)item["room_id"]==room&&!string.IsNullOrEmpty((string)item["warning"])&&!Visible(Point(item))))AddDirection(warnings,Point(item));
            foreach(var item in ((JArray)value["projectiles"]).Where(item=>(int)item["room_id"]==room&&(bool)item["enemy"]&&!Visible(Point(item))))
            {
                var direction=new Vector2((float)item["direction_x"],(float)item["direction_y"]);var point=Point(item);
                if(EntryThreat(value,point,direction,out var travel,(float?)item["radius"]??0.1f)){AddDirection(incoming,point);var sector=Direction(point);var eta=travel/Math.Max(0.01f,(float?)item["speed"]??10);arrival[sector]=arrival.TryGetValue(sector,out var previous)?Math.Min(previous,eta):eta;}
            }
            foreach(var reservation in ((JArray)value["spawn_reservations"]??new JArray()).Where(item=>(int)item["room_id"]==room))
            {
                if(Visible(Point(reservation))){var id="birth:"+reservation["id"];present.Add(id);Draw(id,Point(reservation),new Color(0.85f,0.4f,1,0.4f),Vector2.one*1.3f,4,true);DrawExclamation(id,Point(reservation),present);}else AddDirection(warnings,Point(reservation));
            }
            var names=new[]{"右","右上","上","左上","左","左下","下","右下"};
            dangerText.text=string.Join("  ",warnings.OrderBy(item=>item.Key).Select(item=>names[item.Key]+" !准备×"+item.Value).Concat(incoming.OrderBy(item=>item.Key).Select(item=>names[item.Key]+" 入屏弹×"+item.Value+" "+arrival[item.Key].ToString("0.0")+"s")));
            var remaining=(int)value["rooms"][room]["enemies_remaining"];var game=(long)value["elapsed_ms"];var damage=(long)own["damage"];
            if(damage!=previousDamage||remaining!=previousEnemies){quietAt=game;previousDamage=damage;previousEnemies=remaining;}
            if(remaining>0&&remaining<=2&&game-quietAt>=5000){var enemy=((JArray)value["enemies"]).FirstOrDefault(item=>(int)item["room_id"]==room);if(enemy!=null){var direction=Direction(Point(enemy));dangerText.text+="  剩余敌人大致在"+names[direction]+"方";}}
            DrawP21Map(value,own);
        }
        private int Direction(Vector2 point){var delta=point-Position;return (Mathf.RoundToInt(Mathf.Atan2(delta.y,delta.x)*4/Mathf.PI)+8)%8;}
        private bool EntryThreat(JObject value,Vector2 point,Vector2 direction,out float travel,float radius=0.1f)
        {
            var center=(Vector2)cameraView.transform.position-RoomOffset;var lower=center-new Vector2(12,6.75f);var upper=center+new Vector2(12,6.75f);var near=0f;var far=float.MaxValue;travel=0;
            for(var axis=0;axis<2;axis++){var coordinate=axis==0?point.x:point.y;var velocity=axis==0?direction.x:direction.y;var minimum=axis==0?lower.x:lower.y;var maximum=axis==0?upper.x:upper.y;if(Math.Abs(velocity)<0.0001f){if(coordinate<minimum||coordinate>maximum)return false;continue;}var first=(minimum-coordinate)/velocity;var last=(maximum-coordinate)/velocity;if(first>last){var swap=first;first=last;last=swap;}near=Math.Max(near,first);far=Math.Min(far,last);if(near>far)return false;}
            if(far<0)return false;travel=near;var end=point+direction*travel;var layout=(JObject)geometry["rooms"][room];
            if(DcP5Settings.Active)
            {
                if((DcV6Profile.Active?DcV6Geometry.Terrain(layout,point,end,radius).HasValue:DcV5Collision.Terrain(layout,point,end,radius).HasValue))return false;
                foreach(var cover in value["covers"])if((int)cover["room_id"]==room&&(int)cover["health"]>0&&DcV5Collision.Circle(point,end,Point(cover),0.45f+radius).HasValue)return false;
                foreach(var field in value["fields"])if((int)field["room_id"]==room&&(string)field["kind"]=="wind_bind"&&DcV5Collision.Circle(point,end,Point(field),((float?)field["radius"]??2.5f)+radius).HasValue)return false;
                return true;
            }
            for(var offset=0f;offset<=travel;offset+=0.2f){var sample=point+direction*offset;if(RoomGeometry.Blocked(layout,sample,0.1f)||((JArray)value["covers"]).Any(item=>(int)item["room_id"]==room&&(int)item["health"]>0&&Vector2.Distance(sample,Point(item))<0.55f)||((JArray)value["fields"]).Any(item=>(int)item["room_id"]==room&&(string)item["kind"]=="wind_bind"&&Vector2.Distance(sample,Point(item))<((float?)item["radius"]??2.5f)))return false;}
            return !RoomGeometry.Blocked(layout,end,0.1f);
        }
        private void AddDirection(Dictionary<int,int> groups,Vector2 point){var direction=Direction(point);groups[direction]=groups.TryGetValue(direction,out var amount)?amount+1:1;}
        private void MapDot(string id,Vector2 point,Vector2 size,Color color,HashSet<string> present)
        {
            present.Add(id);if(!mapObjects.TryGetValue(id,out var item)){item=new GameObject("Map "+id,typeof(RectTransform),typeof(Image)).GetComponent<Image>();item.transform.SetParent(UiRoot,false);item.material=uiMaterial;item.raycastTarget=false;mapObjects[id]=item;}
            item.gameObject.SetActive(true);item.color=color;Rect(item.rectTransform,point.x,point.y,size.x,size.y);
        }
        private void DrawP21Map(JObject value,JToken own)
        {
            var present=new HashSet<string>();var layout=geometry["rooms"][room];var width=(float)layout["width"];var height=(float)layout["height"];var scale=Math.Min(160/width,90/height);
            mapTitle.text="已探索 "+explored.Count+"区 · 本房";
            Func<Vector2,Vector2> project=point=>new Vector2(1080+point.x*scale,110+(height-point.y)*scale);
            MapDot("outline",new Vector2(1080,110),new Vector2(width*scale,height*scale),new Color(0.12f,0.17f,0.23f,0.85f),present);
            foreach(var door in geometry["doors"]){var forward=(int)door["from"]==room;var backward=(int)door["to"]==room;if(!forward&&!backward)continue;var side=forward?(string)door["side"]:RoomGeometry.Opposite((string)door["side"]);MapDot("door:"+side,project(RoomGeometry.DoorPoint((JObject)layout,side)),Vector2.one*5,(string)value["rooms"][room]["status"]=="cleared"?Color.green:Color.red,present);}
            foreach(var trap in ((JArray)value["traps"]).Where(item=>(int)item["room_id"]==room)){var id=(string)trap["id"];if(Visible(Point(trap)))knownTraps.Add(id);if(knownTraps.Contains(id))MapDot("trap:"+id,project(Point(trap)),Vector2.one*4,Color.yellow,present);}
            if(layout["objective"] is JArray objectivePoint)MapDot("objective",project(RoomGeometry.Point(objectivePoint)),Vector2.one*6,Color.cyan,present);
            foreach(var player in ((JArray)value["players"]).Where(item=>(int)item["room_id"]==room))MapDot("player:"+player["player_id"],project(Point(player)),Vector2.one*5,(string)player["player_id"]==ownID?Color.white:Color.blue,present);
            foreach(var knownRoom in explored.Where(item=>item<8).OrderBy(item=>item)){MapDot("seen:"+knownRoom,new Vector2(1080+knownRoom*18,215),new Vector2(12,8),knownRoom==room?Color.white:(string)value["rooms"][knownRoom]["status"]=="cleared"?Color.green:Color.gray,present);}
            foreach(var item in mapObjects)if(!present.Contains(item.Key))item.Value.gameObject.SetActive(false);
        }
    }
}
