using OuterCraft.Assets;
using OuterCraft.UI;
using OuterCraft.World;
using UnityEngine;
using UnityEngine.InputSystem;
using Dir = OuterCraft.Assets.Dir;

namespace OuterCraft.Player
{
    /// Sleeping in a Minecraft bed: lie down, Minecraft's sleep darkness comes over the screen, and
    /// time runs fast like sleeping at an Outer Wilds campfire. Up with Shift, Space or "Leave Bed";
    /// the Sun going supernova gets you up too.
    public sealed class Sleep
    {
        public bool Asleep => _state != 0;

        private int _state;          // 0 awake, 1 lying down (attaching next frame), 2 in bed
        private PlanetBlocks _pb;
        private Cell _head;
        private Dir _facing;
        private GameObject _attachGo;
        private PlayerAttachPoint _attach;
        private float _since;
        private bool _fastForward;
        private float _mult = 1f, _savedFixed, _savedMaxDelta;

        private const string SunMessage = "You may not rest now; the Sun is exploding";

        /// The supernova's last minute: no sleeping through that.
        private static bool SunExploding => TimeLoop.IsTimeFlowing() && TimeLoop.GetSecondsRemaining() < 60f;

        public void TryStart(PlanetBlocks pb, Cell cell)
        {
            if (_state != 0 || pb == null) return;
            var def = pb.Def(cell);
            int state = pb.State(cell);
            if (def == null || !Dirs.TryParse(def.Prop(state, "facing"), out var facing)) return;
            if (SunExploding) { McHud.Overlay(SunMessage); return; }
            var head = def.Prop(state, "part") == "head" ? cell : pb.Grid.Step(cell, facing);
            if (pb.Def(head) != def) return;

            _pb = pb;
            _head = head;
            _facing = facing;
            _attachGo = new GameObject("OuterCraft_Bed");
            _attachGo.transform.SetParent(pb.transform, false);
            Place(_attachGo.transform);
            _attach = _attachGo.AddComponent<PlayerAttachPoint>();
            _attach._lockPlayerTurning = true;
            _attach._matchRotation = true;
            _attach._centerCamera = true;
            _state = 1;
            _since = Time.unscaledTime;
        }

        /// Where the player is held: standing on the pillow, facing the foot of the bed.
        private void Place(Transform t)
        {
            var fr = new CubeSphere.CellFrame();
            fr.Set(_pb.Grid, _head);
            var up = fr.EH;
            var toFoot = -fr.D(Dirs.Vec[(int)_facing]);
            t.localPosition = fr.P(new Vector3(0.5f, 0.5625f, 0.5f)) + up * 0.05f;
            t.localRotation = Quaternion.LookRotation(toFoot, up);
        }

        /// Camera.setup for a sleeper: on the pillow (0.6875 + eye 0.2 + 0.3 up), looking at the foot.
        public Matrix4x4? View()
        {
            if (_state != 2 || _pb == null) return null;
            var fr = new CubeSphere.CellFrame();
            fr.Set(_pb.Grid, _head);
            var eye = _pb.transform.TransformPoint(fr.P(new Vector3(0.5f, 1.1875f, 0.5f)));
            var up = _pb.transform.TransformDirection(fr.EH);
            var toFoot = _pb.transform.TransformDirection(-fr.D(Dirs.Vec[(int)_facing]));
            return Matrix4x4.TRS(eye, Quaternion.LookRotation(toFoot, up), Vector3.one);
        }

        public void Update(bool minecraftMode)
        {
            if (_state == 0) return;
            if (_pb == null || _attachGo == null || PlayerState.IsDead() || !minecraftMode || _pb.Def(_head)?.Place != PlaceKind.Bed)
            {
                Stop();
                return;
            }
            if (_state == 1)
            {
                // PlayerAttachPoint picks up the player in its Start: attach the frame after
                if (Time.unscaledTime - _since < 0.05f) return;
                _attach.AttachPlayer();
                OWInput.ChangeInputMode(InputMode.Menu);
                _state = 2;
                _since = Time.unscaledTime;
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (SunExploding) { McHud.Overlay(SunMessage); Stop(); return; }

            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
            {
                Stop();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Time.unscaledTime - _since > 0.3f)
            {
                McGui.Begin();
                var p = mouse.position.ReadValue();
                var m = new Vector2(p.x, Screen.height - p.y) / McGui.S;
                if (LeaveRect().Contains(m)) { McSounds.Click(); Stop(); return; }
            }

            // after Minecraft's 100 ticks of falling asleep, time runs like at a campfire
            float asleepFor = Time.unscaledTime - _since;
            if (asleepFor > 2f && !_fastForward)
            {
                _fastForward = true;
                _mult = 1f;
                _savedFixed = OWTime.GetFixedTimestep();
                _savedMaxDelta = OWTime.GetMaxDeltaTime();
                OWTime.SetFixedTimestep(1f / 30f);
                OWTime.SetMaxDeltaTime(1f / 30f);
                GlobalMessenger.FireEvent("StartFastForward");
            }
            if (_fastForward)
            {
                _mult = Mathf.Min(_mult + Time.unscaledDeltaTime * 3f, 10f);
                OWTime.SetTimeScale(_mult);
            }
        }

        public void Stop()
        {
            if (_state == 0) return;
            if (_fastForward)
            {
                _fastForward = false;
                OWTime.SetTimeScale(1f);
                OWTime.SetFixedTimestep(_savedFixed > 0f ? _savedFixed : 1f / 60f);
                OWTime.SetMaxDeltaTime(_savedMaxDelta > 0f ? _savedMaxDelta : 1f / 15f);
                GlobalMessenger.FireEvent("EndFastForward");
            }
            if (_state == 2 && _attach != null)
            {
                _attach.DetachPlayer();
                if (OWInput.IsInputMode(InputMode.Menu)) OWInput.ChangeInputMode(InputMode.Character);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (_attachGo != null) Object.Destroy(_attachGo);
            _attachGo = null;
            _attach = null;
            _pb = null;
            _state = 0;
        }

        /// A new loop or a scene change: everything's gone already, just forget it.
        public void Forget()
        {
            if (_fastForward) OWTime.SetTimeScale(1f);
            _fastForward = false;
            _attachGo = null;
            _attach = null;
            _pb = null;
            _state = 0;
        }

        private static Rect LeaveRect() => new Rect(McGui.W / 2f - 100f, McGui.H - 40f, 200f, 20f);

        /// Gui.renderSleepOverlay (0x101020, up to 220/255 over 100 ticks) and InBedChatScreen's button.
        public void Draw()
        {
            if (_state != 2 || Event.current.type != EventType.Repaint) return;
            McGui.Begin();
            float f = Mathf.Clamp01((Time.unscaledTime - _since) / 5f);
            McGui.Fill(0, 0, McGui.W + 1, McGui.H + 1, new Color(16 / 255f, 16 / 255f, 32 / 255f, f * 220f / 255f));
            var r = LeaveRect();
            var mouse = Mouse.current;
            bool hover = false;
            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                hover = r.Contains(new Vector2(p.x, Screen.height - p.y) / McGui.S);
            }
            McGui.Button("Leave Bed", r.x, r.y, r.width, r.height, hover);
        }
    }
}
