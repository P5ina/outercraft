using OuterCraft.Assets;
using UnityEngine;
using static OuterCraft.UI.McGui;

namespace OuterCraft.UI
{
    /// Minecraft's death: the red "You died!" screen over Outer Wilds' own death, with a Minecraft
    /// death message for whatever got you, and the same message in chat when the next loop starts.
    public sealed class DeathScreen
    {
        public string PlayerName = "Steve";
        private bool _dead;
        private float _diedAt;
        private string _message;
        private static Texture2D _gradient;

        // carried over the scene reload into the next loop
        private static string _chatLine;
        private static float _chatUntil;
        private static DeathType? _lastDeath;

        public static DeathType? LastDeath => _lastDeath;

        public void OnDeath(DeathType type)
        {
            if (_dead) return;
            _dead = true;
            _diedAt = Time.unscaledTime;
            _message = Message(type, PlayerName);
            _lastDeath = type;
            OuterCraft.Log(_message);
        }

        /// Next loop: the message waits in chat for a while, like Minecraft's death message.
        public void OnNewLoop()
        {
            if (_dead && _message != null)
            {
                _chatLine = _message;
                _chatUntil = -1f; // starts counting when first drawn
            }
            _dead = false;
        }

        public static string Message(DeathType t, string n)
        {
            switch (t)
            {
                case DeathType.Impact: return $"{n} hit the ground too hard";
                case DeathType.Asphyxiation: return $"{n} forgot to breathe";
                case DeathType.Energy: return $"{n} was killed by magic";
                case DeathType.Supernova: return $"{n} was blown up by the Sun";
                case DeathType.Digestion: return $"{n} was eaten by an Anglerfish";
                case DeathType.BigBang: return $"{n} witnessed the birth of a universe";
                case DeathType.Crushed: return $"{n} was squished too much";
                case DeathType.Meditation: return $"{n} meditated into the next loop";
                case DeathType.TimeLoop: return $"{n} ran out of time";
                case DeathType.Lava: return $"{n} tried to swim in lava";
                case DeathType.BlackHole: return $"{n} fell out of the world";
                case DeathType.Dream: return $"{n} woke up";
                case DeathType.DreamExplosion: return $"{n} was killed by [Intentional Game Design]";
                case DeathType.CrushedByElevator: return $"{n} was squashed by an elevator";
                default: return $"{n} died";
            }
        }

        public void Draw()
        {
            if (!McAssets.Loaded) return;
            if (_dead) DrawScreen();
            else DrawChat();
        }

        /// DeathScreen: red gradient, "You died!" at twice the size, the cause, the score, two buttons.
        private void DrawScreen()
        {
            if (Event.current.type != EventType.Repaint) return;
            Begin();
            float a = Mathf.Clamp01((Time.unscaledTime - _diedAt) / 0.5f);
            // fillGradient(0x60500000 -> 0xA0803030): one smooth vertical gradient texture
            if (_gradient == null)
            {
                _gradient = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int i = 0; i < 256; i++)
                    _gradient.SetPixel(0, i, Color.Lerp(new Color(0x80 / 255f, 0x30 / 255f, 0x30 / 255f, 0xA0 / 255f),
                        new Color(0x50 / 255f, 0, 0, 0x60 / 255f), i / 255f)); // row 0 is the bottom
                _gradient.Apply(false, false);
            }
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _gradient, ScaleMode.StretchToFill, true);
            GUI.color = old;
            var white = new Color(1, 1, 1, a);
            TextCentered("You died!", W / 2f, 30f, white, 2f);
            TextCentered(_message ?? "", W / 2f, 85f, white);
            float sx = W / 2f - TextWidth("Score: 0") / 2f;
            Text("Score: ", sx, 100f, white);
            Text("0", sx + TextWidth("Score: ") + 1, 100f, new Color(1f, 1f, 0.33f, a));
            if (Time.unscaledTime - _diedAt > 1f) // the buttons come up after 20 ticks
            {
                Button("Respawn", W / 2f - 100f, H / 4f + 72f);
                Button("Title Screen", W / 2f - 100f, H / 4f + 96f);
            }
        }

        /// ChatComponent: a line with a dark background above the hotbar, fading out after 10 s.
        private void DrawChat()
        {
            if (_chatLine == null || Event.current.type != EventType.Repaint) return;
            if (_chatUntil < 0f) _chatUntil = Time.unscaledTime + 10f;
            float left = _chatUntil - Time.unscaledTime;
            if (left <= 0f) { _chatLine = null; return; }
            float a = Mathf.Clamp01(left);
            Begin();
            float y = H - 48f;
            Fill(0, y - 1, 320, 9, new Color(0, 0, 0, 0.5f * a));
            Text(_chatLine, 2, y, new Color(1, 1, 1, a));
        }
    }
}
