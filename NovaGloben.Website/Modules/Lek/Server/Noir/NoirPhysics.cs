namespace Lek.Server.Noir
{
    public sealed class NoirBody
    {
        public double X { get; set; }
        public double Z { get; set; }
        public double DiveT { get; set; } = -1;   // seconds since the dive started, -1 when on your feet
        public double DiveX { get; set; }
        public double DiveZ { get; set; } = 1;
        public double DiveCd { get; set; }
        public double Speed { get; set; } = 1;    // set by the room: inside someone else's bullet time you move slower
        public bool PrevDive { get; set; }
    }

    /// <summary>
    /// Movement on the ground plane at a fixed 30 Hz, in metres: run relative to the camera's yaw, and the shootdodge
    /// (a dive through the air, a moment on the floor, getting up). Square-against-box collisions.
    /// <b>Mirrored line for line in wwwroot/games/noir/physics.js</b>; change both together.
    /// </summary>
    public static class NoirPhysics
    {
        public const double Dt = 1.0 / 30;
        public const int Fwd = 1, Back = 2, Left = 4, Right = 8, Dive = 16, Fire = 32;
        public const double Run = 5.2, Half = 0.35;
        public const double DiveSpeed = 7.8, DiveAir = 0.55, DiveLie = 0.5, DiveRise = 0.35, DiveCooldown = 0.5;
        public const double DiveEnd = DiveAir + DiveLie + DiveRise;

        /// <summary>yaw: 0 looks down +Z, positive turns toward +X. Forward is (sin yaw, cos yaw), right is (-cos yaw, sin yaw).</summary>
        public static void Step(NoirBody b, int bits, double yaw, double[][] boxes)
        {
            b.DiveCd = Math.Max(0, b.DiveCd - Dt);
            double f = ((bits & Fwd) != 0 ? 1 : 0) - ((bits & Back) != 0 ? 1 : 0);
            double s = ((bits & Right) != 0 ? 1 : 0) - ((bits & Left) != 0 ? 1 : 0);
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw);
            var mx = f * sy - s * cy;
            var mz = f * cy + s * sy;
            var len = Math.Sqrt(mx * mx + mz * mz);
            if (len > 0) { mx /= len; mz /= len; }
            var dive = (bits & Dive) != 0;
            if (dive && !b.PrevDive && b.DiveT < 0 && b.DiveCd <= 0)
            {
                b.DiveT = 0;
                if (len > 0) { b.DiveX = mx; b.DiveZ = mz; }
                else { b.DiveX = sy; b.DiveZ = cy; }
            }
            b.PrevDive = dive;
            double vx, vz;
            if (b.DiveT >= 0)
            {
                var air = b.DiveT < DiveAir;
                vx = air ? b.DiveX * DiveSpeed * b.Speed : 0;
                vz = air ? b.DiveZ * DiveSpeed * b.Speed : 0;
                b.DiveT += Dt * b.Speed;
                if (b.DiveT >= DiveEnd) { b.DiveT = -1; b.DiveCd = DiveCooldown; }
            }
            else
            {
                vx = mx * Run * b.Speed;
                vz = mz * Run * b.Speed;
            }
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(vx), Math.Abs(vz)) * Dt / 0.1);
            if (n < 1) n = 1;
            for (var i = 0; i < n; i++)
            {
                MoveX(b, vx * Dt / n, boxes);
                MoveZ(b, vz * Dt / n, boxes);
            }
        }

        /// <summary>Hit height of a body: standing, flying in a dive, on the floor, getting up.</summary>
        public static double Height(NoirBody b)
        {
            if (b.DiveT < 0) return 1.8;
            if (b.DiveT < DiveAir) return 1.05;
            if (b.DiveT < DiveAir + DiveLie) return 0.55;
            return 1.2;
        }

        // boxes: [x0, z0, x1, z1, height]
        private static void MoveX(NoirBody b, double d, double[][] boxes)
        {
            if (d == 0) return;
            b.X += d;
            foreach (var box in boxes)
            {
                if (b.Z + Half <= box[1] || b.Z - Half >= box[3] || b.X + Half <= box[0] || b.X - Half >= box[2]) continue;
                b.X = d > 0 ? box[0] - Half - 0.001 : box[2] + Half + 0.001;
            }
        }

        private static void MoveZ(NoirBody b, double d, double[][] boxes)
        {
            if (d == 0) return;
            b.Z += d;
            foreach (var box in boxes)
            {
                if (b.X + Half <= box[0] || b.X - Half >= box[2] || b.Z + Half <= box[1] || b.Z - Half >= box[3]) continue;
                b.Z = d > 0 ? box[1] - Half - 0.001 : box[3] + Half + 0.001;
            }
        }
    }
}
