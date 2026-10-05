using System;
using System.Collections.Generic;
using OuterCraft.Assets;
using OuterCraft.World;
using UnityEngine;
using UnityEngine.InputSystem;
using Dir = OuterCraft.Assets.Dir;

namespace OuterCraft.Player
{
    /// Minecraft's hands: the targeted block (exact shapes for torches, slabs, doors...), mining with
    /// break times, cracks, hit sounds and particles (survival) or instant breaking (creative),
    /// drops, placing with block states (stairs facing, slab halves, wall torches, doors), using
    /// doors and crafting tables.
    public sealed class BlockInteraction
    {
        public float Reach = 5f;
        public Action Swung;                 // the hand animates
        public Action OpenCrafting;          // right click on a crafting table
        public Combat Combat;
        public Elytra Elytra;
        private bool _attackPress;           // this left click hit someone: no mining until released

        public struct Target
        {
            public PlanetBlocks Pb;
            public Cell Cell;
            public Dir Face;
            public Vector3 Frac;   // hit point in the cell, Minecraft block coords
            public Bounds Box;     // the shape box that was hit
            public float Dist;
        }

        public Target? Hit { get; private set; }
        private RaycastHit? _ground;          // Outer Wilds terrain under the crosshair

        private float _nextBreak, _nextPlace;
        private float _progress;
        private Cell _miningCell;
        private PlanetBlocks _miningPb;
        private float _tickAcc;
        private int _ticks;

        private GameObject _outlineGo, _crackGo;
        private Mesh _outlineMesh, _crackMesh;

        public BlockDef TargetBlock => Hit.HasValue ? Hit.Value.Pb.Def(Hit.Value.Cell) : null;

        public void Update(Camera cam, Inventory inv, bool allowed)
        {
            Hit = null;
            _ground = null;
            if (!allowed || cam == null)
            {
                ShowOutline(null);
                ResetMining();
                return;
            }

            Raycast(cam);
            ShowOutline(Hit);

            var mouse = Mouse.current;
            if (mouse == null) return;
            float now = Time.unscaledTime;

            // ticks (Minecraft runs these things 20 times a second)
            _tickAcc += Mathf.Min(Time.deltaTime, 0.1f) * 20f;
            int newTicks = (int)_tickAcc;
            _tickAcc -= newTicks;

            // ---- attack: a character in front of the block wins
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _attackPress = false;
                float blockDist = Hit.HasValue ? Hit.Value.Dist : _ground.HasValue ? _ground.Value.distance : Reach;
                if (Combat != null && Combat.Target(cam, blockDist, out var npc, out _))
                {
                    Combat.Attack(npc, inv.CurrentItem, cam);
                    _attackPress = true;
                    Swung?.Invoke();
                }
            }
            if (!mouse.leftButton.isPressed) _attackPress = false;

            // ---- break
            if (mouse.leftButton.wasPressedThisFrame && !_attackPress) { _nextBreak = Mathf.Min(_nextBreak, now); Swung?.Invoke(); }
            if (mouse.leftButton.isPressed && !_attackPress && Hit.HasValue && now >= _nextBreak) Mine(inv, newTicks);
            else ResetMining();

            // ---- pick
            if (mouse.middleButton.wasPressedThisFrame && TargetBlock != null) inv.Pick(TargetBlock);

            // ---- use / place
            if (mouse.rightButton.wasPressedThisFrame) _nextPlace = 0;
            if (mouse.rightButton.isPressed && now >= _nextPlace)
            {
                _nextPlace = now + 0.2f;
                if (Use(inv)) Swung?.Invoke();
            }

            // ---- drop (Q, Ctrl+Q the whole stack)
            var kb = Keyboard.current;
            if (kb != null && kb.qKey.wasPressedThisFrame && inv.CurrentItem != null)
            {
                bool all = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                var stack = inv.TakeFromCurrent(all ? inv.Current.Count : 1);
                if (stack != null) { Throw(cam, stack); Swung?.Invoke(); }
            }
        }

