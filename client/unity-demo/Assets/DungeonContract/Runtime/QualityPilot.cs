using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class QualityPilot
    {
        private static JObject definition;
        public static void Configure(JObject world) { definition = world; }
        private static JToken Own(JObject snapshot, string id) => ((JArray)snapshot["players"]).First(player => (string)player["player_id"] == id);
        private static Vector2 Point(JToken item) => new Vector2((float)item["x"], (float)item["y"]);

        public static GrayboxInput Decide(JObject snapshot, string id, bool defeat = false)
        {
            var own = Own(snapshot, id); if ((int)own["health"] <= 0 || (int)snapshot["safe_node"] >= 0 || definition == null) return default;
            var room = (int)own["room_id"]; var position = Point(own); var layout = (JObject)definition["rooms"][room];
            var enemies = ((JArray)snapshot["enemies"]).Where(enemy => (int)enemy["room_id"] == room).OrderBy(enemy => Vector2.Distance(Point(enemy), position)).ToArray();
            var target = enemies.FirstOrDefault(); var aim = target == null ? Vector2.right : Point(target) - position; var destination = position; var interact = false;
            var doorDirection = Vector2.zero;
            if (target != null)
            {
                if (defeat) destination = Point(target);
                else
                {
                    var center = new Vector2((int)layout["width"] / 2f, (int)layout["height"] / 2f); var offset = position - center;
                    var orbit = new Vector2(-offset.y, offset.x).normalized;
                    var away = Vector2.Distance(position, Point(target)) < 4 ? (position - Point(target)).normalized : Vector2.zero;
                    destination = position + orbit * 2 + away * 2;
                    destination.x = Mathf.Clamp(destination.x, 2, (int)layout["width"] - 2); destination.y = Mathf.Clamp(destination.y, 2, (int)layout["height"] - 2);
                    if (Vector2.Distance(position, destination) < 0.1f) destination = center;
                }
            }
            else if (layout["objective"] is JArray && !(bool)snapshot["rooms"][room]["mechanism_complete"] && (string)snapshot["rooms"][room]["status"] == "active")
            {
                destination = RoomGeometry.Point(layout["objective"]); interact = Vector2.Distance(position, destination) < 0.8f;
            }
            else if ((string)snapshot["rooms"][room]["status"] == "cleared")
            {
                var door = ((JArray)definition["doors"]).FirstOrDefault(item => (int)item["from"] == room && (int)item["to"] < 6);
                if (door != null) { var side = (string)door["side"]; destination = RoomGeometry.DoorPoint(layout, side); if (Vector2.Distance(position, destination) < 0.7f) doorDirection = side == "east" ? Vector2.right : side == "north" ? Vector2.up : Vector2.down; }
            }
            var move = interact ? Vector2.zero : doorDirection.sqrMagnitude > 0 ? doorDirection : RoomGeometry.PathDirection(layout, position, destination, 0.3f);
            if (target != null && !RoomGeometry.ClearLine(layout, position, Point(target))) move = RoomGeometry.PathDirection(layout, position, Point(target), 0.3f);
            var angle = Mathf.Atan2(aim.y, aim.x); if (angle < 0) angle += Mathf.PI * 2;
            return new GrayboxInput { Move = move, Aim = (ushort)(angle / (Mathf.PI * 2) * 65535), Attack = target != null && (!defeat || (long)own["damage"] == 0), Interact = interact };
        }

        public static string Action(JObject snapshot, string id)
        {
            var own = Own(snapshot, id); if ((int)own["health"] <= 0) return null;
            if ((int)snapshot["safe_node"] >= 0) return own["choice"].Type == JTokenType.Null ? "choose:" + own["candidates"][0] : null;
            var target = ((JArray)snapshot["enemies"]).FirstOrDefault(enemy => (int)enemy["room_id"] == (int)own["room_id"]);
            if (target == null) return null;
            if ((long)own["dodge_remaining_ms"] == 0 && (string)target["warning"] != "") return "dodge";
            return (long)own["ability_remaining_ms"] == 0 ? "ability" : null;
        }

        public static Vector2 Placement(JObject snapshot, string id)
        {
            var own = Own(snapshot, id); var origin = Point(own); if (definition == null) return origin + Vector2.right;
            var room = (int)own["room_id"]; var layout = (JObject)definition["rooms"][room];
            foreach (var offset in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down }) { var point = origin + offset; if (!RoomGeometry.Blocked(layout, point, 0.3f) && !((JArray)snapshot["enemies"]).Any(enemy => (int)enemy["room_id"] == room && Vector2.Distance(Point(enemy), point) < 1)) return point; }
            return origin;
        }
    }
}
