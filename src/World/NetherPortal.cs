using System.Collections.Generic;
using System.Linq;
using OuterCraft.Assets;
using UnityEngine;
using Dir = OuterCraft.Assets.Dir;

namespace OuterCraft.World
{
    /// Minecraft's nether portal, except it doesn't go to the Nether: an obsidian frame lit with
    /// flint and steel opens onto Dark Bramble. Stand in it (4 s in survival, at once in creative),
    /// the purple swirl fills the screen, and you come out inside the Bramble's fog.
    public sealed class NetherPortal
    {
        private static readonly System.Random Rng = new System.Random();
        public static NetherPortal Instance;
        public NetherPortal() { Instance = this; }

        private readonly List<(PlanetBlocks pb, Cell cell)> _portals = new List<(PlanetBlocks, Cell)>();

        private float _time;          // seconds spent in a portal (Minecraft's portalTime)
        private bool _inside, _cooldown;
        public float Overlay { get; private set; }  // 0..1, the screen swirl

        // ---------------------------------------------------------------- lighting (PortalShape)

        /// Flint and steel used on `cell` (the air in front of the clicked face): a portal if it's
        /// inside an obsidian frame, along either horizontal axis.
        public bool TryIgnite(PlanetBlocks pb, Cell cell)
        {
            var portal = Blocks.Get("nether_portal");
            if (portal == null || pb.Get(cell) != 0) return false;
            foreach (var axis in new[] { Dir.East, Dir.South })
            {
                var cells = Shape(pb, cell, axis);
                if (cells == null) continue;
                int state = portal.Find(("axis", axis == Dir.East ? "x" : "z"));
                if (state < 0) state = 0;
                foreach (var c in cells)
                {
                    pb.Set(c, portal.Id, state);
                    _portals.Add((pb, c));
                }
                return true;
            }
            return false;
        }

        private static bool Obsidian(PlanetBlocks pb, Cell c) => pb.Def(c)?.Key == "obsidian";

        /// The empty rectangle (2..21 wide, 3..21 tall) inside an obsidian frame, or null.
        private static List<Cell> Shape(PlanetBlocks pb, Cell start, Dir axis)
        {
            var g = pb.Grid;
            var back = Dirs.Opposite(axis);
            // down to the frame's floor
            var bottom = start;
            for (int i = 0; i < 21 && pb.Get(g.Step(bottom, Dir.Down)) == 0; i++) bottom = g.Step(bottom, Dir.Down);
            if (!Obsidian(pb, g.Step(bottom, Dir.Down))) return null;
            // to the left edge
            var left = bottom;
            int guard = 0;
            while (pb.Get(g.Step(left, back)) == 0 && Obsidian(pb, g.Step(g.Step(left, back), Dir.Down)))
            {
                left = g.Step(left, back);
                if (++guard > 21) return null;
            }
            if (!Obsidian(pb, g.Step(left, back))) return null;
            // width along the axis
            var row = new List<Cell>();
            var c = left;
            for (int i = 0; i < 22; i++)
            {
                if (pb.Get(c) != 0) break;
                if (!Obsidian(pb, g.Step(c, Dir.Down))) return null;
                row.Add(c);
                c = g.Step(c, axis);
            }
            if (row.Count < 2 || row.Count > 21 || !Obsidian(pb, c)) return null;
            // rows upwards until the obsidian lintel
            var cells = new List<Cell>();
            for (int h = 0; h < 22; h++)
            {
                if (row.All(x => Obsidian(pb, x)))
                    return h >= 3 ? cells : null;
                if (h >= 21) return null;
                if (!Obsidian(pb, g.Step(row[0], back)) || !Obsidian(pb, g.Step(row[row.Count - 1], axis))) return null;
                if (row.Any(x => pb.Get(x) != 0)) return null;
                cells.AddRange(row);
                row = row.Select(x => g.Step(x, Dir.Up)).ToList();
            }
            return null;
        }

        /// A frame or portal block went: the whole portal goes out (NetherPortalBlock.updateShape).
        public void OnRemoved(PlanetBlocks pb, Cell cell)
        {
            var portal = Blocks.Get("nether_portal");
            if (portal == null) return;
            var todo = new Stack<Cell>();
            for (int i = 0; i < 6; i++) todo.Push(pb.Grid.Step(cell, (Dir)i));
            int n = 0;
            while (todo.Count > 0 && n < 1000)
            {
                var c = todo.Pop();
                if (pb.Get(c) != portal.Id) continue;
                pb.Set(c, 0);
                n++;
                for (int i = 0; i < 6; i++) todo.Push(pb.Grid.Step(c, (Dir)i));
            }
            _portals.RemoveAll(p => p.pb == null || p.pb.Get(p.cell) != portal.Id);
        }

