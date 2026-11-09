using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonContract
{
    public sealed class DcGrayboxView : MonoBehaviour
    {
        private QualityView quality;
        public Action<string> SelectChoice { set { if (quality != null) quality.SelectChoice = value; } }
        public void ReadLocalInput(bool blocked) { quality?.ReadLocalInput(blocked); }
        public void RecordInput(GrayboxInput input, bool blocked) { quality?.RecordInput(input, blocked); }
        public void PredictDodge() { quality?.PredictDodge(); }
        public void AttackStart() { quality?.AttackStart(); }
        public void ActionFeedback(bool accepted, string reason) { quality?.ActionFeedback(accepted, reason); }
        public Vector2 MouseWorld() => quality != null ? quality.MouseWorld() : cameraView == null ? Vector2.zero : (Vector2)cameraView.ScreenToWorldPoint(Input.mousePosition);
        private Camera cameraView;
        private string ownPlayer;
        private JObject snapshot;
        private Tilemap walls;
        private Tile floorTile;
        private Tile wallTile;
        private readonly Dictionary<string, SpriteRenderer> entities = new Dictionary<string, SpriteRenderer>();
        private readonly Dictionary<string, Vector2> targets = new Dictionary<string, Vector2>();
        private GameObject renderRoot;
        private Sprite sprite;
        private Font font;
        private int visibleRoom = -1;
        private GameObject lobbyCamera;
        private float smoothing = 10;
        private int maximumHealth = 100;

        private void Awake()
        {
            var installed = Font.GetOSInstalledFontNames();
            font = Font.CreateDynamicFontFromOSFont(installed.Contains("Microsoft YaHei") ? "Microsoft YaHei" : installed.FirstOrDefault() ?? "Arial", 18);
            CreateLobbyCamera();
        }

        private void CreateLobbyCamera()
        {
            lobbyCamera = new GameObject("Lobby camera"); lobbyCamera.transform.SetParent(transform);
            var lobbyView = lobbyCamera.AddComponent<Camera>(); lobbyView.clearFlags = CameraClearFlags.SolidColor;
            lobbyView.backgroundColor = new Color(0.06f, 0.07f, 0.11f); lobbyView.transform.position = new Vector3(0, 0, -10);
        }

        public void ApplySkin() { if (font != null) GUI.skin.font = font; }

        public void Configure(JObject manifest, string player)
        {
            Clear();
            if ((int?)manifest["config"]["protocol_version"] == 2)
            {
                if (lobbyCamera != null) Destroy(lobbyCamera);
                quality = gameObject.AddComponent<QualityView>(); quality.Configure(manifest, player); return;
            }
            ownPlayer = player;
            maximumHealth = (int)manifest["config"]["player"]["max_health"];
            var interpolation = (int)manifest["p1_world"]["view"]["interpolation_ms"]; smoothing = interpolation == 0 ? 1000 : 1000f / interpolation;
            if (lobbyCamera != null) Destroy(lobbyCamera);
            if (renderRoot != null) Destroy(renderRoot);
            entities.Clear(); targets.Clear();
            renderRoot = new GameObject("P1 graybox presentation");
            renderRoot.transform.SetParent(transform);
            cameraView = new GameObject("Player camera").AddComponent<Camera>();
            cameraView.transform.SetParent(renderRoot.transform); cameraView.orthographic = true; cameraView.orthographicSize = (int)manifest["p1_world"]["view"]["camera_size_milli"] / 1000f;
            cameraView.backgroundColor = new Color(0.06f, 0.07f, 0.11f); cameraView.clearFlags = CameraClearFlags.SolidColor;
            cameraView.transform.position = new Vector3(9, 6, -10);
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixels(Enumerable.Repeat(Color.white, 256).ToArray()); texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            floorTile = ScriptableObject.CreateInstance<Tile>(); floorTile.sprite = sprite; floorTile.color = new Color(0.16f, 0.19f, 0.24f);
            wallTile = ScriptableObject.CreateInstance<Tile>(); wallTile.sprite = sprite; wallTile.color = new Color(0.33f, 0.36f, 0.44f);
            var grid = new GameObject("Main route grid").AddComponent<Grid>(); grid.transform.SetParent(renderRoot.transform);
            var groundObject = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer)); groundObject.transform.SetParent(grid.transform);
            var ground = groundObject.GetComponent<Tilemap>(); ground.GetComponent<TilemapRenderer>().sortingOrder = -20;
            var wallObject = new GameObject("Walls", typeof(Tilemap), typeof(TilemapRenderer)); wallObject.transform.SetParent(grid.transform); walls = wallObject.GetComponent<Tilemap>(); walls.GetComponent<TilemapRenderer>().sortingOrder = -10;
            for (var horizontal = 0; horizontal < 96; horizontal++) for (var vertical = 0; vertical < 12; vertical++)
            {
                ground.SetTile(new Vector3Int(horizontal, vertical, 0), floorTile);
                if (horizontal == 0 || horizontal == 95 || vertical == 0 || vertical == 11 || horizontal % 16 == 0 && vertical != 5 && vertical != 6) walls.SetTile(new Vector3Int(horizontal, vertical, 0), wallTile);
            }
            DrawEntity("hub_a", new Vector2(40, 6), new Color(0.95f, 0.74f, 0.3f), 0.8f);
            DrawEntity("hub_b", new Vector2(56, 6), new Color(0.95f, 0.74f, 0.3f), 0.8f);
            DrawEntity("supply", new Vector2(51, 3), new Color(0.35f, 0.9f, 0.8f), 0.7f);
        }

        public void Receive(JObject incoming)
        {
            if (quality != null) { quality.Receive(incoming); return; }
            snapshot = incoming;
            var present = new HashSet<string> { "hub_a", "hub_b", "supply" };
            foreach (var player in (JArray)incoming["players"])
            {
                var id = "player:" + player["player_id"]; present.Add(id);
                DrawEntity(id, Point(player), (int)player["health"] == 0 ? Color.gray : (string)player["loadout_id"] == "ranger" ? new Color(0.3f, 0.85f, 0.45f) : new Color(0.25f, 0.7f, 0.9f), 0.6f);
            }
            foreach (var enemy in (JArray)incoming["enemies"])
            {
                var id = "enemy:" + enemy["id"]; present.Add(id); var kind = (string)enemy["kind"];
                DrawEntity(id, Point(enemy), kind == "boss" ? new Color(0.75f, 0.3f, 0.85f) : kind == "elite" ? new Color(1f, 0.65f, 0.3f) : kind == "archer" ? new Color(0.9f, 0.45f, 0.5f) : kind == "charger" ? new Color(0.85f, 0.25f, 0.15f) : new Color(0.7f, 0.35f, 0.3f), kind == "boss" ? 1.8f : kind == "elite" ? 1.2f : 0.8f);
                if ((string)enemy["warning"] != "") { var warningID = "warning:" + enemy["id"]; present.Add(warningID); DrawEntity(warningID, Point(enemy), new Color(1, 0.3f, 0.1f, 0.3f), (string)enemy["warning"] == "stomp" ? 6 : 1.8f); }
            }
            foreach (var projectile in (JArray)incoming["projectiles"]) { var id = "projectile:" + projectile["id"]; present.Add(id); DrawEntity(id, Point(projectile), (bool)projectile["enemy"] ? new Color(1, 0.5f, 0.2f) : new Color(0.6f, 0.9f, 1), 0.2f); }
            foreach (var turret in (JArray)incoming["turrets"]) { var id = "turret:" + turret["id"]; present.Add(id); DrawEntity(id, Point(turret), new Color(0.3f, 0.75f, 0.85f), 0.65f); }
            foreach (var id in entities.Keys.Where(id => !present.Contains(id)).ToArray()) { Destroy(entities[id].gameObject); entities.Remove(id); targets.Remove(id); }
            var room = (int)incoming["room"];
            if (room != visibleRoom) { visibleRoom = room; for (var door = 16; door < 96; door += 16) for (var row = 5; row <= 6; row++) walls.SetTile(new Vector3Int(door, row, 0), door / 16 > room ? wallTile : null); }
        }

        public void Clear(bool createLobby = true)
        {
            if (quality != null) { Destroy(quality); quality = null; }
            if (renderRoot != null) Destroy(renderRoot);
            if (floorTile != null) Destroy(floorTile);
            if (wallTile != null) Destroy(wallTile);
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
            renderRoot = null; floorTile = null; wallTile = null; sprite = null; snapshot = null;
            entities.Clear(); targets.Clear(); cameraView = null; visibleRoom = -1;
            if (createLobby && lobbyCamera == null) CreateLobbyCamera();
        }

        private void OnDestroy() { Clear(false); if (font != null) Destroy(font); }

        private void DrawEntity(string id, Vector2 position, Color color, float scale)
        {
            if (!entities.TryGetValue(id, out var renderer)) { renderer = new GameObject(id).AddComponent<SpriteRenderer>(); renderer.transform.SetParent(renderRoot.transform); renderer.sprite = sprite; renderer.sortingOrder = id.StartsWith("warning:") ? -1 : 5; renderer.transform.position = position; entities[id] = renderer; }
            renderer.color = color; renderer.transform.localScale = new Vector3(scale, scale, 1); targets[id] = position;
        }

        private void LateUpdate()
        {
            foreach (var target in targets) entities[target.Key].transform.position = Vector3.Lerp(entities[target.Key].transform.position, new Vector3(target.Value.x, target.Value.y, 0), Mathf.Clamp01(Time.unscaledDeltaTime * smoothing));
            if (cameraView != null && entities.TryGetValue("player:" + ownPlayer, out var own)) { var point = own.transform.position; cameraView.transform.position = new Vector3(Mathf.Clamp(point.x, 10, 86), 6, -10); }
        }

        public ushort MouseAim()
        {
            if (quality != null) return quality.MouseAim();
            if (cameraView == null || !targets.TryGetValue("player:" + ownPlayer, out var position)) return 0;
            var mouse = cameraView.ScreenToWorldPoint(Input.mousePosition); var direction = (Vector2)mouse - position; var angle = Mathf.Atan2(direction.y, direction.x); if (angle < 0) angle += Mathf.PI * 2;
            return (ushort)(angle / (Mathf.PI * 2) * 65535);
        }

        public bool PointerOverPanel() => quality != null ? quality.PointerOverPanel() : Input.mousePosition.y > Screen.height - 100 || Input.mousePosition.y < 95;

        public void DrawHUD(string status, string result)
        {
            if (quality != null) { quality.ShowStatus(status, result); return; }
            if (font != null) GUI.skin.font = font;
            if (snapshot == null) { GUI.Label(new Rect(20, 20, 600, 30), "正在接收世界快照 · " + status); return; }
            var own = ((JArray)snapshot["players"]).FirstOrDefault(item => (string)item["player_id"] == ownPlayer);
            GUI.Box(new Rect(12, 12, Screen.width - 24, 80), "");
            GUI.Label(new Rect(25, 20, 750, 28), "Dungeon Contract · 灰盒       " + ((long)snapshot["elapsed_ms"] / 1000) + "秒       开放区域 " + snapshot["room"] + " / 5");
            GUI.Label(new Rect(25, 50, 1050, 30), "生命 " + own?["health"] + " / " + maximumHealth + "     闪避 " + Cooldown(own?["dodge_remaining_ms"]) + "     技能 " + Cooldown(own?["ability_remaining_ms"]) + "     增援 " + snapshot["reinforcement_remaining"] + "     " + status);
            GUI.Box(new Rect(12, Screen.height - 75, Screen.width - 24, 63), "WASD 移动 · 鼠标瞄准 · 左键射击 · Space闪避 · Q技能 · E持续机关 · F补给 · Esc菜单");
            if ((long)snapshot["boss_warning_remaining_ms"] > 0) GUI.Box(new Rect(Screen.width / 2 - 180, 115, 360, 50), "守卫苏醒预警：" + Cooldown(snapshot["boss_warning_remaining_ms"]));
            if (own != null && (long)own["interaction_remaining_ms"] > 0) GUI.Box(new Rect(Screen.width / 2 - 170, Screen.height - 155, 340, 45), "保持 E，机关操作剩余 " + Cooldown(own["interaction_remaining_ms"]));
            if (own != null && (int)own["health"] == 0) GUI.Box(new Rect(Screen.width / 2 - 200, 190, 400, 60), "已死亡 · 等待共享增援确认，资源不足时观战");
            if (result != null) { GUI.Box(new Rect(100, 120, Screen.width - 200, 190), "作战结果"); GUI.Label(new Rect(120, 150, Screen.width - 240, 120), result); }
            else if ((string)snapshot["run_status"] == "ending") GUI.Box(new Rect(150, 150, Screen.width - 300, 90), "作战结束，正在可靠确认结果");
            if (cameraView != null) { var cursor = Input.mousePosition; GUI.Label(new Rect(cursor.x - 7, Screen.height - cursor.y - 9, 18, 20), "+"); }
            if (cameraView != null)
            {
                foreach (var enemy in (JArray)snapshot["enemies"])
                {
                    var point = cameraView.WorldToScreenPoint(new Vector3((float)enemy["x"], (float)enemy["y"], 0));
                    if (point.x < 0 || point.x > Screen.width || point.y < 80 || point.y > Screen.height - 90) continue;
                    var name = (string)enemy["kind"] == "boss" ? "守卫" : (string)enemy["kind"] == "elite" ? "精英" : (string)enemy["kind"] == "charger" ? "冲锋" : (string)enemy["kind"] == "archer" ? "射手" : "近战";
                    GUI.Label(new Rect(point.x - 45, Screen.height - point.y - 42, 100, 23), name + " " + enemy["health"]);
                }
                foreach (var marker in new[] { new { Position = new Vector2(40, 6), Name = "枢纽 A · E" }, new { Position = new Vector2(56, 6), Name = "枢纽 B · E" }, new { Position = new Vector2(51, 3), Name = "一次补给 · F" } })
                {
                    var point = cameraView.WorldToScreenPoint(marker.Position);
                    if (point.x > 0 && point.x < Screen.width && point.y > 80 && point.y < Screen.height - 90) GUI.Label(new Rect(point.x - 65, Screen.height - point.y + 30, 170, 24), marker.Name);
                }
            }
        }

        private static Vector2 Point(JToken item) => new Vector2((float)item["x"], (float)item["y"]);
        private static string Cooldown(JToken value) => value == null ? "0" : ((long)value / 1000f).ToString("0.0") + "s";
    }
}
