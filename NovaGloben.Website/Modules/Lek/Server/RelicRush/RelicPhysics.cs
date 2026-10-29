namespace Lek.Server.RelicRush
{
    public sealed class RelicBody
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double DashT { get; set; }
        public double DashX { get; set; }
        public double DashY { get; set; }
        public double DashCd { get; set; }
        public double FaceX { get; set; } = 1;
        public double FaceY { get; set; }
        public double Speed { get; set; } = 1;   // set by the room: the carrier is a little slower
        public bool PrevDash { get; set; }
    }

    /// <summary>
    /// Top-down movement at a fixed 30 Hz: eight directions, a short dash, square-against-tile collisions.
    /// <b>Mirrored line for line in wwwroot/games/relicrush/physics.js</b>; change both together.
    /// </summary>
    public static class RelicPhysics
    {
        public const double Dt = 1.0 / 30;
        public const int Up = 1, Down = 2, Left = 4, Right = 8, Dash = 16;
        public const double Run = 92, DashSpeed = 290, DashTime = 0.17, DashCooldown = 3.0, Half = 5;
        private const double T = RelicMaze.Tile;

        public static void Step(RelicBody b, int bits, Func<int, int, bool> wall)
        {
            b.DashCd = Math.Max(0, b.DashCd - Dt);
            double dx = ((bits & Right) != 0 ? 1 : 0) - ((bits & Left) != 0 ? 1 : 0);
            double dy = ((bits & Down) != 0 ? 1 : 0) - ((bits & Up) != 0 ? 1 : 0);
            if (dx != 0 && dy != 0) { dx *= 0.7071; dy *= 0.7071; }
            if (dx != 0 || dy != 0) { b.FaceX = dx; b.FaceY = dy; }
            var dash = (bits & Dash) != 0;
            if (dash && !b.PrevDash && b.DashCd <= 0 && b.DashT <= 0)
            {
                b.DashT = DashTime;
                b.DashCd = DashCooldown;
                var len = Math.Sqrt(b.FaceX * b.FaceX + b.FaceY * b.FaceY);
                b.DashX = len > 0 ? b.FaceX / len : 1;
                b.DashY = len > 0 ? b.FaceY / len : 0;
            }
            b.PrevDash = dash;
            double vx, vy;
            if (b.DashT > 0)
            {
                b.DashT = Math.Max(0, b.DashT - Dt);
                vx = b.DashX * DashSpeed;
                vy = b.DashY * DashSpeed;
            }
            else
            {
                vx = dx * Run * b.Speed;
                vy = dy * Run * b.Speed;
            }
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(vx), Math.Abs(vy)) * Dt / 4);
            if (n < 1) n = 1;
            for (var i = 0; i < n; i++)
            {
                MoveX(b, vx * Dt / n, wall);
                MoveY(b, vy * Dt / n, wall);
            }
        }

        private static void MoveX(RelicBody b, double d, Func<int, int, bool> wall)
        {
            if (d == 0) return;
            b.X += d;
            var r0 = (int)Math.Floor((b.Y - Half + 0.01) / T);
            var r1 = (int)Math.Floor((b.Y + Half - 0.01) / T);
            var c = (int)Math.Floor((b.X + (d > 0 ? Half : -Half)) / T);
            for (var r = r0; r <= r1; r++)
            {
                if (!wall(c, r)) continue;
                b.X = d > 0 ? c * T - Half - 0.01 : (c + 1) * T + Half + 0.01;
                return;
            }
        }

        private static void MoveY(RelicBody b, double d, Func<int, int, bool> wall)
        {
            if (d == 0) return;
            b.Y += d;
            var c0 = (int)Math.Floor((b.X - Half + 0.01) / T);
            var c1 = (int)Math.Floor((b.X + Half - 0.01) / T);
            var r = (int)Math.Floor((b.Y + (d > 0 ? Half : -Half)) / T);
            for (var c = c0; c <= c1; c++)
            {
                if (!wall(c, r)) continue;
                b.Y = d > 0 ? r * T - Half - 0.01 : (r + 1) * T + Half + 0.01;
                return;
            }
        }
    }
}
