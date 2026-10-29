namespace Lek.Server.Elementals
{
    /// <summary>The movement state of one character; the client predicts its own with an identical copy.</summary>
    public sealed class Body
    {
        public double X { get; set; }          // centre
        public double Y { get; set; }          // feet
        public double VX { get; set; }
        public double VY { get; set; }
        public double W { get; set; } = 14;
        public double H { get; set; } = 30;
        public bool Grounded { get; set; }
        public char Ground { get; set; } = '.';
        public double Coyote { get; set; }
        public bool PrevJump { get; set; }
        public bool Jumping { get; set; }
        public double DashT { get; set; }      // forced horizontal movement (slides, dashes)
        public double DashVX { get; set; }
        public double FlyT { get; set; }       // bat form: no gravity, jump flies up, down flies down
        public double LockT { get; set; }      // frozen or stunned: no control
        public double Speed { get; set; } = 1; // buffs and slows
        public double LandV { get; set; }      // vertical speed at the moment of landing, this step only
    }

    /// <summary>
    /// Platformer movement, stepped at a fixed 30 Hz. <b>Mirrored line for line in wwwroot/games/elementals/physics.js</b>
    /// so a client can replay its own unacknowledged inputs; change both together.
    /// </summary>
    public static class ElementalsPhysics
    {
        public const double Dt = 1.0 / 30;
        public const int Left = 1, Right = 2, Jump = 4, Down = 8, Attack = 16;

        public const double Gravity = 1100, MaxFall = 620, Run = 135;
        public const double GroundAccel = 1500, GroundFriction = 1800, AirAccel = 1000, AirFriction = 320;
        public const double IceAccel = 360, IceFriction = 80;
        public const double JumpSpeed = 410, JumpCut = 0.45, CoyoteTime = 0.1;
        public const double MudRun = 0.55, MudJump = 0.7, FlySpeed = 1.6;
        public const double LavaRun = 0.6, LavaJump = 0.75, LavaGravity = 0.45, LavaMaxFall = 110;
        private const double T = ElementalsMap.Tile;

        public static bool IsSolid(char c) => c is '#' or '~' or 'M' or 'P' or 'w' or 'c';
        public static bool IsIcy(char c) => c is '~' or 'w';

        public static void Step(Body b, int bits, Func<int, int, char> cell)
        {
            var locked = b.LockT > 0;
            if (locked) b.LockT = Math.Max(0, b.LockT - Dt);
            var left = !locked && (bits & Left) != 0;
            var right = !locked && (bits & Right) != 0;
            var jump = !locked && (bits & Jump) != 0;
            var down = !locked && (bits & Down) != 0;
            var inLava = Touches(b, cell, 'L');

            if (b.DashT > 0)
            {
                b.DashT = Math.Max(0, b.DashT - Dt);
                b.VX = b.DashVX;
                b.VY = 0;
                b.PrevJump = jump;
                Move(b, b.VX * Dt, 0, down, cell);
                if (b.DashT <= 0) b.VX *= 0.35;
                return;
            }

            if (b.FlyT > 0)
            {
                b.FlyT = Math.Max(0, b.FlyT - Dt);
                var fx = (right ? 1 : 0) - (left ? 1 : 0);
                var fy = (down ? 1 : 0) - (jump ? 1 : 0);
                b.VX = Approach(b.VX, fx * Run * FlySpeed * b.Speed, AirAccel * 1.6 * Dt);
                b.VY = Approach(b.VY, fy * Run * FlySpeed * 0.8 * b.Speed, AirAccel * 1.6 * Dt);
                b.PrevJump = jump;
                b.Grounded = false;
                b.LandV = 0;
                Move(b, b.VX * Dt, b.VY * Dt, true, cell);
                return;
            }

            var g = b.Grounded ? b.Ground : '.';
            var run = Run * b.Speed * (g == 'M' ? MudRun : 1) * (inLava ? LavaRun : 1);
            double accel, friction;
            if (b.Grounded && IsIcy(g)) { accel = IceAccel; friction = IceFriction; }
            else if (b.Grounded) { accel = GroundAccel; friction = GroundFriction; }
            else { accel = AirAccel; friction = AirFriction; }
            var dir = (right ? 1 : 0) - (left ? 1 : 0);
            if (dir != 0)
            {
                var target = dir * run;
                var over = Math.Sign(b.VX) == dir && Math.Abs(b.VX) > run;
                b.VX = Approach(b.VX, target, (over ? friction : accel) * Dt);
            }
            else b.VX = Approach(b.VX, 0, friction * Dt);

            var pressed = jump && !b.PrevJump;
            b.PrevJump = jump;
            if (pressed && (b.Grounded || b.Coyote > 0 || inLava))
            {
                var mul = (g == 'M' ? MudJump : 1) * (inLava ? LavaJump : 1);
                b.VY = -JumpSpeed * mul;
                b.Grounded = false;
                b.Coyote = 0;
                b.Jumping = true;
            }
            if (b.Jumping && !jump && b.VY < -JumpSpeed * JumpCut) b.VY = -JumpSpeed * JumpCut;
            if (b.VY >= 0) b.Jumping = false;

            b.VY += Gravity * (inLava ? LavaGravity : 1) * Dt;
            var maxFall = inLava ? LavaMaxFall : MaxFall;
            if (b.VY > maxFall) b.VY = maxFall;

            var wasGrounded = b.Grounded;
            var vyBefore = b.VY;
            b.Grounded = false;
            b.LandV = 0;
            Move(b, b.VX * Dt, b.VY * Dt, down, cell);
            if (b.Grounded)
            {
                b.Coyote = CoyoteTime;
                if (!wasGrounded) b.LandV = vyBefore;
            }
            else b.Coyote = Math.Max(0, b.Coyote - Dt);
        }

        private static double Approach(double v, double target, double delta) =>
            v < target ? Math.Min(v + delta, target) : Math.Max(v - delta, target);

        private static void Move(Body b, double dx, double dy, bool down, Func<int, int, char> cell)
        {
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 4.0);
            if (n < 1) n = 1;
            var sx = dx / n;
            var sy = dy / n;
            for (var i = 0; i < n; i++)
            {
                if (sx != 0) MoveX(b, sx, cell);
                if (sy != 0) MoveY(b, sy, down, cell);
            }
        }

        private static void MoveX(Body b, double d, Func<int, int, char> cell)
        {
            b.X += d;
            var r0 = (int)Math.Floor((b.Y - b.H + 0.01) / T);
            var r1 = (int)Math.Floor((b.Y - 0.01) / T);
            if (d > 0)
            {
                var c = (int)Math.Floor((b.X + b.W / 2) / T);
                for (var r = r0; r <= r1; r++)
                {
                    if (!IsSolid(cell(c, r))) continue;
                    b.X = c * T - b.W / 2 - 0.01;
                    b.VX = 0;
                    if (b.DashT > 0) b.DashVX = 0;
                    return;
                }
            }
            else
            {
                var c = (int)Math.Floor((b.X - b.W / 2) / T);
                for (var r = r0; r <= r1; r++)
                {
                    if (!IsSolid(cell(c, r))) continue;
                    b.X = (c + 1) * T + b.W / 2 + 0.01;
                    b.VX = 0;
                    if (b.DashT > 0) b.DashVX = 0;
                    return;
                }
            }
        }

        private static void MoveY(Body b, double d, bool down, Func<int, int, char> cell)
        {
            var prevY = b.Y;
            b.Y += d;
            var c0 = (int)Math.Floor((b.X - b.W / 2 + 0.01) / T);
            var c1 = (int)Math.Floor((b.X + b.W / 2 - 0.01) / T);
            if (d > 0)
            {
                var r = (int)Math.Floor(b.Y / T);
                for (var c = c0; c <= c1; c++)
                {
                    var ch = cell(c, r);
                    if (IsSolid(ch) || (ch == '=' && !down && prevY <= r * T + 0.01))
                    {
                        b.Y = r * T;
                        b.VY = 0;
                        b.Grounded = true;
                        b.Ground = ch;
                        return;
                    }
                }
            }
            else
            {
                var r = (int)Math.Floor((b.Y - b.H) / T);
                for (var c = c0; c <= c1; c++)
                {
                    if (!IsSolid(cell(c, r))) continue;
                    b.Y = (r + 1) * T + b.H;
                    b.VY = 0;
                    b.Jumping = false;
                    return;
                }
            }
        }

        /// <summary>Whether the body (shrunk by a pixel) overlaps any cell of the given kind.</summary>
        public static bool Touches(Body b, Func<int, int, char> cell, char kind)
        {
            var c0 = (int)Math.Floor((b.X - b.W / 2 + 1) / T);
            var c1 = (int)Math.Floor((b.X + b.W / 2 - 1) / T);
            var r0 = (int)Math.Floor((b.Y - b.H + 1) / T);
            var r1 = (int)Math.Floor((b.Y - 1) / T);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    if (cell(c, r) == kind) return true;
            return false;
        }
    }
}
