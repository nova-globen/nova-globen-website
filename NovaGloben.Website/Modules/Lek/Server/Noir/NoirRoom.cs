using System.Security.Cryptography;

namespace Lek.Server.Noir
{
    public enum NrPhase { Lobby, Countdown, Playing, Over }

    public sealed record NoirGun(string Name, int Damage, int Pellets, double Cooldown, int Mag, double Reload, double Spread, double Speed);

    public sealed class NrPlayer
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public string? ConnectionId { get; init; }
        public bool IsBot { get; init; }
        public bool AutoBot { get; init; }
        public int Look { get; init; }
        public NoirBody Body { get; } = new();
        public bool InMatch { get; set; }
        public int Bits { get; set; }
        public double Yaw { get; set; }        // movement yaw (the camera's)
        public double AimYaw { get; set; }
        public double AimPitch { get; set; }
        public int LastSeq { get; set; }
        public Queue<(int Seq, int Bits, double Yaw, double AimYaw, double AimPitch)> Inputs { get; } = new();
        public int Hp { get; set; } = NoirRoom.MaxHp;
        public bool Alive { get; set; }
        public double RespawnAt { get; set; }
        public double ProtectUntil { get; set; }
        public int Slot { get; set; }
        public int Second { get; set; } = -1;  // the picked-up weapon in slot 1, or -1
        public int[] Ammo { get; } = new int[2];
        public double ReloadT { get; set; }
        public double FireCd { get; set; }
        public double Meter { get; set; } = 1;  // bullet time, 1 = full
        public bool BtOn { get; set; }
        public bool WantReload { get; set; }
        public int WantSlot { get; set; } = -1;
        public bool WantBt { get; set; }
        public double LastHurtAt { get; set; } = -10;
        public int Kills { get; set; }
        public int Deaths { get; set; }
        // bots
        public double NextThink { get; set; }
        public string? Target { get; set; }
        public double SeenSince { get; set; } = -1;
        public double StrafeSign { get; set; } = 1;
        public double StrafeUntil { get; set; }
        public int BotDiveTicks { get; set; }
        public double AimErrYaw { get; set; }
        public double AimErrPitch { get; set; }

