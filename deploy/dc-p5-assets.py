import argparse
import hashlib
import json
import math
import random
import struct
import wave
from pathlib import Path

from PIL import Image, ImageDraw


PALETTE = ["#1B1C2C", "#343447", "#565568", "#89849A", "#C6BDAF", "#EEE5CD", "#47392F", "#80613D", "#B89B67", "#294F53", "#43877A", "#8DC4A0", "#45385C", "#8672AF", "#BC4B48", "#EB9A52"]
ACTORS = ["ranger", "runesmith", "guard", "archer", "charger", "elite", "boss"]
STATES = ["idle", "move", "prepare", "attack", "recover", "hurt", "death", "charge"]
FRAMES = {}
CANVAS = Image.new("RGBA", (1024, 1024))


def add(name, image, pivot=(0.5, 0.5)):
    index = len(FRAMES)
    horizontal, vertical = index % 32 * 32, index // 32 * 32
    if vertical + 32 > CANVAS.height:
        raise ValueError("atlas capacity")
    CANVAS.alpha_composite(image, (horizontal, vertical))
    FRAMES[name] = {"x": horizontal, "y": CANVAS.height-vertical-32, "width": 32, "height": 32, "pivot_x": pivot[0], "pivot_y": pivot[1]}


def actor(kind, direction, state, frame):
    image = Image.new("RGBA", (32, 32))
    draw = ImageDraw.Draw(image)
    outline, stone, steel, highlight, ivory, green, mint, purple, red, gold, amber = [PALETTE[index] for index in [0, 1, 2, 3, 5, 10, 11, 13, 14, 8, 15]]
    bob = (frame % 2) if state in ("idle", "move") else 0
    walk = [-2, 0, 2, 0][frame] if state == "move" else 0
    if state == "death":
        height = max(0, 3-frame)
        draw.ellipse((4, 23, 29, 29), fill=outline)
        draw.polygon([(6, 23-height), (11, 18-height), (23, 19-height), (28, 26), (12, 27)], fill=green if kind == "ranger" else PALETTE[12] if kind in ("archer", "boss") else stone, outline=steel)
        if kind in ("elite", "boss"):
            draw.polygon([(11, 20-height), (12, 14-height), (16, 17-height), (20, 13-height), (23, 19-height)], fill=gold, outline=outline)
        elif kind == "charger":
            draw.line((9, 23-height, 5, 17-height), fill=highlight, width=2)
            draw.line((22, 22-height, 27, 17-height), fill=highlight, width=2)
        elif kind == "runesmith":
            draw.rectangle((10, 18-height, 24, 21-height), fill=gold, outline=outline)
            draw.line((5, 28, 13, 23), fill=gold, width=2)
        elif kind == "archer":
            draw.arc((2, 17, 12, 29), 250, 100, fill=gold, width=2)
        elif kind == "guard":
            draw.line((4, 27, 18, 22), fill=highlight, width=2)
        draw.line((12, 23-height, 22, 24), fill=purple if kind == "boss" else mint if kind == "ranger" else red, width=2)
        if frame == 3 and kind not in ("ranger", "runesmith"):
            for vertical in range(32):
                for horizontal in range(32):
                    if (horizontal+vertical)%3 == 0:
                        image.putpixel((horizontal, vertical), (0, 0, 0, 0))
        return image
    if kind == "boss":
        draw.ellipse((3, 23, 29, 29), fill=outline)
        draw.polygon([(5, 11), (12, 7), (21, 7), (28, 12), (26, 24), (20, 28), (9, 27), (4, 22)], fill=outline)
        draw.polygon([(6, 12), (12, 8), (21, 8), (26, 13), (25, 22), (20, 26), (10, 25), (6, 21)], fill=stone, outline=steel)
        draw.polygon([(11, 11), (11, 4), (15, 6), (18, 2), (21, 6), (24, 4), (23, 12)], fill=gold, outline=outline)
        draw.rectangle((12, 10+bob, 22, 17+bob), fill=steel, outline=outline)
        draw.rectangle((13, 12+bob, 21, 13+bob), fill=amber if state in ("prepare", "attack") else purple)
        draw.polygon([(16, 18), (19, 16), (22, 20), (19, 24)], fill=amber if state == "charge" else purple)
        draw.polygon([(3, 12), (8, 9), (12, 12), (10, 17), (3, 17)], fill=steel, outline=outline)
        draw.polygon([(22, 10), (29, 12), (30, 18), (24, 17)], fill=steel, outline=outline)
        if state == "attack":
            draw.line((23, 19, 30, 13-frame*2), fill=gold, width=3)
            draw.line((24, 19, 31, 12-frame*2), fill=ivory, width=1)
        elif state == "recover":
            draw.line((23, 19, 30-frame, 8+frame*3), fill=gold, width=3)
        elif state == "hurt":
            draw.polygon([(16, 18), (19, 16), (22, 20), (19, 24)], fill=ivory)
        return image
    accent = green if kind == "ranger" else gold if kind == "runesmith" else purple if kind == "archer" else amber if kind == "charger" else red
    broad = kind in ("elite", "runesmith")
    left, right = (8, 24) if broad else (10, 22)
    draw.ellipse((left-1, 24, right+2, 29), fill=outline)
    draw.rectangle((left+1+walk, 23, left+5+walk, 27), fill=outline)
    draw.rectangle((right-5-walk, 23, right-1-walk, 27), fill=outline)
    draw.rectangle((left+2+walk, 24, left+5+walk, 26), fill=steel)
    draw.rectangle((right-4-walk, 24, right-1-walk, 26), fill=steel)
    if kind == "ranger":
        draw.polygon([(12, 11+bob), (21, 11+bob), (24, 25), (8, 25)], fill=outline)
        draw.polygon([(12, 12+bob), (20, 12+bob), (22, 23), (10, 23)], fill=green)
        draw.line((12, 14, 11, 22), fill=mint, width=1)
    else:
        draw.rectangle((left, 12+bob, right, 24), fill=outline)
        draw.rectangle((left+1, 13+bob, right-1, 22), fill=stone if kind != "runesmith" else PALETTE[7])
        draw.line((left+2, 15, right-2, 15), fill=highlight)
        draw.rectangle((14, 17, 19, 20), fill=accent, outline=outline)
    draw.rectangle((12, 6+bob, 22, 14+bob), fill=outline)
    draw.rectangle((13, 7+bob, 21, 13+bob), fill=gold if kind == "ranger" else steel)
    if kind == "ranger":
        draw.polygon([(10, 9+bob), (12, 4+bob), (19, 3+bob), (24, 8+bob), (23, 12+bob), (20, 7+bob), (13, 8+bob)], fill=green, outline=outline)
    elif kind == "runesmith":
        draw.rectangle((11, 4+bob, 23, 8+bob), fill=outline)
        draw.rectangle((12, 5+bob, 22, 7+bob), fill=gold)
        draw.rectangle((13, 6+bob, 21, 8+bob), fill=green)
        draw.rectangle((15, 12+bob, 21, 15+bob), fill=PALETTE[7])
    elif kind == "archer":
        draw.polygon([(10, 12), (12, 4+bob), (20, 3+bob), (24, 9), (22, 14), (20, 8), (14, 8)], fill=PALETTE[12], outline=outline)
    elif kind == "charger":
        draw.polygon([(11, 8), (8, 3), (14, 6), (22, 5), (25, 2), (24, 12), (12, 13)], fill=steel, outline=outline)
    elif kind == "elite":
        draw.polygon([(10, 8), (11, 3), (15, 4), (18, 1), (21, 5), (25, 4), (23, 12)], fill=gold, outline=outline)
        draw.rectangle((6, 12, 11, 18), fill=steel, outline=outline)
        draw.rectangle((22, 12, 27, 18), fill=steel, outline=outline)
    else:
        draw.polygon([(11, 8), (12, 4), (21, 4), (24, 9), (22, 13), (12, 13)], fill=steel, outline=outline)
    if direction != "up":
        eye_x = 19 if direction in ("right", "left") else 14
        draw.line((eye_x, 10+bob, eye_x+2, 10+bob), fill=ivory if kind in ("ranger", "runesmith") else accent, width=1)
        if direction == "down":
            draw.point((20, 10+bob), fill=ivory if kind in ("ranger", "runesmith") else accent)
    else:
        draw.line((13, 10+bob, 21, 10+bob), fill=accent)
    hand_y = 17 - (frame*2 if state in ("prepare", "charge") else 0)
    if kind in ("guard", "elite", "charger"):
        angle = [-1.4, -0.4, 0.5, 1.1][frame] if state == "attack" else [1.1, 0.5, -0.4, -1][frame] if state == "recover" else -1
        tip = (int(24+7*math.cos(angle)), int(hand_y+9*math.sin(angle)))
        draw.line((23, hand_y+2, *tip), fill=outline, width=4)
        draw.line((23, hand_y+2, *tip), fill=highlight, width=2)
    if kind == "archer":
        draw.arc((23, hand_y-6, 30, hand_y+6), 260, 100, fill=gold, width=2)
        pull = (3-frame) if state == "attack" else frame if state == "prepare" else 0
        draw.line((27, hand_y-5, 27-pull, hand_y, 27, hand_y+5), fill=ivory)
        if state == "attack" and frame < 2:
            draw.line((26, hand_y, 31, hand_y), fill=amber, width=2)
    draw.rectangle((right-1, hand_y, right+2, hand_y+3), fill=gold, outline=outline)
    if state == "hurt":
        draw.line((left-2, 9, left-5, 6), fill=ivory)
        for vertical in range(32):
            for horizontal in range(32):
                pixel = image.getpixel((horizontal, vertical))
                if pixel[3] and pixel[:3] != tuple(bytes.fromhex(outline[1:])):
                    image.putpixel((horizontal, vertical), tuple(bytes.fromhex(ivory[1:]))+(255,))
    if direction == "left":
        image = image.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    return image


