using System;
using UnityEngine;

namespace OuterCraft.Player
{
    /// ModelPart.Cube exactly as Minecraft builds it: eight corners (grown by the deformation,
    /// x swapped when mirrored) and six polygons with the standard box unwrap, each polygon's
    /// vertices taking (u2,v1), (u1,v1), (u1,v2), (u2,v2) in order.
    public static class McBox
    {
        /// emit(corners in model pixels, uvs in texture pixels, model-space normal)
        public static void Build(float x0, float y0, float z0, float dx, float dy, float dz, float u, float v,
            float inflate, bool mirror, Action<Vector3[], Vector2[], Vector3> emit)
        {
            float minX = x0 - inflate, minY = y0 - inflate, minZ = z0 - inflate;
            float maxX = x0 + dx + inflate, maxY = y0 + dy + inflate, maxZ = z0 + dz + inflate;
            if (mirror) { var t = maxX; maxX = minX; minX = t; }
            var v0 = new Vector3(minX, minY, minZ); var v1 = new Vector3(maxX, minY, minZ);
            var v2 = new Vector3(maxX, maxY, minZ); var v3 = new Vector3(minX, maxY, minZ);
            var v4 = new Vector3(minX, minY, maxZ); var v5 = new Vector3(maxX, minY, maxZ);
            var v6 = new Vector3(maxX, maxY, maxZ); var v7 = new Vector3(minX, maxY, maxZ);
            float f4 = u, f5 = u + dz, f6 = u + dz + dx, f7 = u + dz + dx + dx, f8 = u + dz + dx + dz, f9 = u + dz + dx + dz + dx;
            float f10 = v, f11 = v + dz, f12 = v + dz + dy;
            float mx = mirror ? -1f : 1f;
            void Poly(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u1, float vv1, float u2, float vv2, Vector3 n) =>
                emit(new[] { a, b, c, d },
                     new[] { new Vector2(u2, vv1), new Vector2(u1, vv1), new Vector2(u1, vv2), new Vector2(u2, vv2) }, n);
            Poly(v5, v4, v0, v1, f5, f10, f6, f11, new Vector3(0, -1, 0));      // DOWN  (model -y: the top)
            Poly(v2, v3, v7, v6, f6, f11, f7, f10, new Vector3(0, 1, 0));       // UP    (the bottom)
            Poly(v0, v4, v7, v3, f4, f11, f5, f12, new Vector3(-mx, 0, 0));     // WEST
            Poly(v1, v0, v3, v2, f5, f11, f6, f12, new Vector3(0, 0, -1));      // NORTH (the front)
            Poly(v5, v1, v2, v6, f6, f11, f8, f12, new Vector3(mx, 0, 0));      // EAST
            Poly(v4, v5, v6, v7, f8, f11, f9, f12, new Vector3(0, 0, 1));       // SOUTH (the back)
        }
    }
}
