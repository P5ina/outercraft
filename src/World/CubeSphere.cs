using System;
using UnityEngine;

namespace OuterCraft.World
{
    /// A block address on a planet: cube face, two angular indices on that face, and a radial layer.
    public struct Cell : IEquatable<Cell>
    {
        public byte Face;
        public int U, V, H;

        public Cell(int face, int u, int v, int h) { Face = (byte)face; U = u; V = v; H = h; }

        public bool Equals(Cell o) => Face == o.Face && U == o.U && V == o.V && H == o.H;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() { unchecked { return ((Face * 397 ^ U) * 397 ^ V) * 397 ^ H; } }
        public override string ToString() => $"f{Face} u{U} v{V} h{H}";
    }

    /// Equiangular cube-sphere grid around a planet's centre, in the planet's local space.
    ///
    /// Each of the 6 cube faces is split into 2N x 2N columns by equal angles, so blocks stay close
    /// to square everywhere on the face. Layers are 1 m thick shells: H = floor(distance from centre).
    /// N is picked so a block is 1 m wide at the planet's surface; higher up blocks get a bit wider.
    public sealed class CubeSphere
    {
        public readonly int N;
        public readonly double Delta; // angle per column

        private static readonly Vector3[] Normal =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };
        private static readonly Vector3[] Right =
        {
            new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(1, 0, 0),
            new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
        };
        private static readonly Vector3[] Up =
        {
            new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1),
            new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 1, 0),
        };

        /// Layers are 1 m shells starting at this fraction of a metre, so the first build on a planet
        /// sits exactly on the ground it was placed on.
        public readonly float Offset;

        public CubeSphere(int n, float offset = 0f)
        {
            N = Math.Max(4, n);
            Offset = offset - (float)Math.Floor(offset);
            Delta = Math.PI / 2.0 / (2 * N);
        }

        /// Columns so that a block is ~1 m wide at this radius.
        public static int ColumnsFor(float surfaceRadius) => Math.Max(4, (int)Math.Round(Math.PI * surfaceRadius / 4.0));

        /// Unit direction through angular grid position (a, b) on a face (a, b in column units).
        public Vector3 Dir(int face, double a, double b)
        {
            double ta = Math.Tan(a * Delta), tb = Math.Tan(b * Delta);
            var n = Normal[face];
            var r = Right[face];
            var u = Up[face];
            return new Vector3(
                (float)(n.x + ta * r.x + tb * u.x),
                (float)(n.y + ta * r.y + tb * u.y),
                (float)(n.z + ta * r.z + tb * u.z)).normalized;
        }

        /// Corner (du, dv, dh in {0, 1}) of a cell, planet-local.
        public Vector3 Corner(Cell c, int du, int dv, int dh) => Dir(c.Face, c.U + du, c.V + dv) * (c.H + dh + Offset);

        public Vector3 Center(Cell c) => Dir(c.Face, c.U + 0.5, c.V + 0.5) * (c.H + 0.5f + Offset);

        /// A point inside a cell given Minecraft-style block coordinates (x along u, y up, z along v),
        /// each 0..1, by trilinear blend of the cell's corners.
        public Vector3 Point(Cell c, Vector3 mc)
        {
            float x = mc.x, y = mc.y, z = mc.z;
            var b00 = Vector3.LerpUnclamped(Corner(c, 0, 0, 0), Corner(c, 1, 0, 0), x);
            var b10 = Vector3.LerpUnclamped(Corner(c, 0, 1, 0), Corner(c, 1, 1, 0), x);
            var t00 = Vector3.LerpUnclamped(Corner(c, 0, 0, 1), Corner(c, 1, 0, 1), x);
            var t10 = Vector3.LerpUnclamped(Corner(c, 0, 1, 1), Corner(c, 1, 1, 1), x);
            var bot = Vector3.LerpUnclamped(b00, b10, z);
            var top = Vector3.LerpUnclamped(t00, t10, z);
            return Vector3.LerpUnclamped(bot, top, y);
        }

        /// The same, with the corners fetched once (meshing many points of one cell).
        public sealed class CellFrame
        {
            public readonly Vector3[] C = new Vector3[8];
            public Vector3 EU, EV, EH; // unit axes: Minecraft x (east), z (south), y (up)

            public void Set(CubeSphere g, Cell c)
            {
                for (int i = 0; i < 8; i++) C[i] = g.Corner(c, i & 1, (i >> 1) & 1, (i >> 2) & 1);
                EU = (C[1] + C[3] + C[5] + C[7] - C[0] - C[2] - C[4] - C[6]).normalized;
                EV = (C[2] + C[3] + C[6] + C[7] - C[0] - C[1] - C[4] - C[5]).normalized;
                EH = (C[4] + C[5] + C[6] + C[7] - C[0] - C[1] - C[2] - C[3]).normalized;
            }

            public Vector3 P(Vector3 mc)
            {
                var b00 = Vector3.LerpUnclamped(C[0], C[1], mc.x);
                var b10 = Vector3.LerpUnclamped(C[2], C[3], mc.x);
                var t00 = Vector3.LerpUnclamped(C[4], C[5], mc.x);
                var t10 = Vector3.LerpUnclamped(C[6], C[7], mc.x);
                return Vector3.LerpUnclamped(Vector3.LerpUnclamped(b00, b10, mc.z), Vector3.LerpUnclamped(t00, t10, mc.z), mc.y);
            }

            /// A Minecraft-space direction in planet space.
            public Vector3 D(Vector3 mc) => EU * mc.x + EH * mc.y + EV * mc.z;
        }

        /// Where a planet-local point sits relative to a cell, in Minecraft block coordinates
        /// (0..1 inside it; outside values for points beyond it, measured in the cell's own face).
        public Vector3 Frac(Cell c, Vector3 p)
        {
            float r = p.magnitude;
            var n = Normal[c.Face];
            var d = p / Mathf.Max(r, 1e-4f);
            float dn = Vector3.Dot(d, n);
            if (dn < 1e-3f) dn = 1e-3f;
            double a = Math.Atan(Vector3.Dot(d, Right[c.Face]) / dn) / Delta - c.U;
            double b = Math.Atan(Vector3.Dot(d, Up[c.Face]) / dn) / Delta - c.V;
            return new Vector3((float)a, r - c.H - Offset, (float)b);
        }

        /// One step in a Minecraft direction (Down, Up, North, South, West, East).
        public Cell Step(Cell c, Assets.Dir d)
        {
            switch (d)
            {
                case Assets.Dir.Down: return Step(c, 0, 0, -1);
                case Assets.Dir.Up: return Step(c, 0, 0, 1);
                case Assets.Dir.North: return Step(c, 0, -1, 0);
                case Assets.Dir.South: return Step(c, 0, 1, 0);
                case Assets.Dir.West: return Step(c, -1, 0, 0);
                default: return Step(c, 1, 0, 0);
            }
        }

        /// "Up" at a cell (radially out).
        public Vector3 UpAt(Cell c) => Dir(c.Face, c.U + 0.5, c.V + 0.5);

        /// Which cell a planet-local point is in.
        public Cell Locate(Vector3 p)
        {
            float r = p.magnitude;
            if (r < 1e-4f) return new Cell(0, 0, 0, 0);
            var d = p / r;
            int face;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            if (ax >= ay && ax >= az) face = d.x >= 0 ? 0 : 1;
            else if (ay >= az) face = d.y >= 0 ? 2 : 3;
            else face = d.z >= 0 ? 4 : 5;
            float dn = Vector3.Dot(d, Normal[face]);
            double x = Vector3.Dot(d, Right[face]) / dn;
            double y = Vector3.Dot(d, Up[face]) / dn;
            int u = (int)Math.Floor(Math.Atan(x) / Delta);
            int v = (int)Math.Floor(Math.Atan(y) / Delta);
            u = Math.Max(-N, Math.Min(N - 1, u));
            v = Math.Max(-N, Math.Min(N - 1, v));
            return new Cell(face, u, v, (int)Math.Floor(r - Offset));
        }

        /// The neighbouring cell one step along u, v or h. Across a cube edge the step lands on the
        /// next face: we go through the neighbour's centre point, which the next face claims.
        public Cell Step(Cell c, int du, int dv, int dh)
        {
            int u = c.U + du, v = c.V + dv;
            if (u >= -N && u < N && v >= -N && v < N)
                return new Cell(c.Face, u, v, c.H + dh);
            return Locate(Dir(c.Face, u + 0.5, v + 0.5) * (c.H + dh + 0.5f + Offset));
        }
    }
}
