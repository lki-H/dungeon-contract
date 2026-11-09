"""Read-only 2.3 document/geometry/history validation. No gameplay implementation."""
import argparse
import copy
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
from collections import deque
from pathlib import Path

FOLDER=Path(__file__).resolve().parent
ROOT=FOLDER.parents[3]
DOCS=ROOT/"docs/game-demo"
CACHE=Path("D:/dev-cache/dungeon-contract/docs-v6")
EDITABLE={"README.md","AGENTS.md"}|{"docs/game-demo/"+name for name in [
    "index.md","gdd.md","architecture.md","protocols.md","data-design.md",
    "content-config.md","client-assets.md","security-design.md","test-and-runbook.md",
    "p1-human-playtest-and-pacing-guide.md","image-generation-spec.md",
    "implementation-status.md","baseline-and-validation.md","decisions.md",
    "one-click-playtest-launcher.md","environment-setup.md"
]}
NEW_FILES={
    "docs/game-demo/v6-preimplementation-manifest.json",
    "docs/game-demo/movement-projectile-traceability.json",
    "docs/game-demo/movement-projectile-amendment.md",
    "docs/game-demo/documentation-review-2-3.json",
    "docs/game-demo/documentation-change-2-3.json",
    "docs/game-demo/documentation-delivery-2-3.json",
    "docs/game-demo/templates/v6-human-playtest-record.md",
}
EXCLUDED_RECORDS={"docs/game-demo/documentation-change-2-3.json","docs/game-demo/documentation-delivery-2-3.json"}
SKIP={"node_modules",".git","Library","Temp","Logs","UserSettings","obj","bin","dist","__pycache__","tmp","temp",".air"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def require(value,message):
    if not value:raise AssertionError(message)


def safe(root,relative):
    require(not Path(relative).is_absolute() and not re.match(r"^[A-Za-z]:",relative),"Absolute manifest path")
    target=(root/relative).resolve()
    target.relative_to(root.resolve())
    return target


def fingerprint(path):
    data=path.read_bytes()
    return {"path":path.relative_to(ROOT).as_posix(),"bytes":len(data),"sha256":hashlib.sha256(data).hexdigest()}


def all_files():
    result=[]
    for current,dirs,names in os.walk(ROOT,followlinks=False):
        dirs[:]=[name for name in dirs if name not in SKIP and not Path(current,name).is_symlink()]
        for name in names:
            path=Path(current,name)
            if path.is_symlink() or path.suffix.lower() in {".exe",".pdb",".log",".tmp",".pyc",".csproj",".sln",".slnx"} or name.startswith(".env") and name!=".env.example":continue
            result.append(fingerprint(path))
    return sorted(result,key=lambda item:item["path"])


def allowed_new(path):
    return path in NEW_FILES or path.startswith("docs/game-demo/contracts/planned-v6/") or path.startswith("docs/game-demo/archive/2.2/")


def history(final):
    pre=read(DOCS/"v6-preimplementation-manifest.json")
    indexed={item["path"]:item for item in pre["files"]}
    current=all_files()
    present={item["path"]:item for item in current}
    changed=[];added=[]
    for path,before in indexed.items():
        require(path in present,"Deleted preexisting file: "+path)
        after=present[path]
        if after["sha256"]!=before["sha256"]:
            require(path in EDITABLE,"Protected source/config/SQL/resource/evidence changed: "+path)
            changed.append({"path":path,"before_sha256":before["sha256"],"after_sha256":after["sha256"],"before_bytes":before["bytes"],"after_bytes":after["bytes"]})
    for path,item in present.items():
        if path not in indexed:
            require(allowed_new(path),"Unexpected new file: "+path)
            if path not in EXCLUDED_RECORDS:added.append(item)
    archive=read(DOCS/"archive/2.2/manifest.json")
    require(len(archive["files"])==16,"Formal archive count")
    for item in archive["files"]+archive.get("additional_files",[]):
        original=indexed[item["original_path"]]
        require(original["sha256"]==item["original_sha256"],"Unknown archive preimage")
        raw=safe(ROOT,item["exact_copy_path"]).read_bytes()
        require(len(raw)==item["original_bytes"] and hashlib.sha256(raw).hexdigest()==original["sha256"],"Archive exact bytes")
        if "archive_path" in item:
            require(fingerprint(safe(ROOT,item["archive_path"]))["sha256"]==item["archive_sha256"],"Readable archive fingerprint")
    changed.sort(key=lambda item:item["path"]);added.sort(key=lambda item:item["path"])
    if final:
        record=read(DOCS/"documentation-change-2-3.json")
        require(record["changed"]==changed and record["added"]==added and record["deleted"]==[],"Final change record differs")
        delivery=read(DOCS/"documentation-delivery-2-3.json")
        require(delivery["self_included"] is False,"Self hash cycle")
        expected=[item for item in current if item["path"]!="docs/game-demo/documentation-delivery-2-3.json"]
        require(delivery["files"]==expected,"Final source delivery fingerprints differ")
    return {"preimage_files":len(indexed),"unchanged_protected_files":len(indexed)-len(changed),"changed_documents":len(changed),"new_document_entries":len(added),"formal_archives":len(archive["files"]),"original_runtime_files_preserved":True}


def dist_segment(p,a,b):
    dx,dy=b[0]-a[0],b[1]-a[1]
    q=((p[0]-a[0])*dx+(p[1]-a[1])*dy)/(dx*dx+dy*dy) if dx or dy else 0
    q=max(0,min(1,q))
    return math.hypot(p[0]-a[0]-q*dx,p[1]-a[1]-q*dy)


def rect_distance(point,rect):
    x,y,w,h=[v/1000 for v in rect]
    return math.hypot(max(x-point[0],0,point[0]-x-w),max(y-point[1],0,point[1]-y-h))


def circle_point(item):
    return item["x_milli"]/1000,item["y_milli"]/1000


def rectangles_overlap(a,b):
    return a[0]<b[0]+b[2] and b[0]<a[0]+a[2] and a[1]<b[1]+b[3] and b[1]<a[1]+a[3]


def walk_component(room,variant,remove_gate=False):
    step=0.25;width=(room["width"]-2)*4+1;height=(room["height"]-2)*4+1
    blocked=set()
    def mark(bounds,predicate):
        left,bottom,right,top=bounds
        for ix in range(max(0,math.floor((left-1)/step)),min(width,math.ceil((right-1)/step)+1)):
            for iy in range(max(0,math.floor((bottom-1)/step)),min(height,math.ceil((top-1)/step)+1)):
                point=(1+ix*step,1+iy*step)
                if predicate(point):blocked.add((ix,iy))
    radius=0.24
    for ix in range(width):
        for iy in (0,height-1):blocked.add((ix,iy))
    for iy in range(height):
        for ix in (0,width-1):blocked.add((ix,iy))
    for rect in variant["fixed_rects_milli"]:
        x,y,w,h=[v/1000 for v in rect]
        mark((x-radius,y-radius,x+w+radius,y+h+radius),lambda point,shape=rect:rect_distance(point,shape)<radius-1e-8)
    for cover in variant["covers"]:
        if remove_gate and cover["mandatory_group"]:continue
        x,y=circle_point(cover);distance=0.45+radius
        mark((x-distance,y-distance,x+distance,y+distance),lambda point,a=x,b=y,r=distance:math.hypot(point[0]-a,point[1]-b)<r-1e-8)
    def node(point):return (round((point[0]-1)/step),round((point[1]-1)/step))
    start=node(room["spawn"]);require(start not in blocked,room["id"]+" spawn blocked")
    seen={start};queue=deque([start])
    while queue:
        ix,iy=queue.popleft()
        for next_point in [(ix-1,iy),(ix+1,iy),(ix,iy-1),(ix,iy+1)]:
            x,y=next_point
            if 0<=x<width and 0<=y<height and next_point not in blocked and next_point not in seen:
                seen.add(next_point);queue.append(next_point)
    return seen,node


def geometry(world=None):
    world=read(FOLDER/"world.json") if world is None else world
    p=read(FOLDER/"parameters.json")
    require(len(world["rooms"])==15,"Room graph changed")
    rows=[];gate_rooms=[];selected_covers=0;selected_traps=0
    by_id={room["id"]:room for room in world["rooms"]}
    require(len(world["door_boundaries"])==len(world["doors"])==14,"Door boundary objects missing")
    for boundary in world["door_boundaries"]:
        for prefix in ["from","to"]:
            room=by_id[boundary[prefix+"_room"]]
            center=[v/1000 for v in boundary[prefix+"_player_transition_plane_milli"]]
            side=boundary[prefix+"_side"]
            normal={"west":[1,0],"east":[-1,0],"north":[0,-1],"south":[0,1]}[side]
            arrival=[center[i]+normal[i]*0.7 for i in [0,1]]
            require(boundary["opening_width_milli"]>=2000 and boundary["projectile_crossing"] is False,"Door width/attack isolation")
            require(1.24<=arrival[0]<=room["width"]-1.24 and 1.24<=arrival[1]<=room["height"]-1.24,"Door arrival crosses wall")
            for variant in room["variants"]:
                require(all(rect_distance(arrival,rect)>=0.24 for rect in variant["fixed_rects_milli"]),"Door arrival over fixed prop")
                require(all(math.dist(arrival,circle_point(item))>=0.24+item["radius_milli"]/1000 for item in variant["covers"]+variant["traps"]),"Door arrival over cover/trap")
    for room in world["rooms"]:
        require(room["width"]<=34 and room["height"]<=20,"Room maximum")
        require(1.24<=room["spawn"][0]<=room["width"]-1.24 and 1.24<=room["spawn"][1]<=room["height"]-1.24,"Static player spawn crosses room wall")
        selected_covers+=max(len(v["covers"]) for v in room["variants"])
        selected_traps+=max(len(v["traps"]) for v in room["variants"])
        for variant in room["variants"]:
            shapes=variant["fixed_rects_milli"];covers=variant["covers"];traps=variant["traps"]
            require(len(shapes)<=128 and len(covers)<=128 and len(traps)<=24,"Per-room prop budget")
            require(len({tuple(rect) for rect in shapes})==len(shapes),"Duplicate fixed rectangles")
            ids=[item["id"] for item in covers+traps]
            require(len(set(ids))==len(ids),"Duplicate prop identity")
            for index,rect in enumerate(shapes):
                x,y,w,h=rect
                require(x>=1000 and y>=1000 and x+w<=(room["width"]-1)*1000 and y+h<=(room["height"]-1)*1000,"Fixed shape outside inner room")
                require(all(not rectangles_overlap(rect,other) for other in shapes[index+1:]),"Overlapping fixed rectangles")
            circles=[(item,item["radius_milli"]/1000) for item in covers+traps]
            require(all(t["group"] in ("A","B","C") and t["radius_milli"]==700 for t in traps),"Invalid trap group or shape")
            for index,(item,radius) in enumerate(circles):
                point=circle_point(item)
                require(point[0]-radius>=1 and point[1]-radius>=1 and point[0]+radius<=room["width"]-1 and point[1]+radius<=room["height"]-1,"Circle crosses boundary")
                require(all(rect_distance(point,rect)>=radius-1e-8 for rect in shapes),"Circle/fixed overlap")
                require(all(math.dist(point,circle_point(other))>=radius+other_radius-1e-8 for other,other_radius in circles[index+1:]),"Circular props overlap")
            area=sum(rect[2]*rect[3]/1000000 for rect in shapes)+sum(math.pi*radius*radius for item,radius in circles)
            inner=(room["width"]-2)*(room["height"]-2);ratio=area/inner
            require(abs(ratio-variant["density"]["union_ratio"])<1e-7,"Wrong actual union area")
            if "target_min" in variant["density"]:require(variant["density"]["target_min"]<=ratio<=variant["density"]["target_max"],"Density out of accepted range")
            for route in variant["safe_routes"]:
                require(route["minimum_clear_width_milli"]>=2000,"Narrow declared main route")
                for a,b in zip(route["points"],route["points"][1:]):
                    require(a[0]==b[0] or a[1]==b[1],"Non-axis route not covered by exact route checker")
                    for rect in shapes:
                        x,y,w,h=[v/1000 for v in rect]
                        route_box=[min(a[0],b[0]),min(a[1],b[1]),abs(a[0]-b[0]),abs(a[1]-b[1])]
                        dx=max(x-route_box[0]-route_box[2],route_box[0]-x-w,0)
                        dy=max(y-route_box[1]-route_box[3],route_box[1]-y-h,0)
                        require(math.hypot(dx,dy)>=1-1e-8,"Fixed prop narrows two-tile safe route")
                    for item,radius in circles:
                        require(dist_segment(circle_point(item),a,b)>=1+radius-1e-8,"Cover/trap narrows safe route")
            if room["kind"]=="room" and room["id"]!="entry":
                closed,node=walk_component(room,variant)
                for route in variant["safe_routes"]:
                    require(all(node(point) in closed for point in route["points"]),"Safe route not reachable")
                require(len(variant["spawn_candidates"])>=1,"No spawn candidates")
                for point in variant["spawn_candidates"]:
                    require(node(point) in closed,"Spawn in disconnected pocket: "+variant["id"]+" "+str(point))
                    require(all(rect_distance(point,rect)>=0.9-1e-8 for rect in shapes),"Largest-entity spawn blocked")
                    require(all(math.dist(point,circle_point(item))>=0.9+radius-1e-8 for item,radius in circles),"Spawn over prop/trap")
                    doors=[edge[prefix+"_player_transition_plane_milli"] for edge in world["door_boundaries"] for prefix in ["from","to"] if edge[prefix+"_room"]==room["id"]]
                    require(all(math.dist(point,[v/1000 for v in door])>=2-1e-8 for door in doors),"Enemy spawn blocks entrance")
                if room["objective"]:
                    require(node(room["objective"]) not in closed,"Mandatory gate can be bypassed")
                    opened,node=walk_component(room,variant,True)
                    require(node(room["objective"]) in opened,"Mechanism unreachable after named gate broken")
                    require(len(variant["mandatory_cover_groups"])==1 and all(c["health"]==30 for c in covers),"Mandatory group definition")
                    gate_rooms.append(room["id"])
                else:require(not variant["mandatory_cover_groups"],"Gate outside mechanism rooms")
                if variant["open_combat_core"]:
                    core=variant["open_combat_core"];center=core["center"];radius=core["radius_milli"]/1000
                    require(all(rect_distance(center,rect)>=radius-1e-8 for rect in shapes),"Boss core blocked")
                    require(all(math.dist(center,circle_point(item))>=radius+other_radius-1e-8 for item,other_radius in circles),"Boss core contains props")
            rows.append({"variant":variant["id"],"density":round(ratio,8),"fixed":len(shapes),"covers":len(covers),"traps":len(traps),"spawn_candidates":len(variant["spawn_candidates"]),"status":"PASS_DOCUMENT_GEOMETRY"})
    require(set(gate_rooms)=={"mechanism","defense"},"Mandatory groups scope")
    require(selected_covers<=p["budgets"]["covers_world"] and selected_traps<=p["budgets"]["traps_world"],"World prop budget")
    require(len(rows)==19,"Variant count")
    require(world["doors"]==read(DOCS/"config/world-v5-p4.json")["doors"],"Original door graph changed")
    region=by_id["sanctum"]["regions"]
    require(region==[{"id":"inspect_zone","kind":"reach","center_milli":[3000,9000],"radius_milli":2000,"semantics":"existing_sanctum_entry_region; real_movement_crossing_or_actual_admission_only_not_correction_teleport"}],"Unapproved contract reach semantics")
    require([item["id"] for room in world["rooms"] for item in room["shared_objects"]]==["hub_a","hub_b"],"Shared objective identity")
    return {"rooms":15,"variants":len(rows),"selected_cover_count":selected_covers,"selected_trap_count":selected_traps,"mandatory_rooms":sorted(set(gate_rooms)),"connectivity_grid_step_tiles":0.25,"route_width_geometry":"continuous exact axis-segment vs actual circles/rectangles; separate from sampled connectivity","rows":rows}


def negative_geometry():
    original=read(FOLDER/"world.json")
    scenarios=[]
    def rejects(name,mutate,expected):
        value=copy.deepcopy(original);mutate(value)
        try:geometry(value)
        except AssertionError as error:
            require(expected in str(error),"Wrong rejection reason for "+name+": "+str(error))
            scenarios.append({"case":name,"rejected":True,"reason":str(error)})
        else:raise AssertionError("Invalid geometry accepted: "+name)
    rejects("false_density",lambda w:w["rooms"][1]["variants"][0]["density"].update(union_ratio=0.01),"Wrong actual union area")
    rejects("overlapping_fixed",lambda w:w["rooms"][1]["variants"][0]["fixed_rects_milli"].append(w["rooms"][1]["variants"][0]["fixed_rects_milli"][0]),"Duplicate fixed")
    rejects("overlapping_covers",lambda w:w["rooms"][1]["variants"][0]["covers"][1].update(x_milli=w["rooms"][1]["variants"][0]["covers"][0]["x_milli"],y_milli=w["rooms"][1]["variants"][0]["covers"][0]["y_milli"]),"Circular props overlap")
    rejects("narrow_safe_route",lambda w:w["rooms"][1]["variants"][0]["safe_routes"][0].update(minimum_clear_width_milli=500),"Narrow declared")
    rejects("invalid_trap_group",lambda w:w["rooms"][1]["variants"][0]["traps"][0].update(group="D"),"Invalid trap group")
    rejects("spawn_on_wall",lambda w:w["rooms"][8].update(spawn=[1,2]),"Static player spawn crosses")
    rejects("spawn_in_closed_hub",lambda w:w["rooms"][2]["variants"][0]["spawn_candidates"].append(w["rooms"][2]["objective"]),"Spawn in disconnected")
    def remove_gate(world):
        variant=world["rooms"][2]["variants"][0]
        variant["covers"]=variant["covers"][2:]
        density=variant["density"]
        density["cover_area_tiles2"]=len(variant["covers"])*math.pi*0.45**2
        density["union_ratio"]=(density["fixed_area_tiles2"]+density["cover_area_tiles2"]+density["trap_area_tiles2"])/density["inner_area_tiles2"]
    rejects("mandatory_gate_bypass",remove_gate,"Mandatory gate can be bypassed")
    return scenarios


def pointer(value,path):
    for part in path.strip("/").split("/"):
        value=value[int(part)] if isinstance(value,list) else value[part.replace("~1","/").replace("~0","~")]
    return value


def semantics():
    p=read(FOLDER/"parameters.json");old=read(DOCS/"config/parameters-v5-p4.json")
    require(p["status"]=="planned_not_loaded" and p["published"] is False and p["specification_version"]=="2.3","Plan identity")
    require(p["versions"]=={"operation":"dc_ruins.v6","content":"dc_content.v6.draft1","world":"dc_rooms.v6.draft1","tasks":"dc_tasks.v5.p4.1","trial":"trial.v6.draft1","battle_protocol":6,"control_schema":2},"Version matrix")
    for name in ["enemies","growth","scaling","rewards","clocks","tasks","encounters","inheritance"]:
        require(p[name]==old[name],"Unapproved change of inherited "+name)
    require(p["loadouts"][1]["melee"]["charge"]==old["loadouts"][1]["melee"]["charge"],"Slam numbers changed")
    require(p["player"]["move_milli_tiles_per_second"]==7000 and p["player"]["collision_radius_milli_tiles"]==240 and p["player"]["hurt_radius_milli_tiles"]==240,"Movement/body initial values")
    require([p["player"][key] for key in ["dodge_distance_milli_tiles","dodge_duration_ms","dodge_invulnerable_ms"]]==[8000,600,600],"Space values")
    require([p["loadouts"][0]["primary"][key] for key in ["dash_distance_milli_tiles","dash_duration_ms","dash_invulnerable_ms"]]==[4000,300,300],"Gust values")
    require(p["loadouts"][1]["ranged"]["projectile_radius_milli_tiles"]==720 and p["loadouts"][1]["primary"]["projectile_radius_milli_tiles"]==720,"Fourfold rune/turret radius")
    require(p["melee_geometry"]["full_sector_clear"] is False and p["melee_geometry"]["persistent_trail_clear"] is False,"Final question24 violated")
    for source in p["provenance"]["sources"]:
        require(fingerprint(safe(ROOT,source["path"]))["sha256"]==source["sha256"],"Runtime source provenance changed")
    trace=read(DOCS/"movement-projectile-traceability.json");accept=read(FOLDER/"acceptance.json")
    require(len(trace["feedback"])==11 and len(trace["questions"])==26,"Feedback/question coverage")
    require([q["id"] for q in trace["questions"]]==[f"Q23-{i:02}" for i in range(1,27)],"Question IDs")
    require(len(accept["cases"])==60 and len({c["id"] for c in accept["cases"]})==60,"D23 case IDs")
    require(all(c["status"]=="NOT_RUN" and c["evidence"]==[] for c in accept["cases"]),"Unrun gameplay test promoted")
    cases={c["id"] for c in accept["cases"]};decisions=(DOCS/"decisions.md").read_text()
    for q in trace["questions"]:
        require(q["tests"] and set(q["tests"])<=cases,"Unmapped question")
        require(q["id"] in decisions and q["answer"] in decisions,"Decision ledger missing answer")
        for path in q["parameter_pointers"]:pointer(p,path)
    for entry in trace["feedback"]:require(entry["tests"] and set(entry["tests"])<=cases,"Unmapped raw feedback")
    q24=trace["questions"][23]
    require(q24["answer"]=="随刀刃/锤头扫掠" and q24["history"][0]["status"]=="SUPERSEDED" and q24["history"][1]["status"]=="FINAL","Question24 history lost")
    vectors=read(FOLDER/"semantic-cases.json")
    for case in vectors["trap_phase_cases"]:
        offset=p["terrain"]["trap_group_offsets_ms"][case["group"]];elapsed=case["elapsed_game_ms"]-offset
        phase="idle" if elapsed<0 else "warning" if elapsed%3000<600 else "active" if elapsed%3000<800 else "cooldown"
        require(case["expected"]==phase and case["cycle_index"]==max(0,elapsed//3000),"Trap phase vector")
    for case in vectors["contact_order_cases"]:
        hits=[(case[name],priority,kind) for name,priority,kind in [("terrain_toi",0,"terrain_first"),("clear_toi_u16",1,"clear_no_hurt"),("hurt_toi_u16",2,"hurt_not_reversed")] if case[name] is not None]
        require(min(hits)[2]==case["expected"],"Contact ordering vector")
    for case in vectors["damage_cases"]:
        q=max(0,min(1,(case["held_ms"]-250)/650));damage=math.floor(32+32*q)
        require(case["wave_damage"]==damage and case["direct_total"]==damage*2,"Unchanged slam fixture")
    index=read(FOLDER/"examples-index.json")
    for item in index["examples"]:
        value=read(FOLDER/item["path"])
        own=value.get("own")
        if own:
            state=own["attack_state"]
            require(state["preview_impact_valid"]==(state["preview_impact_milli"] is not None),"Preview validity differs from point")
            require(state["impact_valid"]==(state["committed_impact_milli"] is not None),"Committed impact validity differs from point")
            require(own["invulnerable_until_game_ms"]==max(own["protection_sources"].values()),"Protection aggregate differs from sources")
            for weapon in own["weapons"]:
                if weapon["reload_until_game_ms"]:
                    require(weapon["reload_started_game_ms"]+weapon["reload_duration_ms"]==weapon["reload_until_game_ms"],"Reload timeline differs from deadline")
        if "fingerprint" in value:
            signature=value.pop("fingerprint");encoded=json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(",",":")).encode()
            require(hashlib.sha256(encoded).hexdigest()==signature,"Synthetic terminal fingerprint")
    life=read(FOLDER/"projectile-lifecycle.example.json");events=life["events"]
    require([e["sequence"] for e in events]==list(range(life["first_sequence"],life["last_sequence"]+1)),"Lifecycle contiguous sequence")
    require([e["kind"] for e in events]==["spawn","bounce","terminate"],"Lifecycle example order")
    require(events[1]["trajectory_revision"]==events[2]["trajectory_revision"]==2 and events[0]["trajectory_revision"]==1,"Bounce segment revision")
    require(len(json.dumps(life,ensure_ascii=False,separators=(",",":")).encode())<=16384,"Lifecycle sample byte cap")
    admission=read(FOLDER/"admission.example.json")
    for name in ["parameters","world"]:
        file=FOLDER/(name+".json");artifact=admission["content_artifacts"][name]
        require(hashlib.sha256(file.read_bytes()).hexdigest()==artifact["sha256"] and file.stat().st_size==artifact["bytes"],"Admission artifact hash")
    require(47+32*24==815 and 43+math.ceil(216/24)==52 and 64*815*30==1564800,"Codec candidate arithmetic")
    require(sum([32768,67584,16384,24576,8192,4096,4096,32768,16384])==206848<262144,"Baseline budget arithmetic")
    compact=lambda value:len(json.dumps(value,ensure_ascii=False,separators=(",",":")).encode())
    require(compact(read(FOLDER/"world.json"))==183566 and compact(p)==25389,"Update documented static artifact byte counts")
    return {"feedback":11,"confirmed_questions":26,"final_question24":"actual_current_swept_contact","new_behavior_cases":60,"all_behavior_cases_status":"NOT_RUN","logical_examples":len(index["examples"]),"trap_boundary_vectors":len(vectors["trap_phase_cases"]),"contact_vectors":len(vectors["contact_order_cases"]),"runtime_or_human_tested":False}


def links_and_json():
    files=[p for folder in [DOCS,ROOT/"docs/design"] for p in folder.rglob("*") if p.is_file()]+[ROOT/"README.md",ROOT/"AGENTS.md"]
    counts={"markdown_files":0,"json_files":0,"local_links":0,"broken_links":0}
    for path in files:
        if path.suffix==".json":
            value=read(path);counts["json_files"]+=1
            def refs(item):
                if isinstance(item,list):
                    for child in item:refs(child)
                elif isinstance(item,dict):
                    if "$ref" in item:
                        ref=item["$ref"]
                        if not re.match(r"^https?://",ref):
                            filename,_,fragment=ref.partition("#");target=path.parent/filename if filename else path
                            require(target.is_file(),"Missing Schema ref: "+str(target))
                            doc=read(target)
                            if fragment: pointer(doc,fragment)
                    for child in item.values():refs(child)
            refs(value)
        if path.suffix!=".md":continue
        counts["markdown_files"]+=1;prose=[];fence=None
        for line in path.read_text(encoding="utf-8-sig").splitlines():
            marker=re.match(r"^\s{0,3}(`{3,}|~{3,})",line)
            if marker:
                value=marker[1]
                if fence is None:fence=(value[0],len(value))
                elif value[0]==fence[0] and len(value)>=fence[1]:fence=None
                continue
            if fence is None:prose.append(line)
        require(fence is None,"Unclosed Markdown fence: "+str(path))
        for match in re.finditer(r"!?\[[^\]\r\n]*\]\((<[^>]+>|[^\s)]+)(?:\s+\"[^\"]*\")?\)","\n".join(prose)):
            target=match[1].strip("<>")
            if re.match(r"^(https?:|mailto:|app:|#)",target):continue
            target=re.sub(r":\d+(?::\d+)?$","",target.split("#")[0])
            counts["local_links"]+=1
            destination=Path(target) if re.match(r"^[A-Za-z]:",target) else path.parent/target
            require(destination.exists(),"Broken link "+target+" in "+str(path))
    return counts


def historical():
    pre=read(DOCS/"v6-preimplementation-manifest.json");archive=read(DOCS/"archive/2.2/manifest.json")
    raw={entry["original_path"]:safe(ROOT,entry["exact_copy_path"]) for entry in archive["files"]+archive.get("additional_files",[])}
    mirror=CACHE/"historical-pre-v6"
    mirror.mkdir(parents=True,exist_ok=True)
    results=[]
    for item in pre["files"]:
        source=raw.get(item["path"],safe(ROOT,item["path"]))
        require(hashlib.sha256(source.read_bytes()).hexdigest()==item["sha256"],"Cannot reconstruct historic source: "+item["path"])
        target=safe(mirror,item["path"]);target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
    commands=[
        ["node","deploy/dc-p5-docs-check.mjs"],
        ["node","deploy/dc-v5-docs-check.mjs"],
        ["node","docs/game-demo/contracts/planned-v5/check-semantics.mjs"],
        ["node","deploy/dc-docs-check.mjs"],
    ]
    for index,command in enumerate(commands,1):
        completed=subprocess.run(command,cwd=mirror,capture_output=True,text=True,encoding="utf-8",timeout=120)
        (CACHE/f"historical-check-{index}.log").write_text(completed.stdout+completed.stderr,encoding="utf-8")
        require(completed.returncode==0,"Historical checker failed: "+" ".join(command))
        results.append({"command":" ".join(command),"exit_code":completed.returncode,"scope":"archived_pre_v6_view_only"})
    return results


def main():
    parser=argparse.ArgumentParser();parser.add_argument("--historical",action="store_true");parser.add_argument("--final",action="store_true");args=parser.parse_args()
    CACHE.mkdir(parents=True,exist_ok=True)
    result={"schema_version":1,"status":"PASS_DOCUMENT_SEMANTICS_ONLY","scope":"documentation_only_not_game_runtime","semantics":semantics(),"geometry":geometry(),"geometry_negative_cases":negative_geometry(),"links":links_and_json(),"history":history(args.final),"runtime_tested":False,"human_tested":False}
    if args.historical:result["historical_checks"]=historical()
    report=CACHE/("final-check.json" if args.final else "historical-check.json" if args.historical else "semantic-check.json")
    report.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({key:result[key] for key in ["status","scope","semantics","links","history"]},ensure_ascii=False,indent=2))


if __name__=="__main__":main()