def icon(name, index):
    image = Image.new("RGBA", (32, 32))
    draw = ImageDraw.Draw(image)
    dark, mid, pale, gold, teal, red = [PALETTE[value] for value in [0, 2, 5, 8, 10, 14]]
    if name in ("short_blade", "repeating_crossbow", "rune_hammer", "rune_bolt"):
        if name == "short_blade":
            draw.polygon([(9, 23), (13, 16), (25, 4), (23, 12), (15, 20)], fill=pale, outline=dark)
            draw.line((8, 20, 16, 24), fill=gold, width=3)
            draw.line((7, 28, 12, 22), fill=PALETTE[7], width=3)
        elif name == "repeating_crossbow":
            draw.line((10, 26, 22, 8), fill=PALETTE[7], width=5)
            draw.arc((4, 2, 29, 22), 190, 350, fill=gold, width=3)
            draw.line((5, 11, 27, 11), fill=teal, width=1)
            draw.line((17, 19, 23, 4), fill=pale, width=1)
        elif name == "rune_hammer":
            draw.line((10, 29, 20, 11), fill=PALETTE[7], width=4)
            draw.polygon([(11, 4), (24, 5), (29, 12), (21, 18), (8, 13)], fill=mid, outline=dark)
            draw.line((12, 6, 23, 7), fill=gold, width=2)
            draw.rectangle((16, 9, 21, 12), fill=teal)
        else:
            draw.polygon([(16, 3), (26, 13), (16, 27), (6, 13)], fill=teal, outline=dark)
            draw.line((16, 6, 21, 13, 16, 22, 11, 13, 16, 6), fill=pale, width=2)
    elif name in ("health", "rescue"):
        draw.polygon([(5, 10), (9, 6), (14, 6), (16, 9), (19, 6), (24, 6), (28, 10), (27, 17), (16, 28), (5, 17)], fill=red, outline=dark)
        draw.line((9, 11, 13, 11), fill=pale, width=2)
        if name == "rescue":
            draw.rectangle((14, 12, 18, 23), fill=pale)
            draw.rectangle((10, 16, 22, 19), fill=pale)
    elif name in ("ward", "shield", "field"):
        draw.polygon([(5, 7), (16, 3), (27, 7), (25, 20), (16, 29), (7, 20)], fill=teal, outline=gold)
        draw.line((16, 7, 16, 23), fill=pale, width=2)
        if name == "field":
            draw.arc((1, 17, 31, 30), 0, 180, fill=pale, width=2)
        elif name == "ward":
            draw.line((10, 14, 22, 14), fill=pale, width=2)
    elif name == "wind":
        draw.arc((4, 3, 28, 25), 170, 520, fill=teal, width=3)
        draw.arc((9, 7, 24, 21), 0, 285, fill=pale, width=2)
        draw.line((4, 15, 1, 12, 1, 18), fill=gold, width=2)
    elif name == "frost":
        for angle in range(3):
            radians = angle*math.pi/3
            delta = (round(12*math.cos(radians)), round(12*math.sin(radians)))
            draw.line((16-delta[0], 16-delta[1], 16+delta[0], 16+delta[1]), fill=pale, width=2)
        draw.ellipse((12, 12, 20, 20), fill=teal, outline=gold)
    elif name == "vitality":
        draw.rectangle((4, 5, 27, 28), fill=PALETTE[6], outline=gold)
        draw.rectangle((13, 9, 18, 24), fill=pale)
        draw.rectangle((8, 14, 23, 19), fill=pale)
    elif name == "ranger_pierce":
        draw.ellipse((5, 9, 14, 23), outline=teal, width=2)
        draw.ellipse((18, 9, 27, 23), outline=teal, width=2)
        draw.line((1, 16, 30, 16, 25, 12), fill=pale, width=2)
        draw.line((30, 16, 25, 20), fill=pale, width=2)
    elif name in ("volley", "ranger_fan"):
        for offset in (-1, 0, 1):
            tip = (16+offset*(10 if name == "ranger_fan" else 7), 5+abs(offset)*3)
            draw.line((16+offset*3, 27, *tip), fill=pale, width=2)
            draw.line((tip[0]-3, tip[1]+5, *tip, tip[0]+3, tip[1]+5), fill=gold, width=2)
    elif name == "runesmith_chain":
        draw.line((7, 24, 16, 15, 25, 6), fill=pale, width=2)
        for horizontal, vertical in ((7, 24), (16, 15), (25, 6)):
            draw.ellipse((horizontal-4, vertical-4, horizontal+4, vertical+4), fill=teal, outline=gold)
    elif name == "runesmith_twin":
        for horizontal in (8, 23):
            draw.rectangle((horizontal-5, 15, horizontal+5, 27), fill=mid, outline=gold)
            draw.rectangle((horizontal-2, 6, horizontal+2, 18), fill=pale, outline=dark)
    elif name == "runesmith_heavy":
        draw.polygon([(7, 12), (12, 6), (20, 6), (26, 12), (29, 28), (3, 28)], fill=mid, outline=gold)
        draw.line((16, 10, 16, 23, 11, 19, 16, 23, 21, 19), fill=pale, width=2)
    else:
        points = [(int(16+12*math.cos(angle*math.pi/3)), int(16+12*math.sin(angle*math.pi/3))) for angle in range(6)]
        draw.polygon(points, fill=PALETTE[9], outline=gold)
        if name in ("turret", "runesmith_twin", "runesmith_heavy"):
            draw.rectangle((9, 14, 24, 23), fill=mid, outline=dark)
            draw.rectangle((14, 7, 19, 17), fill=pale, outline=dark)
            draw.line((10, 25, 24, 25), fill=gold, width=2)
        elif name in ("swift", "dodge", "ranger_tension"):
            draw.line((8, 13, 14, 13, 12, 22, 25, 12, 18, 12, 20, 7), fill=pale, width=3)
            if name == "ranger_tension":
                draw.rectangle((5, 24, 27, 28), fill=gold, outline=dark)
                for horizontal in (9, 15, 21):
                    draw.line((horizontal, 25, horizontal, 27), fill=dark)
        else:
            for ray in range(2+index%4):
                horizontal = 9 + ray*4
                draw.line((horizontal, 24, horizontal+3, 9), fill=pale, width=2)
                draw.line((horizontal+1, 11, horizontal+3, 9, horizontal+4, 13), fill=gold, width=1)
    return image