        public void Clear()
        {
            _portals.Clear();
            _time = 0;
            Overlay = 0;
            _inside = false;
        }

        // ---------------------------------------------------------------- standing in it

        public void Update(bool creative)
        {
            McAssets.Animate(Time.time);
            var body = Locator.GetPlayerBody();
            var portal = Blocks.Get("nether_portal");
            if (body == null || portal == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _portals.RemoveAll(p => p.pb == null || p.pb.Get(p.cell) != portal.Id);

            bool inside = false;
            foreach (var pb in _portals.Select(p => p.pb).Distinct())
            {
                var feet = pb.transform.InverseTransformPoint(body.GetPosition() - body.transform.up * 0.6f);
                var head = pb.transform.InverseTransformPoint(body.GetPosition() + body.transform.up * 0.4f);
                if (pb.Get(pb.Grid.Locate(feet)) == portal.Id || pb.Get(pb.Grid.Locate(head)) == portal.Id) { inside = true; break; }
            }

            // ambient hum: each portal block, 1 in 100 per tick (we sample the nearest few)
            int ticks = Mathf.Max(1, Mathf.RoundToInt(dt * 20f));
            foreach (var (pb, cell) in _portals.Take(32))
            {
                if (Rng.Next(100 * 8) >= ticks) continue;
                var at = pb.transform.TransformPoint(pb.Grid.Center(cell));
                if ((at - body.GetPosition()).sqrMagnitude > 24 * 24) continue;
                McSounds.Play("portal.portal", at, pb.transform, 0.5f, (float)Rng.NextDouble() * 0.4f + 0.8f);
            }

            if (inside && !_cooldown)
            {
                if (!_inside) McSounds.Play2D("portal.trigger", 0.25f, (float)Rng.NextDouble() * 0.4f + 0.8f);
                _time += dt;
                float wait = creative ? 0.05f : 4f;
                if (_time >= wait)
                {
                    _time = 0;
                    _cooldown = true;
                    if (WarpToBramble()) McSounds.Play2D("portal.travel", 0.25f, (float)Rng.NextDouble() * 0.4f + 0.8f);
                }
            }
            else
            {
                _time = Mathf.Max(0, _time - dt * 2f);
                if (!inside) _cooldown = false;
            }
            _inside = inside;
            // the swirl rises over the wait and fades in half a second after
            if (inside && !_cooldown) Overlay = Mathf.Clamp01(_time / (creative ? 0.05f : 4f));
            else Overlay = Mathf.Max(0, Overlay - dt * 2f);
        }

        /// Into Dark Bramble's first dimension, the way flying into its entrance does it: the hub's
        /// outer fog volume receives the player (sectors, fog colour and music all follow).
        private static bool WarpToBramble()
        {
            var hub = Object.FindObjectsOfType<OuterFogWarpVolume>().FirstOrDefault(v => v.GetName() == OuterFogWarpVolume.Name.Hub);
            var detector = Locator.GetPlayerDetector()?.GetComponent<FogWarpDetector>();
            if (hub == null || detector == null)
            {
                OuterCraft.Log("portal: Dark Bramble not found");
                return false;
            }
            // come in at the hub's exit, facing inwards
            var exitWorld = hub._exits != null && hub._exits.Length > 0 ? hub.GetExitPosition(hub._exits[0]) : hub.transform.position + hub.transform.forward * hub.GetExitRadius();
            var local = hub.transform.InverseTransformPoint(exitWorld);
            if (local.sqrMagnitude < 1f) local = Vector3.forward * Mathf.Max(10f, hub.GetExitRadius());
            local *= 0.85f;
            var rot = Quaternion.LookRotation(-local.normalized, Vector3.up);
            hub.ReceiveWarpedDetector(detector, Vector3.zero, local, rot);
            OuterCraft.Notify("Dark Bramble");
            return true;
        }

        /// Gui.renderPortalOverlay: the portal texture over the whole screen.
        public void DrawOverlay()
        {
            if (Overlay <= 0.001f || Event.current.type != EventType.Repaint) return;
            var tex = McAssets.PortalFrame(Time.time);
            if (tex == null) return;
            float f = Overlay;
            if (f < 1f) { f *= f; f *= f; f = f * 0.8f + 0.2f; }
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }
    }
}
