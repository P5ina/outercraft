using System.Linq;
using System.Collections.Generic;
using OuterCraft.Assets;
using OuterCraft.Player;
using OuterCraft.UI;
using OuterCraft.World;
using OWML.Common;
using OWML.ModHelper;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OuterCraft
{
    /// OuterCraft: Minecraft, natively inside Outer Wilds.
    /// Blocks live on a cube-sphere grid stuck to each planet, the Hearthian moves with Minecraft's
    /// numbers on Outer Wilds' own gravity, and the HUD and hand are Minecraft's, drawn from the
    /// player's own Minecraft jar.
    public class OuterCraft : ModBehaviour
    {
        public static OuterCraft Instance;
        public static IModHelper Helper => Instance.ModHelper;

        private bool _minecraftMode = true;
        private string _jarPath = "";
        private string _skin = "steve";
        private float _reach = 5f;

        private BlockWorld _world;
        private readonly Inventory _inv = new Inventory();
        private readonly NetherPortal _portal = new NetherPortal();
        private readonly Combat _combat = new Combat();
        private readonly Elytra _elytra = new Elytra();
        private readonly SteveModel _steve = new SteveModel();
        private readonly SuitFigure _figure = new SuitFigure();
        private readonly InventoryScreen _screen = new InventoryScreen();
        private bool _creative;
        private float _nextInvSave;
        private readonly McMovement _movement = new McMovement();
        private readonly BlockInteraction _interaction = new BlockInteraction();
        private readonly ViewModel _viewModel = new ViewModel();
        private readonly McHud _hud = new McHud();
        private readonly Sleep _sleep = new Sleep();
        private readonly NomaiRunes _runes = new NomaiRunes();
        private readonly McMusic _music = new McMusic();
        private readonly WarpCores _cores = new WarpCores();
        private bool _inSolarSystem;
        private bool _ready;

        private void Awake() => Instance = this;

        private void Start()
        {
            ApplyConfig(ModHelper.Config);
            BlockMaterials.UseGameShader = ModHelper.Config.GetSettingsValue<bool>("outerWildsShading");
            BlockMaterials.Load(ModHelper.Manifest.ModFolderPath);
            _ready = McAssets.Load(_jarPath, _skin);
            if (!_ready)
            {
                ModHelper.Console.WriteLine("OuterCraft: Minecraft jar not found. Set 'minecraftJar' in the mod settings to your minecraft client .jar.", MessageType.Error);
                _missingJar = true;
                return;
            }

            StartCoroutine(McSounds.Load(McAssets.Source));
            var harmony = new HarmonyLib.Harmony("P5INA.OuterCraft");
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(ToolModeSwapper), nameof(ToolModeSwapper.EquipToolMode)),
                prefix: new HarmonyLib.HarmonyMethod(typeof(OuterCraft), nameof(EquipToolPrefix)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(OWInput), nameof(OWInput.GetAxisValue)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(OuterCraft), nameof(MoveAxisPostfix)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(WarpCoreItem), nameof(WarpCoreItem.GetDisplayName)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(WarpCores), nameof(WarpCores.DisplayNamePostfix)));

            var go = new GameObject("OuterCraft_World");
            DontDestroyOnLoad(go);
            _world = go.AddComponent<BlockWorld>();
            go.AddComponent<Fx>();
            _interaction.Combat = _combat;
            _interaction.Elytra = _elytra;
            var drops = go.AddComponent<ItemEntities>();
            drops.Collect = (item, n) => _inv.Add(item, n);
            _inv.Creative = _creative;
            _inv.ResetToKit();
            _interaction.Swung = _viewModel.Swing;
            _interaction.OpenCrafting = () => OpenScreen(InventoryScreen.Kind.Crafting);
            _interaction.UseBed = (pb, cell) => _sleep.TryStart(pb, cell);
            _viewModel.ViewOverride = _sleep.View;
            _screen.Throw = stack =>
            {
                var cam = Locator.GetPlayerCamera();
                if (cam != null) BlockInteraction.Throw(cam.mainCamera, stack);
            };

            LoadManager.OnStartSceneLoad += (from, to) =>
            {
                _inSolarSystem = false;
                _sleep.Forget();
                _runes.Forget();
                _cores.Clear();
                HandsBusy = false;
                InvertMove = false;
                _body.Clear();
                _bodyState = 0;
                _steve.Destroy();
                _voices.Unhook();
                _figure.Clear();
                _portal.Clear();
                _combat.Clear();
                Fx.Instance?.Clear();
                _elytra.Stop(null);
                CloseScreen(false);
                ItemEntities.Instance?.Clear();
                _movement.Restore();
                _movement.Forget();
                _viewModel.Destroy();
                _world.OnSceneReset();
            };
            LoadManager.OnCompleteSceneLoad += (from, to) =>
            {
                if (to != OWScene.SolarSystem) return;
                ModHelper.Events.Unity.RunWhen(() => Locator.GetPlayerBody() != null, () =>
                {
                    _inSolarSystem = true;
                    _inv.ResetToKit(); // a new loop: the kit again, nothing carried over
                    _death.OnNewLoop();
                    _voices.Hook();
                    _music.OnNewLoop();
                    if (_loops > 0)
                    {
                        if (DeathScreen.LastDeath == DeathType.Digestion) Advancements.Grant("fishy");
                        Advancements.Grant("the_end");
                    }
                    _loops++;
                });
            };
            GlobalMessenger<DeathType>.AddListener("PlayerDeath", type => { if (_minecraftMode && _inSolarSystem) _death.OnDeath(type); });
            ModHelper.Console.WriteLine("OuterCraft ready. G toggles Minecraft mode.", MessageType.Success);
        }

        public override void Configure(IModConfig config) => ApplyConfig(config);

        private void ApplyConfig(IModConfig config)
        {
            _jarPath = config.GetSettingsValue<string>("minecraftJar") ?? "";
            _minecraftMode = config.GetSettingsValue<bool>("startInMinecraftMode");
            McGui.ConfiguredScale = config.GetSettingsValue<int>("guiScale");
            _creative = (config.GetSettingsValue<string>("gameMode") ?? "survival").ToLowerInvariant().StartsWith("c");
            _inv.Creative = _creative;
            _reach = config.GetSettingsValue<float>("reach");
            _skin = config.GetSettingsValue<string>("skin") ?? "steve";
            _music.Volume = Mathf.Clamp01(config.GetSettingsValue<float>("musicVolume"));
            var name = config.GetSettingsValue<string>("playerName");
            _death.PlayerName = string.IsNullOrWhiteSpace(name) ? "Steve" : name.Trim();
            _interaction.Reach = _reach > 0 ? _reach : 5f;
            float bright = config.GetSettingsValue<float>("blockBrightness");
            if (bright <= 0.05f) bright = 0.85f;
            if (Mathf.Abs(bright - Shade.Brightness) > 1e-3f)
            {
                Shade.Brightness = bright;
                if (_world != null) foreach (var pb in _world.Planets) pb.MarkAllDirty();
            }
        }

        // ---------------------------------------------------------------- frame

        private bool OnFoot()
        {
            return OWInput.IsInputMode(InputMode.Character) && !PlayerState.IsDead() && !PlayerState.IsInsideShip() &&
                   !PlayerState.IsAttached() && !PlayerState.InMapView() && !PlayerState.InConversation();
        }

        private bool ToolOut()
        {
            var swapper = Locator.GetToolModeSwapper();
            return swapper != null && swapper.GetToolMode() != ToolMode.None;
        }

        private void Update()
        {
            if (!_ready || !_inSolarSystem) return;
            var controller = Locator.GetPlayerController();
            var cam = Locator.GetPlayerCamera();
            if (controller == null || cam == null) return;

            _sleep.Update(_minecraftMode);
            WarpCores.Enabled = _minecraftMode;
            _cores.Update(_minecraftMode);
            _music.Update(_minecraftMode, TimeLoop.IsTimeFlowing() && TimeLoop.GetSecondsRemaining() < 90f);

            var kb = Keyboard.current;
            if (kb != null && OWInput.IsInputMode(InputMode.Character) && kb.gKey.wasPressedThisFrame)
            {
                _minecraftMode = !_minecraftMode;
                Notify(_minecraftMode ? "Minecraft mode" : "Outer Wilds mode");
            }

            // Tab: the inventory (E is Outer Wilds' interact key)
            if (kb != null && _screen.IsOpen && (kb.tabKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                CloseScreen(true);
            else if (kb != null && !_screen.IsOpen && _minecraftMode && OnFoot() && kb.tabKey.wasPressedThisFrame)
                OpenScreen(_inv.Creative ? InventoryScreen.Kind.Creative : InventoryScreen.Kind.Inventory);
            if (_screen.IsOpen && (!_minecraftMode || PlayerState.IsDead()))
                CloseScreen(true);
            if (_screen.IsOpen)
            {
                _screen.HandleInput(_inv);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            bool onFoot = OnFoot() || _screen.IsOpen;
            bool active = _minecraftMode && onFoot;

            // Minecraft movement only with feet on a planet: in zero-g the jetpack is the only way around.
            if (active && !PlayerState.InZeroG()) _movement.Apply(controller);
            else _movement.Restore();

            bool hands = active && !ToolOut();
            HandsBusy = hands;
            bool input = hands && !_screen.IsOpen;
            if (input)
            {
                if (kb != null && kb.rightBracketKey.wasPressedThisFrame) _inv.Cycle(1);
                if (kb != null && kb.leftBracketKey.wasPressedThisFrame) _inv.Cycle(-1);
            }
            _inv.Update(input);
            _interaction.Update(cam.mainCamera, _inv, input);
            // F5: first person -> behind -> in front, like Minecraft
            if (kb != null && kb.f5Key.wasPressedThisFrame && active) _viewModel.Perspective = (_viewModel.Perspective + 1) % 3;
            bool third = hands && _viewModel.Perspective > 0;
            InvertMove = third && _viewModel.Perspective == 2;
            _viewModel.Sneaking = false; // Outer Wilds lowers the camera itself while crouching
            _viewModel.Update(cam.mainCamera, _inv.CurrentItem, _inv.ChangedAt, hands);
            _steve.Update(third, _viewModel, _inv.CurrentItem, _movement.Sneaking, _elytra.Gliding, _inv.HasElytra);
            Sounds(controller, active);
            HideOwnBody(third || _sleep.Asleep ? 2 : hands ? 1 : 0);
            _portal.Update(_inv.Creative);
            _voices.Enabled = _minecraftMode;
            Milestones(active);
            _figure.Update(_minecraftMode);
            _combat.Update();
            _elytra.Update(_inv, active && !_screen.IsOpen && !PlayerState.InZeroG());
            _movement.Gliding = _elytra.Gliding;

            if (Time.unscaledTime >= _nextInvSave)
            {
                _nextInvSave = Time.unscaledTime + 3f;
            }
        }

        // ---------------------------------------------------------------- the Hearthian's own body

        private readonly List<(Renderer r, UnityEngine.Rendering.ShadowCastingMode mode)> _body = new List<(Renderer, UnityEngine.Rendering.ShadowCastingMode)>();


        /// Minecraft shows only its own hand in first person: the Hearthian's arms and body (seen when
        /// looking down) become shadow-only. With a tool out they come back, they're holding it.
        private int _bodyState; // 0 as the game has it, 1 shadow only (first person), 2 gone (Steve stands in)

        private void HideOwnBody(int state)
        {
            if (state == _bodyState && (state == 0 || _body.Count > 0 && _body[0].r != null)) return;
            if (state != 0 && (_body.Count == 0 || _body[0].r == null))
            {
                _body.Clear();
                var anim = Locator.GetPlayerBody()?.GetComponentInChildren<PlayerAnimController>(true);
                if (anim == null) return;
                foreach (var r in anim.GetComponentsInChildren<Renderer>(true))
                    if (r is SkinnedMeshRenderer || r is MeshRenderer) _body.Add((r, r.shadowCastingMode));
            }
            foreach (var (r, mode) in _body)
            {
                if (r == null) continue;
                r.shadowCastingMode = state == 0 ? mode : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                r.forceRenderingOff = state == 2;
            }
            _bodyState = state;
        }

        /// Right click is Minecraft's "use" here: in Minecraft mode it no longer pulls out the scout launcher.
        public static bool HandsBusy;

        private static bool EquipToolPrefix(ToolMode mode) => !(HandsBusy && mode == ToolMode.Probe);

        /// Third person from the front: walking keys turned around, so W heads away from the camera.
        public static bool InvertMove;

        private static void MoveAxisPostfix(IInputCommands command, ref Vector2 __result)
        {
            if (InvertMove && command == InputLibrary.moveXZ) __result = -__result;
        }

        private readonly DeathScreen _death = new DeathScreen();
        private readonly VillagerVoices _voices = new VillagerVoices();
        private static int _loops;
        private float _nextMilestone;

        /// Advancements for places: in Minecraft mode at all, inside Dark Bramble, close to the Sun.
        private void Milestones(bool active)
        {
            if (!_minecraftMode || Time.unscaledTime < _nextMilestone) return;
            _nextMilestone = Time.unscaledTime + 0.5f;
            if (active) Advancements.Grant("minecraft");
            if (PlayerState.InBrambleDimension()) Advancements.Grant("deeper");
            var sun = Locator.GetAstroObject(AstroObject.Name.Sun);
            var body = Locator.GetPlayerBody();
            if (sun != null && body != null && (sun.transform.position - body.GetPosition()).magnitude < 4500f) Advancements.Grant("hot_tourist");
        }

        private void LateUpdate()
        {
            if (!_ready || !_inSolarSystem) return;
            _runes.LateUpdate(_minecraftMode);
        }

        private void FixedUpdate()
        {
            if (!_ready || !_inSolarSystem) return;
            _elytra.FixedStep();
        }

        public static bool CanPickUp => Instance != null && Instance._minecraftMode && !PlayerState.IsDead();

        private void OpenScreen(InventoryScreen.Kind kind)
        {
            if (_screen.IsOpen) return;
            if (kind == InventoryScreen.Kind.Inventory && _inv.Creative) kind = InventoryScreen.Kind.Creative;
            _screen.Open(kind);
            OWInput.ChangeInputMode(InputMode.Menu);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseScreen(bool restoreInput)
        {
            if (!_screen.IsOpen) return;
            _screen.Close(_inv);
            if (restoreInput && OWInput.IsInputMode(InputMode.Menu)) OWInput.ChangeInputMode(InputMode.Character);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private int _lastStep;
        private float _lastHealth = -1f;
        private float _lastHurtSound = -10f;

        /// Footsteps on Minecraft blocks (every 1/0.6 blocks walked, like Minecraft's nextStep) and the
        /// player's hurt sound whenever Outer Wilds takes health away.
        private void Sounds(PlayerCharacterController controller, bool active)
        {
            var body = Locator.GetPlayerBody();
            if (body == null) return;

            int step = Mathf.FloorToInt(_viewModel.WalkDist);
            if (step != _lastStep)
            {
                _lastStep = step;
                if (active && controller.IsGrounded())
                {
                    var up = body.transform.up;
                    var origin = body.GetPosition();
                    if (Physics.Raycast(origin, -up, out var hit, 2.5f, OWLayerMask.physicalMask, QueryTriggerInteraction.Ignore))
                    {
                        var pb = hit.collider.GetComponentInParent<PlanetBlocks>();
                        if (pb != null)
                        {
                            var cell = pb.Grid.Locate(pb.transform.InverseTransformPoint(hit.point - hit.normal * 0.02f));
                            var def = Blocks.Get(pb.Get(cell));
                            if (def != null) McSounds.Footstep(def, hit.point, pb.transform);
                        }
                    }
                }
            }

            var res = body.GetComponent<PlayerResources>();
            if (res != null)
            {
                float h = res.GetHealthFraction();
                // Minecraft's hurt invulnerability: after a hit, 10 ticks (0.5 s) before the next hurt
                // sound, so standing in a campfire sounds like Minecraft's fire (a hit every half second)
                // instead of a hurt sound every frame.
                if (_lastHealth >= 0f && h < _lastHealth - 0.001f && _minecraftMode && !PlayerState.IsDead() &&
                    Time.time - _lastHurtSound >= 0.5f)
                {
                    McSounds.Hurt(body.GetPosition(), body.transform);
                    _lastHurtSound = Time.time;
                }
                _lastHealth = h;
            }
        }

        private bool _missingJar;
        private GUIStyle _warnStyle;

        private void OnGUI()
        {
            if (_missingJar && Event.current.type == EventType.Repaint)
            {
                if (_warnStyle == null) _warnStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
                GUI.Box(new Rect(Screen.width * 0.2f, 20, Screen.width * 0.6f, 70),
                    "OuterCraft: Minecraft Java Edition not found.\nInstall Minecraft 1.21.4 or newer and launch it once, or set 'Minecraft jar' in the mod's settings.", _warnStyle);
            }
            if (_ready && _inSolarSystem) _portal.DrawOverlay();
            if (_ready && _inSolarSystem && _minecraftMode) _sleep.Draw();
            if (_ready && _inSolarSystem && _minecraftMode) _death.Draw();
            if (_ready && _inSolarSystem) Advancements.Draw(_minecraftMode && !PlayerState.IsDead());
            if (_ready && _inSolarSystem && _minecraftMode && (OnFoot() || _screen.IsOpen))
            {
                var res = Locator.GetPlayerBody()?.GetComponent<PlayerResources>();
                float health = res != null ? res.GetHealthFraction() : 1f;
                float oxygen = res != null ? res.GetOxygenFraction() : 1f;
                _hud.Draw(_inv, health, oxygen, !_screen.IsOpen && (_viewModel.Perspective == 0 || ToolOut())); // Gui.renderCrosshair: first person only
                _screen.OnGUI(_inv);
                if (_screen.IsOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
            DrawNotes();
        }

        // ---------------------------------------------------------------- log & notes

        public static void Log(string msg) => Instance?.ModHelper.Console.WriteLine("OuterCraft: " + msg, MessageType.Info);

        private static readonly List<(string text, float until)> Notes = new List<(string, float)>();

        public static void Notify(string text)
        {
            Log(text);
            Notes.Add((text, Time.unscaledTime + 3f));
        }

        private static GUIStyle _style;

        private static void DrawNotes()
        {
            if (Event.current.type != EventType.Repaint || Notes.Count == 0) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            Notes.RemoveAll(n => n.until < Time.unscaledTime);
            float y = 14;
            foreach (var n in Notes)
            {
                GUI.Label(new Rect(14, y, 800, 26), n.text, _style);
                y += 24;
            }
        }
    }
}