def terrain(name, variant):
    image = Image.new("RGBA", (32, 32))
    draw = ImageDraw.Draw(image)
    rng = random.Random(2400+variant)
    if name == "floor":
        draw.rectangle((0, 0, 31, 31), fill=PALETTE[1])
        for vertical in range(0, 32, 16):
            for horizontal in range(0, 32, 16):
                draw.rectangle((horizontal+1, vertical+1, horizontal+14, vertical+14), fill=PALETTE[0])
                draw.line((horizontal+2, vertical+14, horizontal+14, vertical+14, horizontal+14, vertical+2), fill=PALETTE[1])
                for unused in range(3):
                    draw.point((horizontal+rng.randrange(3, 13), vertical+rng.randrange(3, 13)), fill=PALETTE[1])
        if variant == 2:
            draw.line((13, 12, 18, 12, 18, 19, 14, 19), fill=PALETTE[9], width=1)
    elif name == "wall":
        draw.rectangle((0, 0, 31, 31), fill=PALETTE[0])
        for vertical in range(0, 32, 8):
            offset = 0 if vertical%16 else -8
            for horizontal in range(offset, 32, 16):
                draw.rectangle((horizontal+1, vertical+1, horizontal+15, vertical+7), fill=PALETTE[2])
                draw.line((horizontal+2, vertical+2, horizontal+13, vertical+2), fill=PALETTE[3])
                draw.line((horizontal+2, vertical+7, horizontal+14, vertical+7), fill=PALETTE[1])
    elif name == "cover":
        draw.rectangle((3, 6, 28, 29), fill=PALETTE[6], outline=PALETTE[0])
        draw.polygon([(3, 6), (9, 2), (30, 2), (28, 6)], fill=PALETTE[8], outline=PALETTE[0])
        draw.line((7, 7, 7, 27, 26, 27, 26, 7), fill=PALETTE[7], width=2)
        draw.line((5, 10, 26, 25), fill=PALETTE[8], width=2)
        draw.rectangle((12, 12, 20, 20), fill=PALETTE[9], outline=PALETTE[0])
    elif name == "mechanism":
        draw.polygon([(5, 9), (16, 2), (27, 9), (29, 24), (16, 30), (3, 24)], fill=PALETTE[2], outline=PALETTE[0])
        draw.polygon([(16, 5), (23, 15), (16, 24), (9, 15)], fill=PALETTE[9], outline=PALETTE[11])
        draw.rectangle((14, 11, 18, 19), fill=PALETTE[10])
    elif name == "trap":
        draw.ellipse((2, 4, 30, 29), fill=PALETTE[1], outline=PALETTE[3])
        for horizontal in (9, 16, 23):
            draw.polygon([(horizontal-3, 23), (horizontal, 8 if variant else 17), (horizontal+3, 23)], fill=PALETTE[15] if variant else PALETTE[2], outline=PALETTE[0])
    return image


