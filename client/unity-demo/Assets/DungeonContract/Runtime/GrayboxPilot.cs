using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public static class GrayboxPilot
    {
        private static readonly Dictionary<string, int> corners = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> orbitRooms = new Dictionary<string, int>();
        private static readonly Dictionary<string, long> previousTimes = new Dictionary<string, long>();

        public static GrayboxInput Decide(JObject snapshot, string id)
        {
            if ((int?)snapshot["schema_version"] == 2) return QualityPilot.Decide(snapshot, id);
            var own = Own(snapshot, id); if (own == null || (int)own["health"] == 0) return default;
            var position = Point(own); var room = (int)(position.x / 16);
            var enemies = ((JArray)snapshot["enemies"]).Where(item => (int)((float)item["x"] / 16) == room).OrderBy(item => Vector2.Distance(Point(item), position)).ToArray();
            var target = enemies.FirstOrDefault(); var move = Vector2.zero; var attack = false; var interact = false; var aim = Vector2.right;
            if (target != null)
            {
                aim = Point(target) - position; attack = true;
                var time = (long)snapshot["elapsed_ms"];
                if (!orbitRooms.TryGetValue(id, out var priorRoom) || priorRoom != room || previousTimes.TryGetValue(id, out var priorTime) && time < priorTime) { corners[id] = 0; orbitRooms[id] = room; }
                previousTimes[id] = time;
                var route = new[] { new Vector2(room * 16 + 3, 9), new Vector2(room * 16 + 13, 9), new Vector2(room * 16 + 13, 3), new Vector2(room * 16 + 3, 3) };
                if (Vector2.Distance(position, route[corners[id]]) < 0.6f) corners[id] = (corners[id] + 1) % route.Length;
                move = (route[corners[id]] - position).normalized;
            }
            else if ((room == 2 || room == 3) && !((bool)snapshot["completed"][room]))
            {
                var point = new Vector2(room * 16 + 8, 6);
                if (Vector2.Distance(position, point) < 0.5f) interact = true; else move = (point - position).normalized;
            }
            else if (room < (int)snapshot["room"])
            {
                var door = new Vector2((room + 1) * 16 + 1, 6);
                move = (door - position).normalized;
            }
            var angle = Mathf.Atan2(aim.y, aim.x); if (angle < 0) angle += Mathf.PI * 2;
            return new GrayboxInput { Move = move, Aim = (ushort)(angle / (Mathf.PI * 2) * 65535), Attack = attack, Interact = interact };
        }

        public static string Action(JObject snapshot, string id)
        {
            if (snapshot == null) return null;
            if ((int?)snapshot["schema_version"] == 2) return QualityPilot.Action(snapshot, id);
            var own = Own(snapshot, id); if (own == null || (int)own["health"] == 0) return null;
            var position = Point(own);
            if ((int)own["health"] <= 70 && !(bool)own["supply_used"] && Vector2.Distance(position, new Vector2(51, 3)) <= 1.5f) return "supply";
            var target = ((JArray)snapshot["enemies"]).FirstOrDefault(item => Vector2.Distance(position, Point(item)) < 7);
            if (target == null) return null;
            if ((long)own["dodge_remaining_ms"] == 0 && ((string)target["warning"] == "charge" || (string)target["warning"] == "stomp" || Vector2.Distance(position, Point(target)) < 1.8f)) return "dodge";
            return (long)own["ability_remaining_ms"] == 0 ? "ability" : null;
        }

        public static GrayboxInput DefeatDecide(JObject snapshot, string id)
        {
            if ((int?)snapshot["schema_version"] == 2) return QualityPilot.Decide(snapshot, id, true);
            var own = Own(snapshot, id); if (own == null || (int)own["health"] == 0) return default;
            var position = Point(own); var destination = new Vector2(23, 6);
            var target = ((JArray)snapshot["enemies"]).OrderBy(item => Vector2.Distance(position, Point(item))).FirstOrDefault();
            var aim = target == null ? Vector2.right : Point(target) - position;
            var angle = Mathf.Atan2(aim.y, aim.x); if (angle < 0) angle += Mathf.PI * 2;
            return new GrayboxInput { Move = Vector2.Distance(position, destination) > 0.4f ? (destination - position).normalized : Vector2.zero, Aim = (ushort)(angle / (Mathf.PI * 2) * 65535), Attack = target != null && (long?)own["damage"] == 0 };
        }

        private static JToken Own(JObject snapshot, string id) => snapshot == null ? null : ((JArray)snapshot["players"]).FirstOrDefault(item => (string)item["player_id"] == id);
        private static Vector2 Point(JToken item) => new Vector2((float)item["x"], (float)item["y"]);
    }
}
