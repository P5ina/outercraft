using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.Player
{
    /// The signalscope as Minecraft's spyglass: the Outer Wilds prop is hidden and the hand holds a
    /// spyglass instead (its 3D in-hand model); zoomed in, Minecraft's spyglass scope frames the view
    /// (Gui.renderSpyglassOverlay). Frequencies, signals and the zoom itself stay Outer Wilds'.
    public sealed class Spyglass
    {
        public bool Out { get; private set; }
        public bool Zoomed { get; private set; }
        public float OutAt { get; private set; }
        public ItemDef Item => _item ?? (_item = Items.Get("spyglass"));

        private ItemDef _item;
        private Signalscope _scope;
        private Renderer[] _renderers;
        private bool _hidden;
        private float _scale = 0.5f;

        public void Clear()
        {
            _scope = null;
            _renderers = null;
            _hidden = false;
            Out = Zoomed = false;
        }

        public void Update(bool minecraftMode)
        {
            var swapper = Locator.GetToolModeSwapper();
            var sc = swapper != null ? swapper.GetSignalScope() : null;
            if (sc != _scope)
            {
                _scope = sc;
                _renderers = sc != null ? sc.GetComponentsInChildren<Renderer>(true) : null;
                _hidden = false;
            }
            bool hide = minecraftMode && _scope != null && Item != null;
            if (hide != _hidden && _renderers != null)
            {
                foreach (var r in _renderers) if (r != null) r.forceRenderingOff = hide;
                _hidden = hide;
            }

            bool outNow = hide && swapper.GetToolMode() == ToolMode.SignalScope;
            if (outNow && !Out) OutAt = Time.unscaledTime;
            Out = outNow;
            bool zoomed = Out && _scope.InZoomMode();
            if (zoomed && !Zoomed) _scale = 0.5f;
            Zoomed = zoomed;
            if (Zoomed) _scale = Mathf.Lerp(_scale, 1.125f, Mathf.Clamp01(Time.unscaledDeltaTime * 10f));
        }

        /// The scope texture in a square as big as the screen's short side (growing in as you zoom),
        /// black all around it.
        public void DrawScope()
        {
            if (!Zoomed || McAssets.SpyglassScope == null || Event.current.type != EventType.Repaint) return;
            float w = Screen.width, h = Screen.height;
            float size = Mathf.Floor(Mathf.Min(w, h) * _scale);
            float x = Mathf.Floor((w - size) / 2f), y = Mathf.Floor((h - size) / 2f);
            GUI.DrawTexture(new Rect(x, y, size, size), McAssets.SpyglassScope, ScaleMode.StretchToFill, true);
            var old = GUI.color;
            GUI.color = Color.black;
            var black = Texture2D.whiteTexture;
            if (y > 0) { GUI.DrawTexture(new Rect(0, 0, w, y), black); GUI.DrawTexture(new Rect(0, y + size, w, h - y - size), black); }
            if (x > 0) { GUI.DrawTexture(new Rect(0, Mathf.Max(0, y), x, Mathf.Min(size, h)), black); GUI.DrawTexture(new Rect(x + size, Mathf.Max(0, y), w - x - size, Mathf.Min(size, h)), black); }
            GUI.color = old;
        }
    }
}