def audio(target):
    sounds = ["shot", "hit", "hurt", "dodge", "skill", "light", "heavy", "charge", "clear", "reload", "interact", "warning", "downed", "rescue", "death", "boss", "success", "failure", "music", "shot_crossbow", "shot_rune", "turret_shot"]
    records = []
    rate = 24000
    for index, name in enumerate(sounds):
        duration = 32 if name == "music" else 0.65 if name in ("success", "failure", "boss", "heavy") else 0.16
        rng = random.Random(1400+index)
        samples = []
        for sample in range(int(rate*duration)):
            time = sample/rate
            if name == "music":
                chord = [110, 130.8128, 146.8324, 164.8138][int(time/8)%4]
                envelope = math.sin(math.pi*(time%8)/8)**2
                signal = (math.sin(math.tau*chord*time)+0.3*math.sin(math.tau*chord*1.5*time)+0.15*math.sin(math.tau*chord*2*time))*envelope*0.08
            else:
                frequency = 110+index*39
                phase = math.tau*(frequency*time+(120 if name in ("dodge", "shot") else -35)*time*time)
                envelope = max(0, 1-time/duration)**2*min(1, time/0.004)
                noise = rng.uniform(-1, 1)*(0.5 if name in ("hurt", "heavy", "hit", "shot") else 0.08)
                signal = (math.sin(phase)*0.6+math.sin(phase*2)*0.1+noise)*envelope*0.3
            samples.append(struct.pack("<h", round(max(-1, min(1, signal))*32767)))
        path = target / (name+".wav")
        with wave.open(str(path), "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(rate)
            output.writeframes(b"".join(samples))
        records.append({"asset_id": "audio."+name, "path": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "duration_seconds": duration})
    return records


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent)
    arguments = parser.parse_args()
    root = arguments.project.resolve()
    target = root / "client/unity-demo/Assets/Resources/DungeonContract/P5"
    target.mkdir(parents=True, exist_ok=True)
    for kind in ACTORS:
        for direction in ("down", "up", "left", "right"):
            for state in STATES:
                for frame in range(4):
                    add("actor."+kind+"."+direction+"."+state+"."+str(frame), actor(kind, direction, state, frame), (0.5, 0.15625))
    icons = ["short_blade", "repeating_crossbow", "rune_hammer", "rune_bolt", "health", "shield", "rescue", "dodge", "volley", "wind", "turret", "field", "swift", "frost", "ward", "vitality", "ranger_pierce", "ranger_fan", "ranger_tension", "runesmith_chain", "runesmith_twin", "runesmith_heavy"]
    for index, name in enumerate(icons):
        add("icon."+name, icon(name, index))
    for name, count in (("floor", 3), ("wall", 1), ("cover", 1), ("mechanism", 1), ("trap", 2)):
        for variant in range(count):
            add("terrain."+name+"."+str(variant), terrain(name, variant))
    CANVAS.save(target / "ruins-p5.png", optimize=True)
    atlas = {"schema_version": 1, "style_version": "ruins.original.p5.1", "pixels_per_unit": 16, "palette": PALETTE, "frames": FRAMES}
    (target / "ruins-p5.json").write_text(json.dumps(atlas, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    records = audio(target)
    for filename in ("ruins-p5.png", "ruins-p5.json"):
        records.append({"asset_id": filename, "path": filename, "sha256": hashlib.sha256((target / filename).read_bytes()).hexdigest()})
    manifest = {"schema_version": 1, "presentation_version": "p5.1", "method": "original_deterministic_pixel_drawing_and_audio_synthesis", "ai_generated": False, "license": "CC0-1.0", "license_file": "LICENSE.txt", "source": "deploy/dc-p5-assets.py", "seed": "2400/1400", "review_status": "candidate", "frame_count": len(FRAMES), "audio_peak_limit": 1, "assets": records}
    (target / "provenance.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"frames": len(FRAMES), "audio": len(records)-2, "directory": str(target)}))


if __name__ == "__main__":
    main()