        public int Gun => Slot == 1 && Second >= 0 ? Second : 0;
    }

    /// <summary>
    /// One Bullet Noir room: a free-for-all on The Docks. Bullets are real projectiles; bullet time opens a bubble
    /// around you where bullets crawl and everyone else moves slower; the shootdodge drops your hitbox and opens a small
    /// bubble while you fly. First to <see cref="KillsToWin"/> or the most kills after <see cref="MatchSeconds"/>.
    /// Runs at a fixed 30 Hz from <see cref="NoirLoop"/>.
    /// </summary>
    public sealed class NoirRoom
    {
        public const int MaxPlayers = 8, MaxHp = 100, KillsToWin = 15;
        public const double MatchSeconds = 300, RespawnSeconds = 3, ProtectSeconds = 1.5;
        public const double BubbleRadius = 5, DiveBubbleRadius = 3.2, BtSeconds = 5, MeterRegen = 1.0 / 35, KillBonus = 0.35;
        public const double BotReaction = 0.7, BotFireSlowdown = 1.8, BotWobble = 0.06;
        public const double SlowPlayers = 0.55, SlowBullets = 0.2, BulletRange = 70, BarrelRadius = 4.2;
        private const double Dt = NoirPhysics.Dt;
        private const double Cell = 0.5;
        private static readonly int Cols = (int)(NoirMap.Width / Cell), Rows = (int)(NoirMap.Depth / Cell);

        public static readonly NoirGun[] Guns =
        {
            new("Pistol", 24, 1, 0.26, 12, 1.1, 0.006, 90),
            new("Shotgun", 11, 8, 0.85, 6, 1.9, 0.055, 75),
            new("SMG", 12, 1, 0.085, 30, 1.6, 0.032, 90),
            new("Rifle", 20, 1, 0.12, 25, 1.8, 0.014, 100),
        };

        private static readonly string[] BotNames = { "Sal", "Mickey", "Rosa", "Dutch", "Lou", "Vera", "Benny", "Nico", "Ivy", "Tommy" };

        private sealed class Bullet
        {
            public int Id;
            public string Owner = "";
            public int Gun;
            public double X, Y, Z, Dx, Dy, Dz, Speed, Travelled;
        }

        public object Sync { get; } = new();
        public string Code { get; }
        public string? OwnerId { get; set; }
        public int Size { get; private set; } = 4;
        public Dictionary<string, NrPlayer> Players { get; } = new();
        public NrPhase Phase { get; private set; } = NrPhase.Lobby;
        public double Now { get; private set; }
        public double PhaseUntil { get; private set; }
        public double MatchEnds { get; private set; }
        public string? Winner { get; private set; }
        public List<object[]> Events { get; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public long Tick { get; private set; }
        public long LastSentTick { get; set; } = -100;

        private readonly Random _random = new(RandomNumberGenerator.GetInt32(int.MaxValue));
        private readonly List<Bullet> _bullets = new();
        private readonly double[] _pickupAt = new double[NoirMap.Pickups.Length];   // available again at this time
        private readonly double[] _barrelAt = new double[NoirMap.Barrels.Length];
        private readonly double[][] _barrelBoxes = NoirMap.Barrels.Select(NoirMap.BarrelBox).ToArray();
        private readonly bool[] _blocked = BuildGrid();
        private readonly Dictionary<int, int[]> _fields = new();
        private readonly List<(double X, double Z, double R, string Owner)> _bubbles = new();
        private int _botCounter, _bulletId;

        public NoirRoom(string code)
        {
            Code = code;
        }

        public int HumanCount => Players.Values.Count(p => !p.IsBot);

        // ------------------------------------------------------------------ lobby
        public NrPlayer AddPlayer(string id, string name, string? connectionId, bool isBot, bool autoBot = false)
        {
            var p = new NrPlayer { Id = id, Name = name, ConnectionId = connectionId, IsBot = isBot, AutoBot = autoBot, Look = _random.Next(3) };
            Players[id] = p;
            if (OwnerId == null && !isBot) OwnerId = id;
            if (Phase is NrPhase.Countdown or NrPhase.Playing)
            {
                p.InMatch = true;
                Spawn(p);
            }
            LastActivity = DateTime.UtcNow;
            return p;
        }

        public void RemovePlayer(string id)
        {
            if (!Players.Remove(id)) return;
            if (OwnerId == id) OwnerId = Players.Values.FirstOrDefault(p => !p.IsBot)?.Id;
            if (Phase != NrPhase.Lobby && HumanCount == 0) BackToLobby();
            LastActivity = DateTime.UtcNow;
        }

        public string? SetSize(int size)
        {
            if (Phase != NrPhase.Lobby) return "The match is running.";
            Size = Math.Clamp(size, 2, MaxPlayers);
            return null;
        }

        public string? Start()
        {
            if (Phase != NrPhase.Lobby) return "A match is already running.";
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            while (Players.Count < Size)
            {
                _botCounter++;
                var used = Players.Values.Select(p => p.Name).ToHashSet();
                var name = BotNames.FirstOrDefault(n => !used.Contains(n + " (bot)")) ?? "Bot " + _botCounter;
                AddPlayer("bot-" + _botCounter, name + " (bot)", null, true, true);
            }
            Winner = null;
            _bullets.Clear();
            Array.Clear(_pickupAt);
            Array.Clear(_barrelAt);
            foreach (var p in Players.Values) { p.InMatch = true; p.Kills = p.Deaths = 0; p.Meter = 1; Spawn(p); }
            Phase = NrPhase.Countdown;
            PhaseUntil = Now + 3;
            MatchEnds = 0;
            Events.Add(new object[] { "round" });
            return null;
        }

        private void EndMatch()
        {
            var best = Players.Values.Where(p => p.InMatch).OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths).FirstOrDefault();
            Winner = best?.Id;
            Phase = NrPhase.Over;
            PhaseUntil = Now + 8;
            foreach (var p in Players.Values) p.BtOn = false;
            Events.Add(new object[] { "over", Winner ?? "" });
        }

        private void BackToLobby()
        {
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            foreach (var p in Players.Values) { p.InMatch = false; p.Alive = false; }
            _bullets.Clear();
            Phase = NrPhase.Lobby;
        }

        private void Spawn(NrPlayer p)
        {
            var enemies = Players.Values.Where(e => e != p && e.Alive).ToList();
            var ranked = NoirMap.Spawns
                .Select(s => (s, d: enemies.Count == 0 ? _random.NextDouble() * 100 : enemies.Min(e => Dist(e.Body.X, e.Body.Z, s.X, s.Z))))
                .OrderByDescending(x => x.d).Take(3).ToList();
            var spot = ranked[_random.Next(ranked.Count)].s;
            var b = p.Body;
            b.X = spot.X; b.Z = spot.Z;
            b.DiveT = -1; b.DiveCd = 0; b.Speed = 1; b.PrevDive = false;
            p.Yaw = p.AimYaw = Math.Atan2(NoirMap.Width / 2 - spot.X, NoirMap.Depth / 2 - spot.Z);
            p.AimPitch = 0;
            p.Hp = MaxHp;
            p.Alive = true;
            p.ProtectUntil = Now + ProtectSeconds + (Phase == NrPhase.Countdown ? 3 : 0);
            p.Slot = 0; p.Second = -1;
            p.Ammo[0] = Guns[0].Mag; p.Ammo[1] = 0;
            p.ReloadT = 0; p.FireCd = 0; p.BtOn = false;
            p.Target = null; p.SeenSince = -1;
            Events.Add(new object[] { "spawn", p.Id });
        }

        // ------------------------------------------------------------------ input
        public void QueueInput(NrPlayer p, int seq, int bits, double yaw, double aimYaw, double aimPitch)
        {
            p.Inputs.Enqueue((seq, bits, yaw, aimYaw, aimPitch));
            while (p.Inputs.Count > 20) p.Inputs.Dequeue();
            LastActivity = DateTime.UtcNow;
        }

        // ------------------------------------------------------------------ tick
        public void Step()
        {
            Now += Dt;
            Tick++;
            switch (Phase)
            {
                case NrPhase.Lobby:
                    foreach (var p in Players.Values) p.Inputs.Clear();
                    return;
                case NrPhase.Countdown when Now >= PhaseUntil:
                    Phase = NrPhase.Playing;
                    MatchEnds = Now + MatchSeconds;
                    Events.Add(new object[] { "go" });
                    break;
                case NrPhase.Over when Now >= PhaseUntil:
                    BackToLobby();
                    return;
            }
            var playing = Phase == NrPhase.Playing;
            UpdateBubbles();
            foreach (var p in Players.Values.ToList())
            {
                if (!p.InMatch) { p.Inputs.Clear(); continue; }
                if (!p.Alive)
                {
                    p.Inputs.Clear();
                    if (playing && Now >= p.RespawnAt) Spawn(p);
                    continue;
                }
                if (p.IsBot && playing) BotThink(p);
                p.Body.Speed = SpeedOf(p);
                var mask = playing ? ~0 : 0;
                var steps = !p.IsBot && p.Inputs.Count > 3 ? 2 : 1;
                for (var s = 0; s < steps; s++)
                {
                    if (!p.IsBot && p.Inputs.Count > 0)
                    {
                        var input = p.Inputs.Dequeue();
                        p.LastSeq = input.Seq;
                        p.Bits = input.Bits;
                        p.Yaw = input.Yaw;
                        p.AimYaw = input.AimYaw;
                        p.AimPitch = Math.Clamp(input.AimPitch, -1.4, 1.4);
                    }
                    NoirPhysics.Step(p.Body, p.Bits & mask, p.Yaw, NoirMap.Solids);
                }
                if (playing) Combat(p);
            }
            if (!playing) return;
            StepBullets();
            Pickups();
            if (Phase == NrPhase.Playing && Now >= MatchEnds) EndMatch();
        }

        private void UpdateBubbles()
        {
            _bubbles.Clear();
            foreach (var p in Players.Values)
            {
                if (!p.Alive || !p.InMatch) continue;
                if (p.BtOn) _bubbles.Add((p.Body.X, p.Body.Z, BubbleRadius, p.Id));
                else if (p.Body.DiveT >= 0 && p.Body.DiveT < NoirPhysics.DiveAir) _bubbles.Add((p.Body.X, p.Body.Z, DiveBubbleRadius, p.Id));
            }
        }

        /// <summary>Inside someone else's bubble you move and shoot slower; inside your own you keep full speed.</summary>
        private double SpeedOf(NrPlayer p)
        {
            var own = false;
            var inOther = false;
            foreach (var b in _bubbles)
            {
                if (Dist(b.X, b.Z, p.Body.X, p.Body.Z) > b.R) continue;
                if (b.Owner == p.Id) own = true; else inOther = true;
            }
            return inOther && !own ? SlowPlayers : 1;
        }

        private bool InBubble(double x, double z)
        {
            foreach (var b in _bubbles) if (Dist(b.X, b.Z, x, z) <= b.R) return true;
            return false;
        }

        private void Combat(NrPlayer p)
        {
            var k = p.Body.Speed;
            // bullet time: the meter drains while it is on and refills slowly when it is off
            if (p.WantBt) { p.WantBt = false; if (p.BtOn) p.BtOn = false; else if (p.Meter > 0.15) p.BtOn = true; }
            if (p.BtOn) { p.Meter -= Dt / BtSeconds; if (p.Meter <= 0) { p.Meter = 0; p.BtOn = false; } }
            else p.Meter = Math.Min(1, p.Meter + Dt * MeterRegen);

            if (p.WantSlot >= 0)
            {
                var slot = p.WantSlot == 1 && p.Second < 0 ? 0 : p.WantSlot;
                p.WantSlot = -1;
                if (slot != p.Slot) { p.Slot = slot; p.ReloadT = 0; p.FireCd = Math.Max(p.FireCd, 0.25); }
            }
            var gun = Guns[p.Gun];
            p.FireCd = Math.Max(0, p.FireCd - Dt * k);
            if (p.ReloadT > 0)
            {
                p.ReloadT -= Dt * k;
                if (p.ReloadT <= 0) { p.ReloadT = 0; p.Ammo[p.Slot] = gun.Mag; Events.Add(new object[] { "reloaded", p.Id }); }
            }
            if (p.WantReload)
            {
                p.WantReload = false;
                if (p.ReloadT <= 0 && p.Ammo[p.Slot] < gun.Mag) { p.ReloadT = gun.Reload; Events.Add(new object[] { "reload", p.Id }); }
            }
            if ((p.Bits & NoirPhysics.Fire) == 0 || p.FireCd > 0 || p.ReloadT > 0) return;
            if (p.Ammo[p.Slot] <= 0) { p.ReloadT = gun.Reload; Events.Add(new object[] { "reload", p.Id }); return; }
            p.Ammo[p.Slot]--;
            // bots shoot slower than people can, so walking into view is not instant death
            p.FireCd = gun.Cooldown * (p.IsBot ? BotFireSlowdown : 1);
            p.ProtectUntil = Math.Min(p.ProtectUntil, Now);
            var (ox, oy, oz) = Muzzle(p);
            var cp = Math.Cos(p.AimPitch);
            var dx = Math.Sin(p.AimYaw) * cp;
            var dy = Math.Sin(p.AimPitch);
            var dz = Math.Cos(p.AimYaw) * cp;
            var spread = gun.Spread * (p.Body.DiveT >= 0 ? 1.4 : 1) * ((p.Bits & 15) != 0 ? 1.3 : 1);
            for (var i = 0; i < gun.Pellets; i++)
            {
                double ex = dx + Gauss() * spread, ey = dy + Gauss() * spread, ez = dz + Gauss() * spread;
                var len = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                var bullet = new Bullet { Id = ++_bulletId, Owner = p.Id, Gun = p.Gun, X = ox, Y = oy, Z = oz, Dx = ex / len, Dy = ey / len, Dz = ez / len, Speed = gun.Speed };
                _bullets.Add(bullet);
                Events.Add(new object[] { "shot", bullet.Id, p.Id, p.Gun, R(ox), R(oy), R(oz), R3(bullet.Dx), R3(bullet.Dy), R3(bullet.Dz), gun.Speed, R(Now) });
            }
            if (p.Ammo[p.Slot] == 0) { p.ReloadT = gun.Reload; Events.Add(new object[] { "reload", p.Id }); }
        }

        /// <summary>Where shots leave: right hand, at a height that follows the dive.</summary>
        public static (double X, double Y, double Z) Muzzle(NrPlayer p)
        {
            var h = NoirPhysics.Height(p.Body);
            var y = p.Body.DiveT < 0 ? 1.42 : h * 0.72;
            double rx = -Math.Cos(p.AimYaw), rz = Math.Sin(p.AimYaw);
            return (p.Body.X + rx * 0.2 + Math.Sin(p.AimYaw) * 0.3, y, p.Body.Z + rz * 0.2 + Math.Cos(p.AimYaw) * 0.3);
        }

        private void StepBullets()
        {
            for (var i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                var travel = b.Speed * Dt * (InBubble(b.X, b.Z) ? SlowBullets : 1);
                var n = (int)Math.Ceiling(travel / 0.2);
                var ds = travel / n;
                var done = false;
                for (var s = 0; s < n && !done; s++)
                {
                    b.X += b.Dx * ds; b.Y += b.Dy * ds; b.Z += b.Dz * ds;
                    b.Travelled += ds;
                    if (b.Y <= 0) { End(b, "floor"); done = true; break; }
                    foreach (var p in Players.Values)
                    {
                        if (!p.Alive || p.Id == b.Owner) continue;
                        var part = HitPart(p, b.X, b.Y, b.Z);
                        if (part == 0) continue;
                        End(b, "flesh");
                        Damage(p, Guns[b.Gun].Damage * (part == 2 ? 2 : 1), b.Owner, b.Gun, part == 2, b.Dx, b.Dz);
                        done = true;
                        break;
                    }
                    if (done) break;
                    for (var k = 0; k < _barrelBoxes.Length; k++)
                    {
                        if (_barrelAt[k] > Now || !Inside(_barrelBoxes[k], b.X, b.Y, b.Z)) continue;
                        End(b, "barrel");
                        Explode(k, b.Owner);
                        done = true;
                        break;
                    }
                    if (done) break;
                    foreach (var box in NoirMap.Solids)
                    {
                        if (!Inside(box, b.X, b.Y, b.Z)) continue;
                        End(b, "wall");
                        done = true;
                        break;
                    }
                }
                if (!done && b.Travelled >= BulletRange) { End(b, "none"); done = true; }
                if (done) _bullets.RemoveAt(i);
            }
        }

        private void End(Bullet b, string what) => Events.Add(new object[] { "end", b.Id, R(b.X), R(b.Y), R(b.Z), what, R(Now) });

        private static int HitPart(NrPlayer p, double x, double y, double z)
        {
            var h = NoirPhysics.Height(p.Body);
            if (y < 0 || y > h) return 0;
            var r = p.Body.DiveT >= 0 ? 0.5 : 0.4;
            if (Dist(x, z, p.Body.X, p.Body.Z) > r) return 0;
            return p.Body.DiveT < 0 && y > h - 0.32 ? 2 : 1;
        }

        private static bool Inside(double[] box, double x, double y, double z) =>
            x >= box[0] && x <= box[2] && z >= box[1] && z <= box[3] && y >= 0 && y <= box[4];

        private void Damage(NrPlayer victim, double amount, string attackerId, int gun, bool head, double dx, double dz)
        {
            if (!victim.Alive || victim.ProtectUntil > Now) return;
            var dmg = (int)Math.Round(amount);
            victim.Hp -= dmg;
            victim.LastHurtAt = Now;
            Events.Add(new object[] { "hit", victim.Id, attackerId, dmg, head ? 1 : 0, R3(dx), R3(dz) });
            if (victim.Hp > 0) return;
            victim.Hp = 0;
            victim.Alive = false;
            victim.BtOn = false;
            victim.RespawnAt = Now + RespawnSeconds;
            victim.Deaths++;
            if (Players.TryGetValue(attackerId, out var killer) && killer != victim)
            {
                killer.Kills++;
                killer.Meter = Math.Min(1, killer.Meter + KillBonus);
            }
            else if (killer == victim) victim.Kills = Math.Max(0, victim.Kills - 1);
            Events.Add(new object[] { "kill", victim.Id, attackerId, gun, head ? 1 : 0, R(victim.Body.X), R(victim.Body.Z) });
            if (killer != null && killer.Kills >= KillsToWin && Phase == NrPhase.Playing) EndMatch();
        }

        private void Explode(int k, string attackerId)
        {
            if (_barrelAt[k] > Now) return;
            _barrelAt[k] = Now + 25;
            var (bx, bz) = NoirMap.Barrels[k];
            Events.Add(new object[] { "boom", k, bx, bz });
            foreach (var p in Players.Values.ToList())
            {
                if (!p.Alive) continue;
                var d = Dist(p.Body.X, p.Body.Z, bx, bz);
                if (d > BarrelRadius) continue;
                Damage(p, 105 * (1 - d / BarrelRadius) + 10, attackerId, 4, false, (p.Body.X - bx) / Math.Max(d, 0.1), (p.Body.Z - bz) / Math.Max(d, 0.1));
            }
            for (var j = 0; j < NoirMap.Barrels.Length; j++)
            {
                if (j != k && _barrelAt[j] <= Now && Dist(NoirMap.Barrels[j].X, NoirMap.Barrels[j].Z, bx, bz) < 3.2) Explode(j, attackerId);
            }
        }

        private void Pickups()
        {
            for (var i = 0; i < NoirMap.Pickups.Length; i++)
            {
                if (_pickupAt[i] > Now) continue;
                var pk = NoirMap.Pickups[i];
                foreach (var p in Players.Values)
                {
                    if (!p.Alive || Dist(p.Body.X, p.Body.Z, pk.X, pk.Z) > 0.9) continue;
                    if (pk.Kind < 0)
                    {
                        if (p.Hp >= MaxHp) continue;
                        p.Hp = Math.Min(MaxHp, p.Hp + 40);
                        _pickupAt[i] = Now + 20;
                    }
                    else
                    {
                        if (p.Second != pk.Kind) { p.Second = pk.Kind; p.Slot = 1; p.ReloadT = 0; }
                        p.Ammo[1] = Guns[pk.Kind].Mag;
                        _pickupAt[i] = Now + 15;
                    }
                    Events.Add(new object[] { "pick", p.Id, pk.Kind, i });
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ bots
        private void BotThink(NrPlayer bot)
        {
            if (bot.BotDiveTicks > 0 && --bot.BotDiveTicks == 0) bot.Bits &= ~NoirPhysics.Dive;
            if (Now < bot.NextThink) return;
            bot.NextThink = Now + 0.1 + _random.NextDouble() * 0.05;
            var b = bot.Body;
            var enemies = Players.Values.Where(e => e != bot && e.Alive && e.InMatch).OrderBy(e => Dist(e.Body.X, e.Body.Z, b.X, b.Z)).ToList();
            NrPlayer? target = null;
            foreach (var e in enemies.Take(4))
            {
                if (Dist(e.Body.X, e.Body.Z, b.X, b.Z) > 32) break;
                if (Sight(b.X, 1.5, b.Z, e.Body.X, NoirPhysics.Height(e.Body) * 0.6, e.Body.Z)) { target = e; break; }
            }
            if (target?.Id != bot.Target) { bot.Target = target?.Id; bot.SeenSince = target == null ? -1 : Now; }

            (double X, double Z)? goal = null;
            if (bot.Hp < 50) goal = NearestPickup(b, k => k < 0, 22);
            if (goal == null && bot.Second < 0) goal = NearestPickup(b, k => k >= 0, target == null ? 40 : 10);

            double mx = 0, mz = 0;
            if (goal != null) (mx, mz) = PathDir(b, goal.Value);
            else if (target != null)
            {
                double tx = target.Body.X - b.X, tz = target.Body.Z - b.Z;
                var d = Math.Max(0.1, Math.Sqrt(tx * tx + tz * tz));
                tx /= d; tz /= d;
                if (Now > bot.StrafeUntil) { bot.StrafeSign = _random.NextDouble() < 0.5 ? -1 : 1; bot.StrafeUntil = Now + 0.7 + _random.NextDouble() * 1.1; }
                var want = bot.Gun == 1 ? 5 : 9;
                var push = d > want + 3 ? 0.8 : d < want - 3 ? -0.7 : 0;
                mx = -tz * bot.StrafeSign + tx * push;
                mz = tx * bot.StrafeSign + tz * push;
            }
            else if (enemies.Count > 0) (mx, mz) = PathDir(b, (enemies[0].Body.X, enemies[0].Body.Z));

            var bits = 0;
            if (Math.Abs(mx) + Math.Abs(mz) > 0.01) { bot.Yaw = Math.Atan2(mx, mz); bits = NoirPhysics.Fwd; }
            if (target != null)
            {
                var (ox, oy, oz) = Muzzle(bot);
                double ax = target.Body.X - ox, az = target.Body.Z - oz, ay = NoirPhysics.Height(target.Body) * 0.6 - oy;
                var flat = Math.Sqrt(ax * ax + az * az);
                if (_random.NextDouble() < 0.35)
                {
                    var wobble = BotWobble + flat * 0.004 + (target.Body.DiveT >= 0 ? 0.08 : 0) + (target.BtOn ? 0.05 : 0) + (Now - bot.SeenSince < 1.5 ? 0.05 : 0);
                    bot.AimErrYaw = Gauss() * wobble;
                    bot.AimErrPitch = Gauss() * wobble * 0.5;
                }
                bot.AimYaw = Math.Atan2(ax, az) + bot.AimErrYaw;
                bot.AimPitch = Math.Atan2(ay, flat) + bot.AimErrPitch;
                if (Now - bot.SeenSince > BotReaction + _random.NextDouble() * 0.4) bits |= NoirPhysics.Fire;
                if (Now - bot.LastHurtAt < 0.25 && b.DiveT < 0 && b.DiveCd <= 0 && _random.NextDouble() < 0.18)
                {
                    bits |= NoirPhysics.Dive;
                    bot.BotDiveTicks = 2;
                }
                if (!bot.BtOn && bot.Meter > 0.6 && flat < 14 && (bot.Hp < 45 || _random.NextDouble() < 0.004)) bot.WantBt = true;
                if (bot.BtOn && flat > 18) bot.WantBt = true;
            }
            else
            {
                bot.AimYaw = bot.Yaw;
                bot.AimPitch = 0;
                if (bot.BtOn) bot.WantBt = true;
                if (bot.Ammo[bot.Slot] < Guns[bot.Gun].Mag / 2) bot.WantReload = true;
            }
            bot.Bits = bits | (bot.Bits & NoirPhysics.Dive);
        }

        private (double X, double Z)? NearestPickup(NoirBody b, Func<int, bool> kind, double within)
        {
            (double X, double Z)? best = null;
            var bestD = within;
            for (var i = 0; i < NoirMap.Pickups.Length; i++)
            {
                var pk = NoirMap.Pickups[i];
                if (_pickupAt[i] > Now || !kind(pk.Kind)) continue;
                var d = Dist(pk.X, pk.Z, b.X, b.Z);
                if (d < bestD) { bestD = d; best = (pk.X, pk.Z); }
            }
            return best;
        }

        /// <summary>The direction to walk toward a point: the neighbouring grid cell with the shortest path, straight in when close.</summary>
        private (double X, double Z) PathDir(NoirBody b, (double X, double Z) goal)
        {
            double gx = goal.X - b.X, gz = goal.Z - b.Z;
            var gd = Math.Sqrt(gx * gx + gz * gz);
            if (gd < 0.2) return (0, 0);
            if (gd < 2.5 && Sight(b.X, 0.4, b.Z, goal.X, 0.4, goal.Z)) return (gx / gd, gz / gd);
            int cx = CellX(b.X), cz = CellZ(b.Z);
            var field = Field(CellX(goal.X), CellZ(goal.Z));
            var here = field[cz * Cols + cx];
            var best = here < 0 ? int.MaxValue : here;
            (int X, int Z) next = (cx, cz);
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                int nx = cx + dx, nz = cz + dz;
                if (Blocked(nx, nz)) continue;
                if (dx != 0 && dz != 0 && (Blocked(cx + dx, cz) || Blocked(cx, cz + dz))) continue;
                var v = field[nz * Cols + nx];
                if (v >= 0 && v < best) { best = v; next = (nx, nz); }
            }
            if (next == (cx, cz)) return (gx / gd, gz / gd);
            double wx = (next.X + 0.5) * Cell - b.X, wz = (next.Z + 0.5) * Cell - b.Z;
            var wd = Math.Max(0.01, Math.Sqrt(wx * wx + wz * wz));
            return (wx / wd, wz / wd);
        }

        private static int CellX(double x) => Math.Clamp((int)(x / Cell), 0, Cols - 1);
        private static int CellZ(double z) => Math.Clamp((int)(z / Cell), 0, Rows - 1);
        private bool Blocked(int x, int z) => x < 0 || z < 0 || x >= Cols || z >= Rows || _blocked[z * Cols + x];

        private static bool[] BuildGrid()
        {
            var grid = new bool[Cols * Rows];
            var pad = NoirPhysics.Half + 0.05;
            for (var z = 0; z < Rows; z++)
                for (var x = 0; x < Cols; x++)
                {
                    double px = (x + 0.5) * Cell, pz = (z + 0.5) * Cell;
                    grid[z * Cols + x] = NoirMap.Solids.Any(s => px > s[0] - pad && px < s[2] + pad && pz > s[1] - pad && pz < s[3] + pad);
                }
            return grid;
        }

        private int[] Field(int tx, int tz)
        {
            var key = tz * Cols + tx;
            if (_fields.TryGetValue(key, out var f)) return f;
            if (_fields.Count > 600) _fields.Clear();
            f = new int[Cols * Rows];
            Array.Fill(f, -1);
            var queue = new Queue<int>();
            f[key] = 0;
            queue.Enqueue(key);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int x = c % Cols, z = c / Cols;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (Blocked(nx, nz)) continue;
                    var n = nz * Cols + nx;
                    if (f[n] >= 0) continue;
                    f[n] = f[c] + 1;
                    queue.Enqueue(n);
                }
            }
            _fields[key] = f;
            return f;
        }

        /// <summary>Line of sight through the solid boxes, sampled every 0.25 m.</summary>
        private static bool Sight(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            var d = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1) + (z2 - z1) * (z2 - z1));
            var n = (int)Math.Ceiling(d / 0.25);
            for (var i = 1; i < n; i++)
            {
                var t = (double)i / n;
                double x = x1 + (x2 - x1) * t, y = y1 + (y2 - y1) * t, z = z1 + (z2 - z1) * t;
                foreach (var box in NoirMap.Solids) if (Inside(box, x, y, z)) return false;
            }
            return true;
        }

        private double Gauss()
        {
            var u = 1 - _random.NextDouble();
            var v = _random.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }

        private static double Dist(double x1, double z1, double x2, double z2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));
        private static double R(double v) => Math.Round(v, 2);
        private static double R3(double v) => Math.Round(v, 3);

        // ------------------------------------------------------------------ snapshot
        private string _rosterKey = "";
        private int _sinceRoster;

        public object Snapshot()
        {
            var timeLeft = Phase switch
            {
                NrPhase.Playing => Math.Max(0, MatchEnds - Now),
                NrPhase.Countdown or NrPhase.Over => Math.Max(0, PhaseUntil - Now),
                _ => 0
            };
            var ps = Players.Values.Where(p => p.InMatch).Select(p => new object[]
            {
                p.Id, R(p.Body.X), R(p.Body.Z), R3(p.Body.DiveT), R3(p.Body.DiveX), R3(p.Body.DiveZ), R3(p.Body.DiveCd),
                p.Body.Speed, p.Body.PrevDive ? 1 : 0, p.LastSeq, R3(p.AimYaw), R3(p.AimPitch), p.Hp, p.Alive ? 1 : 0,
                p.Slot, p.Second, p.Ammo[p.Slot], p.ReloadT > 0 ? 1 : 0, Math.Round(p.Meter, 2), p.BtOn ? 1 : 0,
                p.ProtectUntil > Now ? 1 : 0, p.Kills, p.Deaths, R(Math.Max(0, p.RespawnAt - Now)), R3(p.Yaw)
            }).ToArray();
            object? roster = null;
            var key = string.Join(";", Players.Values.Select(p => $"{p.Id}|{p.Name}|{p.Look}|{p.InMatch}"));
            if (key != _rosterKey || ++_sinceRoster >= 15)
            {
                _rosterKey = key;
                _sinceRoster = 0;
                roster = Players.Values.Select(p => new { id = p.Id, n = p.Name, bot = p.IsBot, lk = p.Look }).ToArray();
            }
            return new
            {
                ph = Phase.ToString().ToLowerInvariant(),
                now = R(Now),
                tl = R(timeLeft),
                size = Size,
                owner = OwnerId,
                win = Winner,
                ps,
                ro = roster,
                pk = _pickupAt.Select(t => t > Now ? 0 : 1).ToArray(),
                br = _barrelAt.Select(t => t > Now ? 0 : 1).ToArray(),
                ev = Events.ToArray()
            };
        }
    }
}