        /// LocalPlayer.drop: out of the eyes, 0.3 blocks/tick forward, picked up again after 2 s.
        public static void Throw(Camera cam, ItemStack stack)
        {
            var body = Locator.GetPlayerBody();
            if (body == null || ItemEntities.Instance == null) return;
            var frame = Locator.GetPlayerController()?.GetLastGroundBody()?.transform ?? body.transform.parent;
            if (frame == null) return;
            var at = cam.transform.position - body.transform.up * 0.3f;
            var vel = cam.transform.forward * 0.3f * 20f + body.transform.up * 0.1f * 20f;
            ItemEntities.Instance.Spawn(frame, at, stack, vel, 2f);
        }

        // ---------------------------------------------------------------- targeting

        private void Raycast(Camera cam)
        {
            var origin = cam.transform.position;
            var dir = cam.transform.forward;
            float limit = Reach;

            // Outer Wilds ground (and anything else solid that isn't ours)
            var hits = Physics.RaycastAll(origin, dir, Reach, OWLayerMask.physicalMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PlanetBlocks>() != null) continue;
                if (Locator.GetPlayerBody() != null && h.collider.attachedRigidbody != null &&
                    h.collider.attachedRigidbody.gameObject == Locator.GetPlayerBody().gameObject) continue;
                best = h.distance;
                _ground = h;
            }
            if (_ground.HasValue) limit = _ground.Value.distance;

