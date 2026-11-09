using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DungeonContract
{
    public sealed class QualityView : MonoBehaviour
    {
        private sealed class Visual { public SpriteRenderer Renderer; public Vector3 Target; public Color Base; public float FlashUntil; public float FreezeUntil; }
        private sealed class InputRecord { public uint Sequence; public Vector2 Move; public float Seconds; }
        private readonly Dictionary<string, Visual> visuals = new Dictionary<string, Visual>();
        private readonly HashSet<string> effectIDs = new HashSet<string>();
        private readonly Queue<string> effectOrder = new Queue<string>();
        private readonly List<InputRecord> history = new List<InputRecord>();
        private readonly HashSet<int> explored = new HashSet<int>();
        private readonly List<GameObject> floating = new List<GameObject>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
        private JObject manifest;
        private JObject snapshot;
        private JToken own;
        private string ownID;
        private Camera roomCamera;
        private GameObject worldRoot;
        private Canvas canvas;
        private Font font;
        private Sprite pixel;
        private Texture2D atlas;
        private AudioSource sound;
        private SpriteRenderer weapon;
        private SpriteRenderer preview;
        private SpriteRenderer secondPreview;
        private Text crosshair;
        private Sprite circle;
        private Text title;
        private Text health;
        private Text objective;
        private Text help;
        private Text map;
        private Text notification;
        private Text result;
        private GameObject choicePanel;
        private readonly Button[] choiceButtons = new Button[3];
        private Vector2 predicted;
        private Vector2 lastDirection;
        private Vector2 localMove;
        private Vector2 predictedDodge;
        private float dodgeUntil;
        private float localDodgeReady;
        private float weaponPulseUntil;
        private float notificationUntil;
        private float lastAudio;
        private float shakeUntil;
        private bool mutedInput;
        private bool shakeEnabled = true;
        private bool numbersEnabled = true;
        private int room = -1;
        private int deaths;
        private Vector3 cameraStart;
        private Vector3 cameraTarget;
        private float transitionAt;
        private float started;
        public Action<string> SelectChoice;
        public Vector2 Position => predicted;

        public void Configure(JObject allocation, string player)
        {
            manifest = allocation; ownID = player; started = Time.unscaledTime;
            font = Resources.Load<Font>("DungeonContract/Fonts/NotoSansSC");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            atlas = Resources.Load<Texture2D>("DungeonContract/Art/tileset");
            if (atlas != null) atlas.filterMode = FilterMode.Point;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point }; texture.SetPixel(0, 0, Color.white); texture.Apply(); pixel = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1);
            worldRoot = new GameObject("Quality rooms"); worldRoot.transform.SetParent(transform);
            var circleTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (var horizontal = 0; horizontal < 64; horizontal++) for (var vertical = 0; vertical < 64; vertical++) circleTexture.SetPixel(horizontal, vertical, Vector2.Distance(new Vector2(horizontal, vertical), new Vector2(31.5f, 31.5f)) <= 31 ? Color.white : Color.clear);
            circleTexture.Apply(); circle = Sprite.Create(circleTexture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 64);
            weapon = CreateSprite("manual weapon", Vector2.zero, Color.white, new Vector2(0.65f, 0.35f), 7); if (atlas != null) weapon.sprite = AtlasSprite(129);
            preview = CreateSprite("turret preview", Vector2.zero, Color.clear, Vector2.one * 0.6f, 9); secondPreview = CreateSprite("second turret preview", Vector2.zero, Color.clear, Vector2.one * 0.6f, 9);
            roomCamera = new GameObject("Fixed room camera").AddComponent<Camera>(); roomCamera.transform.SetParent(transform); roomCamera.orthographic = true; roomCamera.orthographicSize = 6.75f; roomCamera.backgroundColor = new Color(0.035f, 0.045f, 0.065f); roomCamera.transform.position = new Vector3(6, 4, -10);
            roomCamera.gameObject.AddComponent<AudioListener>(); sound = gameObject.AddComponent<AudioSource>(); sound.playOnAwake = false; sound.volume = 0.25f;
            foreach (var kind in new[] { "shot", "turret_shot", "hit", "hurt", "dodge", "ability", "interaction", "warning", "death", "respawn", "room_clear", "choice", "supply" }) clips[kind] = Tone(kind);
            BuildUI();
            foreach (var definition in (JArray)allocation["p1_world"]["rooms"]) DrawRoom((int)((JArray)allocation["p1_world"]["rooms"]).IndexOf(definition), (JObject)definition);
            if (EventSystem.current == null) new GameObject("UI event system", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform);
        }

        private Vector2 Offset(int index)
        {
            var offsets = new[] { new Vector2(0, 0), new Vector2(24, 0), new Vector2(24, 14), new Vector2(48, 14), new Vector2(48, 0), new Vector2(72, 0), new Vector2(24, -14), new Vector2(48, -14) };
            return offsets[index];
        }
        private JObject Layout(int index) => (JObject)manifest["p1_world"]["rooms"][index];
        private static Vector2 Point(JToken entity) => new Vector2((float)entity["x"], (float)entity["y"]);

        private void DrawRoom(int index, JObject definition)
        {
            for (var horizontal = 0; horizontal < (int)definition["width"]; horizontal++) for (var vertical = 0; vertical < (int)definition["height"]; vertical++)
            {
                var point = new Vector2(horizontal + 0.5f, vertical + 0.5f); var wall = RoomGeometry.Blocked(definition, point, 0);
                var renderer = CreateSprite("tile_" + index + "_" + horizontal + "_" + vertical, point + Offset(index), wall ? new Color(0.30f, 0.34f, 0.40f) : (horizontal + vertical) % 2 == 0 ? new Color(0.12f, 0.16f, 0.20f) : new Color(0.14f, 0.18f, 0.22f), Vector2.one, -20);
                if (atlas != null) { renderer.sprite = AtlasSprite(wall ? 28 : 3); renderer.color = Color.white; }
            }
            if (definition["objective"] is JArray) CreateSprite("mechanism_" + index, RoomGeometry.Point(definition["objective"]) + Offset(index), new Color(0.9f, 0.7f, 0.25f), Vector2.one * 0.8f, 0);
            if (index == 3 || index == 7) CreateSprite("supply_" + index, (index == 3 ? new Vector2(3, 3) : new Vector2(5, 4)) + Offset(index), new Color(0.3f, 0.85f, 0.65f), Vector2.one * 0.6f, 0);
        }

        private Sprite AtlasSprite(int index)
        {
            if (sprites.TryGetValue(index, out var cached)) return cached;
            var columns = Math.Max(1, atlas.width / 16); var horizontal = index % columns * 16; var vertical = atlas.height - 16 - index / columns * 16;
            var created = Sprite.Create(atlas, new Rect(horizontal, Math.Max(0, vertical), 16, 16), Vector2.one * 0.5f, 16); sprites[index] = created; return created;
        }

        private SpriteRenderer CreateSprite(string id, Vector2 point, Color color, Vector2 size, int order)
        {
            var renderer = new GameObject(id).AddComponent<SpriteRenderer>(); renderer.transform.SetParent(worldRoot.transform); renderer.sprite = pixel; renderer.transform.position = new Vector3(point.x, point.y, 0); renderer.transform.localScale = size; renderer.color = color; renderer.sortingOrder = order; return renderer;
        }

        public void Receive(JObject incoming)
        {
            if (snapshot != null && (long?)incoming["world_frame_id"] < (long?)snapshot["world_frame_id"]) return;
            snapshot = incoming; own = ((JArray)incoming["players"]).FirstOrDefault(player => (string)player["player_id"] == ownID); if (own == null) return;
            var nextRoom = (int)own["room_id"]; var nextDeaths = (int)own["deaths"];
            if (room != nextRoom || deaths != nextDeaths || (int)own["health"] == 0)
            {
                room = nextRoom; deaths = nextDeaths; predicted = Point(own); history.Clear(); dodgeUntil = 0; explored.Add(room); cameraStart = roomCamera.transform.position; cameraTarget = (Vector3)(Offset(room) + new Vector2((int)Layout(room)["width"] / 2f, (int)Layout(room)["height"] / 2f)) + Vector3.back * 10; transitionAt = Time.unscaledTime;
            }
            var acknowledgment = (uint)own["last_input_sequence"]; history.RemoveAll(record => record.Sequence <= acknowledgment);
            var authoritative = Point(own);
            foreach (var record in history) authoritative = RoomGeometry.Move(Layout(room), authoritative, record.Move * 5 * record.Seconds, 0.3f);
            var correction = authoritative - predicted; predicted = correction.magnitude > 1.5f ? authoritative : predicted + correction * 0.45f;
            var present = new HashSet<string>();
            foreach (var player in (JArray)incoming["players"])
            {
                if ((int)player["room_id"] != room || (bool)player["left"]) continue;
                var id = "player:" + player["player_id"]; present.Add(id); Draw(id, (string)player["player_id"] == ownID ? predicted : Point(player), (int)player["health"] <= 0 ? Color.gray : (string)player["loadout_id"] == "ranger" ? new Color(0.3f, 0.9f, 0.55f) : new Color(0.35f, 0.75f, 1), 0.65f, 5);
            }
            foreach (var enemy in ((JArray)incoming["enemies"]).Where(item => (int)item["room_id"] == room))
            {
                var id = "enemy:" + enemy["id"]; present.Add(id); var kind = (string)enemy["kind"];
                Draw(id, Point(enemy), kind == "boss" ? new Color(0.72f, 0.4f, 0.9f) : kind == "elite" ? new Color(1, 0.7f, 0.25f) : kind == "archer" ? new Color(0.95f, 0.45f, 0.6f) : kind == "charger" ? new Color(1, 0.35f, 0.2f) : new Color(0.8f, 0.5f, 0.3f), kind == "boss" ? 1.8f : kind == "elite" ? 1.2f : 0.8f, 4);
                if ((string)enemy["warning"] != "") DrawWarning(enemy, present);
            }
            foreach (var projectile in ((JArray)incoming["projectiles"]).Where(item => (int)item["room_id"] == room)) { var id = "projectile:" + projectile["id"]; present.Add(id); Draw(id, Point(projectile), (bool)projectile["enemy"] ? new Color(1, 0.45f, 0.2f) : new Color(0.6f, 0.95f, 1), 0.18f, 8); }
            foreach (var turret in ((JArray)incoming["turrets"]).Where(item => (int)item["room_id"] == room)) { var id = "turret:" + turret["id"]; present.Add(id); Draw(id, Point(turret), new Color(0.4f, 0.7f, 0.9f), 0.65f, 4); }
            foreach (var trap in ((JArray)incoming["traps"]).Where(item => (int)item["room_id"] == room)) { var id = "trap:" + trap["id"]; present.Add(id); Draw(id, Point(trap), (string)trap["phase"] == "active" ? new Color(1, 0.2f, 0.1f) : (string)trap["phase"] == "warning" ? new Color(1, 0.6f, 0.1f) : new Color(0.3f, 0.25f, 0.22f), 1.2f, -1); }
            foreach (var id in visuals.Keys.Where(id => !present.Contains(id)).ToArray()) { Destroy(visuals[id].Renderer.gameObject); visuals.Remove(id); }
            foreach (var effect in (JArray)incoming["effects"]) PlayEffect(effect);
            UpdateHUD();
        }

        private void Draw(string id, Vector2 position, Color color, float size, int order)
        {
            if (!visuals.TryGetValue(id, out var visual)) { visual = new Visual { Renderer = CreateSprite(id, position + Offset(room), color, Vector2.one * size, order) }; visuals[id] = visual; if (atlas != null && !id.StartsWith("trap:") && !id.StartsWith("warning:") && !id.StartsWith("projectile:")) visual.Renderer.sprite = AtlasSprite(id.StartsWith("player:") ? 98 : id.StartsWith("turret:") ? 39 : 109); }
            visual.Target = position + Offset(room); visual.Base = color; visual.Renderer.transform.localScale = Vector2.one * size;
        }

        private void DrawWarning(JToken enemy, HashSet<string> present)
        {
            var origin = new Vector2((float)enemy["warning_x"], (float)enemy["warning_y"]); var aim = new Vector2((float)enemy["aim_x"], (float)enemy["aim_y"]); var kind = (string)enemy["warning"];
            var id = "warning:" + enemy["id"]; present.Add(id);
            Draw(id, origin + (kind == "charge" ? aim * 1.8f : Vector2.zero), new Color(1, 0.32f, 0.08f, 0.55f), kind == "stomp" ? 6 : 1.5f, -2);
            var visual = visuals[id]; if (kind == "charge") { visual.Renderer.transform.localScale = new Vector3(3.6f, 1.1f, 1); visual.Renderer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg); }
            if (kind == "stomp") visual.Renderer.sprite = circle;
            if (kind == "fan" || kind == "shot") { visual.Renderer.transform.localScale = new Vector3(3, 0.18f, 1); visual.Renderer.transform.position = origin + Offset(room) + aim * 1.5f; visual.Target = visual.Renderer.transform.position; visual.Renderer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg); }
        }

        public void ReadLocalInput(bool blocked)
        {
            mutedInput = blocked || own == null || (int)own["health"] <= 0 || snapshot == null || (string)snapshot["run_status"] != "running";
            localMove = mutedInput ? Vector2.zero : Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1);
            if (localMove.sqrMagnitude > 0.01f) lastDirection = localMove.normalized;
            if (mutedInput) { history.Clear(); dodgeUntil = 0; }
        }
        public void RecordInput(GrayboxInput input, bool blocked) { if (blocked || mutedInput) return; history.Add(new InputRecord { Sequence = input.Sequence, Move = Time.unscaledTime < dodgeUntil ? predictedDodge * 1.6f : input.Move, Seconds = 1f / 30 }); if (history.Count > 60) { history.RemoveAt(0); predicted = Point(own); } }
        public void AttackStart() { if (!mutedInput && !PointerOverPanel()) weaponPulseUntil = Time.unscaledTime + 0.06f; }
        public void PredictDodge() { if (mutedInput || own == null || (int)snapshot["safe_node"] >= 0 || (long)own["dodge_remaining_ms"] > 0 || Time.unscaledTime < localDodgeReady) return; predictedDodge = localMove.sqrMagnitude > 0 ? localMove.normalized : lastDirection.sqrMagnitude > 0 ? lastDirection : Aim(MouseAim()); dodgeUntil = Time.unscaledTime + 0.25f; localDodgeReady = Time.unscaledTime + 3; }
        public void ActionFeedback(bool accepted, string reason) { if (!accepted) { dodgeUntil = 0; Notify(reason == "cooldown" ? "动作冷却中" : reason == "invalid_position" ? "落点不合法，技能未消耗" : reason == "safe_window" ? "安全选择阶段不能攻击" : "当前状态不能执行该动作"); } }
        public Vector2 MouseWorld() => roomCamera == null || room < 0 ? Vector2.zero : (Vector2)roomCamera.ScreenToWorldPoint(Input.mousePosition) - Offset(room);
        public ushort MouseAim() { var direction = MouseWorld() - predicted; var angle = Mathf.Atan2(direction.y, direction.x); if (angle < 0) angle += Mathf.PI * 2; return (ushort)(angle / (Mathf.PI * 2) * 65535); }
        private static Vector2 Aim(ushort angle) { var radians = angle / 65535f * Mathf.PI * 2; return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)); }
        public bool PointerOverPanel() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void LateUpdate()
        {
            if (room < 0 || own == null) return;
            if (!mutedInput) predicted = RoomGeometry.Move(Layout(room), predicted, (Time.unscaledTime < dodgeUntil ? predictedDodge * 8 : localMove * 5) * Math.Min(Time.unscaledDeltaTime, 0.05f), 0.3f);
            foreach (var entry in visuals)
            {
                var visual = entry.Value;
                if (entry.Key == "player:" + ownID) visual.Renderer.transform.position = predicted + Offset(room);
                else if (Time.unscaledTime >= visual.FreezeUntil) visual.Renderer.transform.position = Vector3.Lerp(visual.Renderer.transform.position, visual.Target, Math.Min(1, Time.unscaledDeltaTime * 15));
                visual.Renderer.color = Time.unscaledTime < visual.FlashUntil ? Color.white : visual.Base;
            }
            var cameraPosition = Vector3.Lerp(cameraStart, cameraTarget, Mathf.Clamp01((Time.unscaledTime - transitionAt) / 0.2f));
            if (shakeEnabled && Time.unscaledTime < shakeUntil) cameraPosition += new Vector3(Mathf.Sin(Time.unscaledTime * 100) * 0.035f, Mathf.Cos(Time.unscaledTime * 80) * 0.035f, 0);
            roomCamera.transform.position = cameraPosition;
            var aim = Aim(MouseAim()); weapon.transform.position = predicted + Offset(room) + aim * 0.4f; weapon.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg); weapon.transform.localScale = Time.unscaledTime < weaponPulseUntil ? new Vector3(0.75f, 0.4f, 1) : new Vector3(0.65f, 0.35f, 1); weapon.enabled = (int)own["health"] > 0;
            if (crosshair != null) crosshair.rectTransform.anchoredPosition = new Vector2(Input.mousePosition.x * 1280 / Screen.width - 7, -(Screen.height - Input.mousePosition.y) * 720 / Screen.height - 7);
            var showingPreview = (string)own["loadout_id"] == "runesmith" && !mutedInput;
            preview.enabled = showingPreview; secondPreview.enabled = false;
            if (showingPreview)
            {
                var point = MouseWorld(); var dual = ((JArray)own["upgrades"]).Any(upgrade => (string)upgrade == "dual_turret"); var perpendicular = new Vector2(-aim.y, aim.x) * 0.5f;
                var first = dual ? point - perpendicular : point; var second = point + perpendicular;
                var valid = Vector2.Distance(point, predicted) <= 3 && !RoomGeometry.Blocked(Layout(room), first, 0.3f) && (!dual || !RoomGeometry.Blocked(Layout(room), second, 0.3f));
                preview.transform.position = first + Offset(room); preview.color = valid ? new Color(0.3f, 1, 0.6f, 0.5f) : new Color(1, 0.3f, 0.2f, 0.5f); secondPreview.enabled = dual; secondPreview.transform.position = second + Offset(room); secondPreview.color = preview.color;
            }
            if (Time.unscaledTime >= notificationUntil) notification.text = "";
        }

        public void ShowStatus(string status, string outcome) { if (result != null) result.text = outcome ?? ""; if (snapshot == null) objective.text = status; }

        private void BuildUI()
        {
            var canvasObject = new GameObject("Quality HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform); canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            title = Label("Title", 20, 14, 780, 25, 20); objective = Label("Objective", 20, 42, 950, 24, 17); health = Label("Health", 20, 661, 1040, 30, 19); help = Label("Help", 20, 695, 1170, 24, 14);
            crosshair = Label("Crosshair", 0, 0, 20, 20, 18); crosshair.text = "+";
            map = Label("Map", 1070, 15, 200, 120, 13); notification = Label("Notification", 300, 110, 680, 60, 22); result = Label("Result", 270, 200, 750, 230, 22);
            ButtonAt("震动", 1100, 663, 70, 27, () => shakeEnabled = !shakeEnabled); ButtonAt("数字", 1180, 663, 70, 27, () => numbersEnabled = !numbersEnabled);
            ButtonAt("帮助", 1180, 694, 70, 24, () => Notify("WASD移动 / 鼠标手动瞄准 / 左键攻击 / Space闪避 / Q技能 / E按住机关 / F补给。闪避可穿普通敌人，不能穿墙和Boss。", 8));
            choicePanel = new GameObject("Independent upgrade choice", typeof(RectTransform)); choicePanel.transform.SetParent(canvas.transform, false);
            for (var index = 0; index < 3; index++) { var captured = index; choiceButtons[index] = ButtonAt("选择", 265 + index * 255, 270, 240, 100, () => { if (own?["candidates"] is JArray candidates && captured < candidates.Count) SelectChoice?.Invoke((string)candidates[captured]); }); choiceButtons[index].transform.SetParent(choicePanel.transform, false); }
            choicePanel.SetActive(false);
            help.text = "WASD移动 · 鼠标瞄准 · 左键射击 · Space闪避 · Q技能 · E机关 · F补给 · Esc菜单";
        }

        private Text Label(string name, float horizontal, float vertical, float width, float heightValue, int size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>(); label.transform.SetParent(canvas.transform, false); SetRect(label.rectTransform, horizontal, vertical, width, heightValue); label.font = font; label.fontSize = size; label.color = new Color(0.88f, 0.91f, 0.94f); label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow; return label;
        }
        private Button ButtonAt(string caption, float horizontal, float vertical, float width, float heightValue, Action clicked)
        {
            var button = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>(); button.transform.SetParent(canvas.transform, false); SetRect(button.GetComponent<RectTransform>(), horizontal, vertical, width, heightValue); button.GetComponent<Image>().color = new Color(0.12f, 0.21f, 0.28f, 0.97f); button.onClick.AddListener(() => clicked());
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); label.transform.SetParent(button.transform, false); label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = new Vector2(8, 2); label.rectTransform.offsetMax = new Vector2(-8, -2); label.font = font; label.fontSize = 16; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.text = caption; label.raycastTarget = false; return button;
        }
        private static void SetRect(RectTransform rectangle, float horizontal, float vertical, float width, float heightValue) { rectangle.anchorMin = rectangle.anchorMax = new Vector2(0, 1); rectangle.pivot = new Vector2(0, 1); rectangle.anchoredPosition = new Vector2(horizontal, -vertical); rectangle.sizeDelta = new Vector2(width, heightValue); }
        private void Notify(string message, float seconds = 2) { notification.text = message; notificationUntil = Time.unscaledTime + seconds; }

        private void UpdateHUD()
        {
            var definition = Layout(room); var state = snapshot["rooms"][room];
            title.text = "DUNGEON CONTRACT  ·  " + RoomName(room) + "     " + TimeSpan.FromMilliseconds((long)snapshot["elapsed_ms"]).ToString(@"mm\:ss");
            objective.text = (string)state["status"] == "warning" ? "即将开战 · " + ((long)state["warning_remaining_ms"] / 1000f).ToString("0.0") + "s · 迟到队友仍可进入" : (string)state["status"] == "cleared" ? "区域已完成 · 选择已开放出口继续" : "剩余敌人 " + state["enemies_remaining"] + "   波次 " + state["wave"] + (definition["objective"] != null ? ((bool)state["mechanism_complete"] ? " · 机关已完成" : " · 按住E操作机关，队友可保护") : "");
            health.text = "生命 " + own["health"] + "/100  盾 " + own["shield"] + "   闪避 " + Seconds(own["dodge_remaining_ms"]) + "   技能 " + Seconds(own["ability_remaining_ms"]) + "   全队增援 " + snapshot["reinforcement_remaining"];
            if ((int)own["health"] <= 0) objective.text = (long)own["respawn_remaining_ms"] >= 0 ? "死亡 · 自动增援等待 " + Seconds(own["respawn_remaining_ms"]) : "死亡 · 增援不足，保留本局身份等待队伍结果";
            if ((long)own["interaction_remaining_ms"] > 0) objective.text += " · 操作剩余 " + Seconds(own["interaction_remaining_ms"]);
            map.text = string.Join("\n", explored.OrderBy(index => index).Select(index => (index == room ? "▶ " : "   ") + RoomName(index) + " " + StateLabel((string)snapshot["rooms"][index]["status"]))) + "\n队友 " + string.Join(" / ", ((JArray)snapshot["players"]).Where(player => !(bool)player["left"]).Select(player => ((int)player["slot"] + 1) + ":" + RoomName((int)player["room_id"])));
            var safe = (int)snapshot["safe_node"] >= 0; choicePanel.SetActive(safe);
            if (safe)
            {
                objective.text = "安全强化 · 各自三选一 · " + Seconds(snapshot["safe_remaining_ms"]) + "后默认第一项 · 已选：" + string.Join("、", ((JArray)own["upgrades"]).Select(item => UpgradeName((string)item)));
                var candidates = (JArray)own["candidates"];
                for (var index = 0; index < 3; index++) { choiceButtons[index].interactable = own["choice"].Type == JTokenType.Null && index < candidates.Count; choiceButtons[index].GetComponentInChildren<Text>().text = index < candidates.Count ? UpgradeName((string)candidates[index]) + "\n" + UpgradeDescription((string)candidates[index]) + (index == 0 ? "\n默认" : "") : "无合法候选"; }
            }
            if (room == 0 && Time.unscaledTime - started < 20 && (string)manifest["mode"] != "trial") objective.text = "安全入口：练习移动、瞄准、Space闪避和Q技能；朝右侧门移动开始。右下角可随时查看帮助。";
        }

        private void PlayEffect(JToken effect)
        {
            var id = (string)effect["id"]; if (!effectIDs.Add(id)) return; effectOrder.Enqueue(id); if (effectOrder.Count > 256) effectIDs.Remove(effectOrder.Dequeue());
            if ((int)effect["room"] != room || (long)snapshot["elapsed_ms"] - (long)effect["at"] > 200) return;
            var kind = (string)effect["kind"]; var actor = (string)effect["actor"]; var target = (string)effect["target"];
            if (target != null && visuals.TryGetValue("enemy:" + target, out var visual)) { visual.FlashUntil = Time.unscaledTime + 0.07f; visual.FreezeUntil = Time.unscaledTime + 0.035f; }
            if (actor == ownID && kind == "hurt") shakeUntil = Time.unscaledTime + 0.08f;
            if (kind == "room_clear") Notify("区域完成 · 出口已开放");
            if (clips.TryGetValue(kind, out var clip) && Time.unscaledTime - lastAudio > 0.025f) { sound.PlayOneShot(clip); lastAudio = Time.unscaledTime; }
            if (numbersEnabled && (int)effect["amount"] > 0 && floating.Count < 32)
            {
                var point = roomCamera.WorldToScreenPoint(new Vector3((float)effect["x"] + Offset(room).x, (float)effect["y"] + Offset(room).y, 0));
                var label = Label("Damage", point.x * 1280 / Screen.width, (Screen.height - point.y) * 720 / Screen.height, 60, 24, 15); label.text = effect["amount"].ToString(); label.color = kind == "hurt" ? new Color(1, 0.5f, 0.4f) : Color.white; floating.Add(label.gameObject); StartCoroutine(RemoveFloating(label.gameObject));
            }
        }
        private System.Collections.IEnumerator RemoveFloating(GameObject item) { yield return new WaitForSecondsRealtime(0.35f); floating.Remove(item); Destroy(item); }
        private AudioClip Tone(string kind)
        {
            var frequency = kind == "hurt" || kind == "death" ? 120 : kind == "shot" ? 520 : kind == "hit" ? 760 : kind == "warning" ? 260 : 400;
            var samples = new float[2205]; for (var index = 0; index < samples.Length; index++) samples[index] = Mathf.Sin(index * frequency * Mathf.PI * 2 / 22050) * (1 - (float)index / samples.Length) * 0.15f;
            var clip = AudioClip.Create("Original DC " + kind, samples.Length, 1, 22050, false); clip.SetData(samples, 0); return clip;
        }
        private static string Seconds(JToken number) => ((long)number / 1000f).ToString("0.0") + "s";
        private static string RoomName(int index) => new[] { "入口", "前厅", "机关廊", "镇守庭", "内殿", "守卫厅", "符文支路", "补给支路" }[index];
        private static string StateLabel(string value) => value == "cleared" ? "✓" : value == "active" ? "战斗" : value == "warning" ? "预警" : "";
        private static string UpgradeName(string id) => new Dictionary<string, string> { ["swift_dodge"] = "迅捷闪避", ["landing_impact"] = "落点冲击", ["frost"] = "冰霜附着", ["rune_shield"] = "符文护盾", ["piercing_bolt"] = "穿透弩矢", ["fan_shot"] = "扇形齐射", ["charged_volley"] = "蓄能齐射", ["chain_rune"] = "连锁符弹", ["dual_turret"] = "双重炮塔", ["heavy_turret"] = "重型炮塔" }.TryGetValue(id, out var name) ? name : id;
        private static string UpgradeDescription(string id) => id == "dual_turret" ? "一次部署两台，每台5伤害" : id == "piercing_bolt" ? "额外穿透一个目标" : id == "fan_shot" ? "三箭散射，单箭65%伤害" : id == "charged_volley" ? "技能齐射提升为五箭" : id == "chain_rune" ? "额外跳跃一个目标，70%伤害" : id == "heavy_turret" ? "炮塔伤害提升50%" : id == "swift_dodge" ? "闪避冷却减少0.6秒" : id == "landing_impact" ? "闪避落点冲击，10伤害" : id == "rune_shield" ? "技能后获得15短时护盾" : "命中短暂减速，不叠加";
        private void OnDestroy() { foreach (var clip in clips.Values) Destroy(clip); foreach (var created in sprites.Values) Destroy(created); if (circle != null) { Destroy(circle.texture); Destroy(circle); } if (pixel != null) { Destroy(pixel.texture); Destroy(pixel); } if (worldRoot != null) Destroy(worldRoot); if (canvas != null) Destroy(canvas.gameObject); if (roomCamera != null) Destroy(roomCamera.gameObject); }
    }
}