            // our blocks: march along the ray through the cube-sphere grid, testing each block's real shape
            if (BlockWorld.Instance == null) return;
            foreach (var pb in BlockWorld.Instance.Planets)
            {
                var o = pb.transform.InverseTransformPoint(origin);
                var d = pb.transform.InverseTransformDirection(dir).normalized;
                if (o.magnitude > pb.MaxH + Reach + 3f) continue;
                const float step = 0.02f;
                var prev = o;
                for (float t = 0; t <= limit; t += step)
                {
                    var p = o + d * t;
                    var cell = pb.Grid.Locate(p);
                    var def = pb.Def(cell);
                    if (def == null) { prev = p; continue; }
                    var f = pb.Grid.Frac(cell, p);
                    bool inside = false;
                    foreach (var box in Boxes(pb, cell, def))
                    {
                        if (!box.Contains(f)) continue;
                        inside = true;
                        if (!Hit.HasValue || t < Hit.Value.Dist)
                        {
                            var fp = pb.Grid.Frac(cell, prev);
                            Hit = new Target { Pb = pb, Cell = cell, Frac = f, Box = box, Dist = t, Face = EntryFace(box, fp, f) };
                        }
                        break;
                    }
                    if (inside) break;
                    prev = p;
                }
            }
            if (Hit.HasValue) _ground = null;
        }

        private static readonly List<Bounds> UnitBox = new List<Bounds> { new Bounds(Vector3.one * 0.5f, Vector3.one) };

        public static List<Bounds> Boxes(PlanetBlocks pb, Cell c, BlockDef def)
        {
            if (!def.IsModel) return UnitBox;
            var m = pb.ModelAt(c);
            return m != null && m.Boxes.Count > 0 ? m.Boxes : UnitBox;
        }

        /// Which face of the box the segment prev -> cur went in through (slab test).
        private static Dir EntryFace(Bounds box, Vector3 prev, Vector3 cur)
        {
            float bestT = -1f;
            Dir face = Dir.Up;
            var d = cur - prev;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-6f) continue;
                float plane = d[a] > 0 ? box.min[a] : box.max[a];
                float t = (plane - prev[a]) / d[a];
                if (t > bestT && t <= 1.0001f)
                {
                    bestT = t;
                    face = a == 0 ? (d[a] > 0 ? Dir.West : Dir.East) : a == 1 ? (d[a] > 0 ? Dir.Down : Dir.Up) : (d[a] > 0 ? Dir.North : Dir.South);
                }
            }
            if (bestT < 0f)
            {
                // started inside: nearest face
                float m = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    var n = Dirs.Vec[i];
                    int a = n.x != 0 ? 0 : n.y != 0 ? 1 : 2;
                    float dist = n[a] > 0 ? box.max[a] - cur[a] : cur[a] - box.min[a];
                    if (dist < m) { m = dist; face = (Dir)i; }
                }
            }
            return face;
        }

        // ---------------------------------------------------------------- mining

        private void ResetMining()
        {
            _progress = 0;
            _miningPb = null;
            ShowCrack(null, default, 0);
        }

        private void Mine(Inventory inv, int ticks)
        {
            var h = Hit.Value;
            var def = h.Pb.Def(h.Cell);
            if (def == null) return;
            if (_miningPb != h.Pb || !_miningCell.Equals(h.Cell))
            {
                _miningPb = h.Pb;
                _miningCell = h.Cell;
                _progress = 0;
                _ticks = 0;
            }

            if (inv.Creative)
            {
                Break(h.Pb, h.Cell, false, inv.CurrentItem);
                _nextBreak = Time.unscaledTime + 0.25f; // creative: 5 ticks between blocks
                Swung?.Invoke();
                ResetMining();
                return;
            }

            float perTick = Items.BreakPerTick(inv.CurrentItem, def);
            if (perTick >= 1f) // instant (torches, flowers, or a fast enough tool)
            {
                Break(h.Pb, h.Cell, true, inv.CurrentItem);
                _nextBreak = Time.unscaledTime + 0.05f;
                Swung?.Invoke();
                ResetMining();
                return;
            }

            Swung?.Invoke(); // the arm keeps swinging while mining
            var particles = h.Pb.GetComponent<BlockParticles>();
            var at = h.Pb.transform.TransformPoint(h.Pb.Grid.Center(h.Cell));
            for (int i = 0; i < ticks; i++)
            {
                if (_ticks % 4 == 0) McSounds.Hit(def, at, h.Pb.transform);
                _ticks++;
                _progress += perTick;
                particles?.Crack(h.Pb.Grid, h.Cell, def, h.Face, h.Box);
            }
            if (_progress >= 1f)
            {
                Break(h.Pb, h.Cell, true, inv.CurrentItem);
                _nextBreak = Time.unscaledTime + 0.25f; // destroyDelay = 5 ticks
                ResetMining();
                return;
            }
            ShowCrack(h.Pb, h.Cell, Mathf.Clamp((int)(_progress * 10f), 0, 9));
        }

        /// Removes a block with its sound and particles; drops (survival) and anything hanging off it.
        public static void Break(PlanetBlocks pb, Cell cell, bool drops, ItemDef tool, int depth = 0)
        {
            var def = pb.Def(cell);
            if (def == null) return;
            int state = pb.State(cell);
            var center = pb.transform.TransformPoint(pb.Grid.Center(cell));
            var up = pb.transform.TransformDirection(pb.Grid.UpAt(cell));

            // the other half of a door goes too (and drops nothing)
            if (def.Place == PlaceKind.Door)
            {
                var other = pb.Grid.Step(cell, def.Prop(state, "half") == "upper" ? Dir.Down : Dir.Up);
                if (pb.Def(other) == def) pb.Set(other, 0); // no drop from the other half: one door
            }
            // and the other half of a bed (it drops once, from whichever half was broken)
            if (def.Place == PlaceKind.Bed && Dirs.TryParse(def.Prop(state, "facing"), out var bf))
            {
                var other = pb.Grid.Step(cell, def.Prop(state, "part") == "head" ? Dirs.Opposite(bf) : bf);
                if (pb.Def(other) == def) pb.Set(other, 0);
            }

            pb.Set(cell, 0);
            NetherPortal.Instance?.OnRemoved(pb, cell);
            if (depth == 0) UI.Advancements.OnMined(def.Key);
            McSounds.Break(def, center, pb.transform);
            pb.GetComponent<BlockParticles>()?.Burst(pb.Grid, cell, def);

            if (drops && Items.CanHarvest(tool, def) && ItemEntities.Instance != null)
            {
                int count = def.Place == PlaceKind.Slab && def.Prop(state, "type") == "double" ? 2 : 1;
                foreach (var stack in DropsOf(def, count))
                    ItemEntities.Instance.SpawnFromBlock(pb.transform, center, up, stack);
            }

            // Block.canSurvive: whatever hangs on this block falls off
            if (depth > 16) return;
            for (int i = 0; i < 6; i++)
            {
                var d = (Dir)i;
                var n = pb.Grid.Step(cell, d);
                var nd = pb.Def(n);
                if (nd == null) continue;
                var support = nd.Support(pb.State(n));
                if (support.HasValue && support.Value == Dirs.Opposite(d)) Break(pb, n, drops, null, depth + 1);
            }
        }

        private static readonly System.Random Rng = new System.Random();

        private static IEnumerable<ItemStack> DropsOf(BlockDef def, int times)
        {
            for (int t = 0; t < times; t++)
            {
                if (def.Drop == "") yield break;
                if (Rng.NextDouble() >= def.DropChance) continue;
                var item = def.Drop != null ? Items.Get(def.Drop) : Items.Of(def);
                if (item == null) continue;
                int n = Rng.Next(def.DropMin, def.DropMax + 1);
                if (n > 0) yield return new ItemStack(item, n);
            }
        }

        // ---------------------------------------------------------------- use / place

        /// Right click on a bed: lie down in it.
        public System.Action<PlanetBlocks, Cell> UseBed;

        private bool Use(Inventory inv)
        {
            var kb = Keyboard.current;
            bool sneaking = kb != null && kb.spaceKey.isPressed; // Outer Wilds' crouch is the sneak

            // interacting with the block comes first (unless sneaking with something in hand)
            if (Hit.HasValue && !(sneaking && inv.CurrentItem != null))
            {
                var h = Hit.Value;
                var def = h.Pb.Def(h.Cell);
                if (def != null && def.Place == PlaceKind.Door)
                {
                    ToggleDoor(h.Pb, h.Cell);
                    return true;
                }
                if (def != null && def.Place == PlaceKind.Bed && UseBed != null)
                {
                    UseBed(h.Pb, h.Cell);
                    return true;
                }
                if (def != null && def.Key == "crafting_table")
                {
                    OpenCrafting?.Invoke();
                    return true;
                }
            }

            var item = inv.CurrentItem;
            if (item?.Key == "elytra")
            {
                // Equipable: right click puts it on (swapping with what's worn)
                var worn = inv.Chest;
                inv.Chest = inv.Current;
                inv.Slots[inv.Selected] = worn;
                inv.Dirty = true;
                McSounds.Click();
                return true;
            }
            if (item?.Key == "firework_rocket")
            {
                if (Elytra != null && Elytra.Gliding)
                {
                    if (Elytra.Boost()) { inv.UseOne(); return true; }
                    return false;
                }
                // launched from the ground, like using it on a block
                Vector3 at; Transform frame; Vector3 up;
                if (Hit.HasValue)
                {
                    var h = Hit.Value;
                    var fr = new CubeSphere.CellFrame();
                    fr.Set(h.Pb.Grid, h.Cell);
                    at = h.Pb.transform.TransformPoint(fr.P(h.Frac));
                    frame = h.Pb.transform;
                    up = h.Pb.transform.TransformDirection(fr.EH);
                }
                else if (_ground.HasValue)
                {
                    var planet = BlockWorld.PlanetOf(_ground.Value.collider);
                    if (planet == null) return false;
                    at = _ground.Value.point;
                    frame = planet.transform;
                    up = (at - planet.transform.position).normalized;
                }
                else return false;
                Fx.Instance?.Launch(frame, at, up);
                inv.UseOne();
                return true;
            }
            if (item?.Key == "flint_and_steel")
            {
                if (!Hit.HasValue) return false;
                var h = Hit.Value;
                var at = h.Pb.transform.TransformPoint(h.Pb.Grid.Center(h.Cell));
                McSounds.Play("fire.ignite", at, h.Pb.transform, 1f, (float)Rng.NextDouble() * 0.4f + 0.8f);
                NetherPortal.Instance?.TryIgnite(h.Pb, h.Pb.Grid.Step(h.Cell, h.Face));
                return true;
            }
            var block = item?.Block;
            if (block == null) return false;
            if (block.Key == "torch") return PlaceTorch(inv);
            return Place(inv, block);
        }

        private static void ToggleDoor(PlanetBlocks pb, Cell cell)
        {
            var def = pb.Def(cell);
            int state = pb.State(cell);
            bool open = def.Prop(state, "open") != "true";
            var other = pb.Grid.Step(cell, def.Prop(state, "half") == "upper" ? Dir.Down : Dir.Up);
            pb.Set(cell, def.Id, def.With(state, "open", open ? "true" : "false"));
            if (pb.Def(other) == def) pb.Set(other, def.Id, def.With(pb.State(other), "open", open ? "true" : "false"));
            McSounds.Door(open, pb.transform.TransformPoint(pb.Grid.Center(cell)), pb.transform);
        }

        /// Where a new block would go and against which face, from the current target.
        private bool PlaceSpot(out PlanetBlocks pb, out Cell cell, out Dir face, out float hitY)
        {
            pb = null; cell = default; face = Dir.Up; hitY = 0.5f;
            if (Hit.HasValue)
            {
                var h = Hit.Value;
                pb = h.Pb;
                face = h.Face;
                var target = pb.Def(h.Cell);
                // replaceable plants are replaced in place, like Minecraft
                cell = target != null && target.Place == PlaceKind.Plant && target.Key != "oak_sapling" ? h.Cell : pb.Grid.Step(h.Cell, face);
                hitY = Dirs.Horizontal(face) ? h.Frac.y : (face == Dir.Down ? 1f : 0f);
                return true;
            }
            if (!_ground.HasValue) return false;
            var g = _ground.Value;
            var planet = BlockWorld.PlanetOf(g.collider);
            if (planet == null) return false; // the ship, the probe, a moving prop
            float radius = (g.point - planet.transform.position).magnitude;
            pb = BlockWorld.Instance.GetOrCreate(planet, radius, g.collider.gameObject.layer);
            var local = pb.transform.InverseTransformPoint(g.point + g.normal * 0.02f);
            cell = pb.Grid.Locate(local);
            var up = pb.Grid.UpAt(cell);
            var n = pb.transform.InverseTransformDirection(g.normal);
            if (Vector3.Dot(n, up) > 0.5f)
            {
                // on the ground: the layer whose floor is nearest the surface here, so the block sits
                // on it (at most half a block in or above, where the ground isn't level with the grid)
                face = Dir.Up;
                float r = local.magnitude;
                int k = Mathf.RoundToInt(r - pb.Grid.Offset);
                var dir = local / Mathf.Max(r, 1e-3f);
                local = dir * (k + 0.5f + pb.Grid.Offset);
                cell = pb.Grid.Locate(local);
                hitY = 0f;
                return true;
            }
            else if (Vector3.Dot(n, up) < -0.5f) face = Dir.Down;
            else
            {
                var fr = new CubeSphere.CellFrame();
                fr.Set(pb.Grid, cell);
                face = Dirs.Nearest(new Vector3(Vector3.Dot(n, fr.EU), 0, Vector3.Dot(n, fr.EV)));
            }
            hitY = pb.Grid.Frac(cell, local).y;
            return true;
        }

        /// The horizontal direction the player looks, in the block grid (Minecraft's getHorizontalDirection).
        private static Dir Facing(PlanetBlocks pb, Cell cell)
        {
            var cam = Locator.GetPlayerCamera();
            if (cam == null) return Dir.North;
            var fr = new CubeSphere.CellFrame();
            fr.Set(pb.Grid, cell);
            var f = pb.transform.InverseTransformDirection(cam.transform.forward);
            float a = Vector3.Dot(f, fr.EU), b = Vector3.Dot(f, fr.EV);
            if (Mathf.Abs(a) > Mathf.Abs(b)) return a > 0 ? Dir.East : Dir.West;
            return b > 0 ? Dir.South : Dir.North;
        }

        private static string Name(Dir d) => Dirs.Name[(int)d];

        private bool PlaceTorch(Inventory inv)
        {
            if (!PlaceSpot(out var pb, out var cell, out var face, out _)) return false;
            if (face == Dir.Down) return false;
            if (face == Dir.Up) return Commit(inv, pb, cell, Blocks.Get("torch"), 0);
            var wall = Blocks.Get("wall_torch");
            int s = wall.Find(("facing", Name(face)));
            return Commit(inv, pb, cell, wall, s < 0 ? 0 : s);
        }

        private bool Place(Inventory inv, BlockDef block)
        {
            if (!PlaceSpot(out var pb, out var cell, out var face, out float hitY)) return false;
            int state = block.DefaultState;

            // slab on slab: a double slab
            if (block.Place == PlaceKind.Slab)
            {
                if (Hit.HasValue && Hit.Value.Pb.Def(Hit.Value.Cell) == block)
                {
                    var h = Hit.Value;
                    string type = block.Prop(h.Pb.State(h.Cell), "type");
                    if ((type == "bottom" && h.Face == Dir.Up) || (type == "top" && h.Face == Dir.Down))
                        return Commit(inv, h.Pb, h.Cell, block, Math.Max(0, block.Find(("type", "double"))), replace: true);
                }
                if (pb.Def(cell) == block)
                {
                    string type = block.Prop(pb.State(cell), "type");
                    if (type != "double")
                        return Commit(inv, pb, cell, block, Math.Max(0, block.Find(("type", "double"))), replace: true);
                }
            }

            bool upperHalf = face == Dir.Down || (Dirs.Horizontal(face) && hitY > 0.5f);
            Dir look = Facing(pb, cell);
            switch (block.Place)
            {
                case PlaceKind.Stairs:
                    state = block.Find(("facing", Name(look)), ("half", upperHalf ? "top" : "bottom"), ("shape", "straight"));
                    break;
                case PlaceKind.Slab:
                    state = block.Find(("type", upperHalf ? "top" : "bottom"));
                    break;
                case PlaceKind.Lantern:
                    state = block.Find(("hanging", face == Dir.Down ? "true" : "false"));
                    break;
                case PlaceKind.WallAttached:
                    if (!Dirs.Horizontal(face)) return false;
                    state = block.Find(("facing", Name(face)));
                    break;
                case PlaceKind.FacingAway:
                    state = block.Find(("facing", Name(Dirs.Opposite(look))), ("lit", "false"));
                    if (state < 0) state = block.Find(("facing", Name(Dirs.Opposite(look))));
                    break;
                case PlaceKind.Plant:
                    if (face != Dir.Up) return false;
                    break;
                case PlaceKind.Bed:
                {
                    // BedBlock: the foot where you click, the head one block on, the way you look
                    if (face != Dir.Up) return false;
                    var headCell = pb.Grid.Step(cell, look);
                    var ex = pb.Def(headCell);
                    if (ex != null && !(ex.Place == PlaceKind.Plant && ex.Key != "oak_sapling")) return false;
                    int foot = block.Find(("facing", Name(look)), ("part", "foot"));
                    int head = block.Find(("facing", Name(look)), ("part", "head"));
                    if (foot < 0 || head < 0) return false;
                    if (!Commit(inv, pb, cell, block, foot)) return false;
                    pb.Set(headCell, block.Id, head);
                    return true;
                }
                case PlaceKind.Door:
                {
                    var above = pb.Grid.Step(cell, Dir.Up);
                    if (pb.Get(above) != 0) return false;
                    int lower = block.Find(("facing", Name(look)), ("half", "lower"), ("hinge", "left"), ("open", "false"));
                    int upper = block.Find(("facing", Name(look)), ("half", "upper"), ("hinge", "left"), ("open", "false"));
                    if (lower < 0 || upper < 0) return false;
                    if (!Commit(inv, pb, cell, block, lower)) return false;
                    pb.Set(above, block.Id, upper);
                    return true;
                }
            }
            if (state < 0) state = block.DefaultState;
            return Commit(inv, pb, cell, block, state);
        }

        private bool Commit(Inventory inv, PlanetBlocks pb, Cell cell, BlockDef block, int state, bool replace = false)
        {
            var existing = pb.Def(cell);
            if (existing != null && !replace && !(existing.Place == PlaceKind.Plant && existing.Key != "oak_sapling")) return false;
            if (block.Collide && !replace && IntersectsPlayer(pb, cell)) return false;
            pb.Set(cell, block.Id, state);
            McSounds.Place(block, pb.transform.TransformPoint(pb.Grid.Center(cell)), pb.transform);
            inv.UseOne();
            return true;
        }

        /// Don't build a block into ourselves (Minecraft's rule).
        private static bool IntersectsPlayer(PlanetBlocks pb, Cell cell)
        {
            var body = Locator.GetPlayerBody();
            if (body == null) return false;
            var cap = body.GetComponentInChildren<CapsuleCollider>();
            var center = pb.transform.TransformPoint(pb.Grid.Center(cell));
            if (cap == null) return (center - body.GetPosition()).magnitude < 1.2f;
            var t = cap.transform;
            float half = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
            var up = t.up;
            var c = t.TransformPoint(cap.center);
            var a = c - up * half;
            var b = c + up * half;
            var ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(center - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float dist = (center - (a + ab * k)).magnitude;
            return dist < cap.radius + 0.45f;
        }

        // ---------------------------------------------------------------- outline (one box per model element)

        private void ShowOutline(Target? target)
        {
            if (!target.HasValue)
            {
                if (_outlineGo != null) _outlineGo.SetActive(false);
                return;
            }
            var h = target.Value;
            var pb = h.Pb;
            if (_outlineGo == null)
            {
                _outlineGo = new GameObject("OuterCraft_Outline");
                _outlineMesh = new Mesh { name = "outline" };
                _outlineMesh.MarkDynamic();
                _outlineGo.AddComponent<MeshFilter>().sharedMesh = _outlineMesh;
                var mr = _outlineGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PlanetBlocks.Material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_outlineGo.transform.parent != pb.transform) _outlineGo.transform.SetParent(pb.transform, false);
            _outlineGo.SetActive(true);

            var frame = new CubeSphere.CellFrame();
            frame.Set(pb.Grid, h.Cell);
            _pos.Clear(); _idx.Clear(); _uv2.Clear(); _col.Clear();
            var def = pb.Def(h.Cell);
            var boxes = def != null ? Boxes(pb, h.Cell, def) : UnitBox;
            foreach (var box in boxes)
            {
                var mn = box.min - Vector3.one * 0.002f;
                var mx = box.max + Vector3.one * 0.002f;
                var corner = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corner[i] = frame.P(new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 4) == 0 ? mn.y : mx.y, (i & 2) == 0 ? mn.z : mx.z));
                var center = frame.P(box.center);
                for (int i = 0; i < 8; i++)
                    for (int bit = 0; bit < 3; bit++)
                    {
                        int j = i | (1 << bit);
                        if (j != i) Edge(corner[i], corner[j], center);
                    }
            }
            _outlineMesh.Clear();
            _outlineMesh.SetVertices(_pos);
            _outlineMesh.SetUVs(1, _uv2);
            _outlineMesh.SetColors(_col);
            _outlineMesh.SetTriangles(_idx, 0, true);
            _outlineMesh.RecalculateNormals();
        }

        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<Vector3> _uv2 = new List<Vector3>();
        private readonly List<Color32> _col = new List<Color32>();
        private readonly List<int> _idx = new List<int>();

        /// A thin square tube from a to b (4 sides, double-sided so winding never matters).
        private void Edge(Vector3 a, Vector3 b, Vector3 center)
        {
            const float w = 0.006f;
            var d = (b - a).normalized;
            var side = Vector3.Cross(d, ((a + b) * 0.5f - center).normalized).normalized * w;
            var up = Vector3.Cross(d, side).normalized * w;
            var o = new[] { side + up, side - up, -side - up, -side + up };
            for (int k = 0; k < 4; k++)
            {
                var p0 = a + o[k];
                var p1 = a + o[(k + 1) % 4];
                int i = _pos.Count;
                _pos.Add(p0); _pos.Add(p1); _pos.Add(b + o[(k + 1) % 4]); _pos.Add(b + o[k]);
                for (int n = 0; n < 4; n++)
                {
                    _uv2.Add(new Vector3(0f, 1f, 4f)); // flag 4: untextured, colour only
                    _col.Add(new Color32(0, 0, 0, 255));
                }
                _idx.Add(i); _idx.Add(i + 1); _idx.Add(i + 2); _idx.Add(i); _idx.Add(i + 2); _idx.Add(i + 3);
                _idx.Add(i); _idx.Add(i + 2); _idx.Add(i + 1); _idx.Add(i); _idx.Add(i + 3); _idx.Add(i + 2);
            }
        }

        // ---------------------------------------------------------------- crack overlay (destroy_stage_0..9)

        private void ShowCrack(PlanetBlocks pb, Cell cell, int stage)
        {
            if (pb == null)
            {
                if (_crackGo != null) _crackGo.SetActive(false);
                return;
            }
            if (_crackGo == null)
            {
                _crackGo = new GameObject("OuterCraft_Crack");
                _crackMesh = new Mesh { name = "crack" };
                _crackMesh.MarkDynamic();
                _crackGo.AddComponent<MeshFilter>().sharedMesh = _crackMesh;
                var mr = _crackGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = PlanetBlocks.Material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_crackGo.transform.parent != pb.transform) _crackGo.transform.SetParent(pb.transform, false);
            _crackGo.SetActive(true);

            var def = pb.Def(cell);
            var model = def != null && def.IsModel ? pb.ModelAt(cell) : null;
            var quads = model != null ? model.Quads : CubeQuads;
            var r = McAssets.DestroyStage[stage];
            var frame = new CubeSphere.CellFrame();
            frame.Set(pb.Grid, cell);
            var mid = frame.P(Vector3.one * 0.5f);
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nrm = new List<Vector3>();
            var col = new List<Color32>(); var uv2 = new List<Vector3>(); var idx = new List<int>();
            foreach (var q in quads)
            {
                var n = frame.D(q.N).normalized;
                Vector3 P(Vector3 v) { var p = frame.P(v); return p + n * 0.003f; }
                Vector2 U(Vector2 f) => new Vector2(r.x + f.x * r.width, r.y + f.y * r.height);
                int i = pos.Count;
                pos.Add(P(q.P0)); pos.Add(P(q.P1)); pos.Add(P(q.P2)); pos.Add(P(q.P3));
                uv.Add(U(q.F0)); uv.Add(U(q.F1)); uv.Add(U(q.F2)); uv.Add(U(q.F3));
                for (int k = 0; k < 4; k++) { nrm.Add(n); col.Add(new Color32(255, 255, 255, 255)); uv2.Add(new Vector3(0f, 1f, 0f)); }
                var cr = Vector3.Cross(pos[i + 1] - pos[i], pos[i + 2] - pos[i]);
                bool flip = Vector3.Dot(cr, n) < 0;
                if (!flip) { idx.Add(i); idx.Add(i + 1); idx.Add(i + 2); idx.Add(i); idx.Add(i + 2); idx.Add(i + 3); }
                else { idx.Add(i); idx.Add(i + 2); idx.Add(i + 1); idx.Add(i); idx.Add(i + 3); idx.Add(i + 2); }
            }
            _crackMesh.Clear();
            _crackMesh.SetVertices(pos); _crackMesh.SetUVs(0, uv); _crackMesh.SetNormals(nrm);
            _crackMesh.SetColors(col); _crackMesh.SetUVs(1, uv2);
            _crackMesh.SetTriangles(idx, 0, true);
        }

        private static List<BQuad> _cubeQuads;
        private static List<BQuad> CubeQuads
        {
            get
            {
                if (_cubeQuads == null)
                {
                    var d = new BlockDef { TopUv = new Rect(0, 0, 1, 1), BottomUv = new Rect(0, 0, 1, 1), SideUv = new Rect(0, 0, 1, 1) };
                    _cubeQuads = McModels.Cube(d, new Dictionary<string, ModelDisplay>()).Quads;
                }
                return _cubeQuads;
            }
        }
    }
}
