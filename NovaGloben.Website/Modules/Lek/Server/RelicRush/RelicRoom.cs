using System.Security.Cryptography;

namespace Lek.Server.RelicRush
{
    public enum RrPhase { Lobby, Countdown, Playing, Scored, Over }

    public sealed class RrPlayer
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public string? ConnectionId { get; init; }
        public bool IsBot { get; init; }
        public bool AutoBot { get; init; }
        public int Team { get; set; }
        public int Look { get; init; }
        public RelicBody Body { get; } = new();
        public bool InMatch { get; set; }
        public int Bits { get; set; }
        public double Aim { get; set; }
        public int LastSeq { get; set; }
        public Queue<(int Seq, int Bits, double Aim)> Inputs { get; } = new();
        public bool WantSteal { get; set; }
        public bool WantPass { get; set; }
        public double StealCd { get; set; }
        public double PassCd { get; set; }
        public double ImmuneUntil { get; set; }
        public double LostAt { get; set; } = -10;
        public string? LostTo { get; set; }
        public int Scored { get; set; }
        public double NextThink { get; set; }
        public int BotDashTicks { get; set; }
        public int? UserId { get; set; }
        public int TenantId { get; set; }
        public int SiteId { get; set; }
    }

    public sealed class RrEvent
    {
        public required string K { get; init; }
        public string? A { get; init; }
        public string? B { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double V { get; init; }
    }

    /// <summary>
    /// One Relic Rush room. Two teams, one relic: carry it to your own shrine.
    /// Each team sees only its own shrine until a carrier gets close to it. Passing is instant to a teammate in range
    /// and in sight; stealing takes a button press up close. First to <see cref="ScoreToWin"/>, or the higher score
    /// when the clock runs out; a tie is a draw. Runs at a fixed 30 Hz from <see cref="RelicLoop"/>.
    /// </summary>
    public sealed class RelicRoom
    {
        public const int MaxPlayers = 8;
        public const int ScoreToWin = 3;
        public const double MatchSeconds = 240;
        public const double StealRange = 20, PassRange = 120, ShrineRadius = 14, RevealRange = 80;
        private const double T = RelicMaze.Tile;
        private const double Dt = RelicPhysics.Dt;
        private static readonly string[] BotNames = { "Rostam", "Sohrab", "Arash", "Tahmineh", "Kaveh", "Gordafarid", "Babak", "Roudabeh", "Zal", "Simin" };

        public object Sync { get; } = new();
        public string Code { get; }
        public string? OwnerId { get; set; }
        public int TeamSize { get; private set; } = 2;
        public RelicMaze Maze { get; private set; }
        public Dictionary<string, RrPlayer> Players { get; } = new();
        public RrPhase Phase { get; private set; } = RrPhase.Lobby;
        public double Now { get; private set; }
        public double PhaseUntil { get; private set; }
        public double MatchEnds { get; private set; }
        public int[] Score { get; } = new int[2];
        public int Winner { get; private set; } = -1;
        public string? Holder { get; private set; }
        public (double X, double Y)[] Shrines { get; } = new (double, double)[2];
        public List<RrEvent> Events { get; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public long Tick { get; private set; }
        public long LastSentTick { get; set; } = -100;
        public int MazeVersion { get; private set; }

        private readonly Random _random = new(RandomNumberGenerator.GetInt32(int.MaxValue));
        private readonly Func<int, int, bool> _wall;
        private readonly Dictionary<int, int[]> _fields = new();
        private int _botCounter;

        public RelicRoom(string code)
        {
            Code = code;
            Maze = new RelicMaze(_random.Next());
            _wall = (x, y) => Maze.Wall(x, y);
        }

        public int HumanCount => Players.Values.Count(p => !p.IsBot);

        // ------------------------------------------------------------------ lobby
        public RrPlayer AddPlayer(string id, string name, string? connectionId, bool isBot, bool autoBot = false, int? team = null)
        {
            var p = new RrPlayer
            {
                Id = id, Name = name, ConnectionId = connectionId, IsBot = isBot, AutoBot = autoBot, Look = _random.Next(6),
                Team = team ?? (Players.Values.Count(x => x.Team == 0) <= Players.Values.Count(x => x.Team == 1) ? 0 : 1)
            };
            Players[id] = p;
            if (OwnerId == null && !isBot) OwnerId = id;
            if (Phase != RrPhase.Lobby) p.InMatch = false;
            LastActivity = DateTime.UtcNow;
            return p;
        }

        public void RemovePlayer(string id)
        {
            if (!Players.Remove(id)) return;
            if (OwnerId == id) OwnerId = Players.Values.FirstOrDefault(p => !p.IsBot)?.Id;
            if (Holder == id) GiveRelicTo(Players.Values.Where(p => p.InMatch).OrderBy(_ => _random.Next()).FirstOrDefault(), null, "drop");
            if (Phase != RrPhase.Lobby && HumanCount == 0) BackToLobby();
            LastActivity = DateTime.UtcNow;
        }

        public string? SetTeam(string playerId, int team)
        {
            if (!Players.TryGetValue(playerId, out var p)) return "You are not in the room.";
            if (Phase != RrPhase.Lobby) return "Teams are picked in the lobby.";
            p.Team = team == 1 ? 1 : 0;
            return null;
        }

        public string? SetMode(int size)
        {
            if (Phase != RrPhase.Lobby) return "The match is running.";
            TeamSize = Math.Clamp(size, 1, 4);
            return null;
        }

        public string? Start()
        {
            if (Phase != RrPhase.Lobby) return "A match is already running.";
            var humans = Players.Values.Where(p => !p.IsBot).ToList();
            if (humans.Count > TeamSize * 2) return $"Too many players for {TeamSize} against {TeamSize}: pick a bigger mode.";
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            for (var team = 0; team < 2; team++)
            {
                var side = humans.Where(p => p.Team == team).ToList();
                while (side.Count > TeamSize) { side[^1].Team = 1 - team; side.RemoveAt(side.Count - 1); }
            }
            for (var team = 0; team < 2; team++)
                while (Players.Values.Count(p => p.Team == team) < TeamSize)
                {
                    _botCounter++;
                    var used = Players.Values.Select(p => p.Name).ToHashSet();
                    var name = BotNames.FirstOrDefault(n => !used.Contains(n + " (bot)")) ?? "Bot " + _botCounter;
                    AddPlayer("bot-" + _botCounter, name + " (bot)", null, true, true, team);
                }
            Maze = new RelicMaze(_random.Next());
            MazeVersion++;
            _fields.Clear();
            Score[0] = Score[1] = 0;
            Winner = -1;
            foreach (var p in Players.Values) { p.InMatch = true; p.Scored = 0; }
            MatchEnds = 0;
            StartRound(_random.Next(2));
            return null;
        }

        private void StartRound(int relicTeam)
        {
            // spawns: each team on its own outer column of rooms
            var index = new int[2];
            foreach (var p in Players.Values.Where(p => p.InMatch).OrderBy(_ => _random.Next()))
            {
                var cy = new[] { 3, 1, 5, 0, 6, 2, 4 }[index[p.Team]++ % 7];
                var (x, y) = RelicMaze.RoomCenter(p.Team == 0 ? 0 : RelicMaze.CellsX - 1, cy);
                var b = p.Body;
                b.X = x + (_random.NextDouble() - 0.5) * 8;
                b.Y = y + (_random.NextDouble() - 0.5) * 8;
                b.DashT = 0; b.DashCd = 0; b.Speed = 1; b.PrevDash = false;
                b.FaceX = p.Team == 0 ? 1 : -1; b.FaceY = 0;
                p.StealCd = p.PassCd = 0; p.ImmuneUntil = 0; p.LostTo = null;
            }
            PlaceShrines();
            var carrier = Players.Values.Where(p => p.InMatch && p.Team == relicTeam).OrderBy(_ => _random.Next()).FirstOrDefault();
            Holder = carrier?.Id;
            if (carrier != null) carrier.ImmuneUntil = Now + 3 + 1.5;
            Phase = RrPhase.Countdown;
            PhaseUntil = Now + 3;
            Events.Add(new RrEvent { K = "round", A = Holder, V = Score[0] + Score[1] + 1 });
        }

        /// <summary>Shrines in rooms away from both spawn columns and from each other, a fair walk for both teams.</summary>
        private void PlaceShrines()
        {
            var rooms = new List<(int X, int Y)>();
            for (var cx = 3; cx <= RelicMaze.CellsX - 4; cx++) for (var cy = 0; cy < RelicMaze.CellsY; cy++) rooms.Add((cx, cy));
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var a = rooms[_random.Next(rooms.Count)];
                var b = rooms[_random.Next(rooms.Count)];
                var pa = RelicMaze.RoomCenter(a.X, a.Y);
                var pb = RelicMaze.RoomCenter(b.X, b.Y);
                var field = Field((int)(pa.X / T), (int)(pa.Y / T));
                var apart = field[(int)(pb.Y / T) * RelicMaze.Cols + (int)(pb.X / T)];
                if (apart < 14 && attempt < 150) continue;
                Shrines[0] = pa;
                Shrines[1] = pb;
                return;
            }
        }

        private void EndMatch()
        {
            Winner = Score[0] == Score[1] ? -1 : (Score[0] > Score[1] ? 0 : 1);
            Phase = RrPhase.Over;
            PhaseUntil = Now + 8;
            Events.Add(new RrEvent { K = "over", V = Winner });
        }

        private void BackToLobby()
        {
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            foreach (var p in Players.Values) p.InMatch = false;
            Holder = null;
            Phase = RrPhase.Lobby;
        }

        // ------------------------------------------------------------------ input
        public void QueueInput(RrPlayer p, int seq, int bits, double aim)
        {
            p.Inputs.Enqueue((seq, bits, aim));
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
                case RrPhase.Lobby:
                    foreach (var p in Players.Values) p.Inputs.Clear();
                    return;
                case RrPhase.Countdown when Now >= PhaseUntil:
                    Phase = RrPhase.Playing;
                    if (MatchEnds <= 0) MatchEnds = Now + MatchSeconds;
                    Events.Add(new RrEvent { K = "go" });
                    break;
                case RrPhase.Scored when Now >= PhaseUntil:
                    if (Score[0] >= ScoreToWin || Score[1] >= ScoreToWin) EndMatch();
                    else StartRound(1 - _lastScorer);
                    return;
                case RrPhase.Over when Now >= PhaseUntil:
                    BackToLobby();
                    return;
            }
            if (Phase == RrPhase.Over) return;
            var playing = Phase == RrPhase.Playing;
            var holder = Holder != null && Players.TryGetValue(Holder, out var h) ? h : null;
            foreach (var p in Players.Values.ToList())
            {
                if (!p.InMatch) { p.Inputs.Clear(); continue; }
                if (p.IsBot && playing) BotThink(p, holder);
                p.StealCd = Math.Max(0, p.StealCd - Dt);
                p.PassCd = Math.Max(0, p.PassCd - Dt);
                p.Body.Speed = p.Id == Holder ? 0.9 : 1;
                var mask = playing ? ~0 : 0;
                var steps = !p.IsBot && p.Inputs.Count > 3 ? 2 : 1;
                for (var s = 0; s < steps; s++)
                {
                    if (!p.IsBot && p.Inputs.Count > 0)
                    {
                        var input = p.Inputs.Dequeue();
                        p.LastSeq = input.Seq;
                        p.Bits = input.Bits;
                        if (!double.IsNaN(input.Aim)) p.Aim = input.Aim;
                    }
                    RelicPhysics.Step(p.Body, p.Bits & mask, _wall);
                }
            }
            if (!playing) return;
            foreach (var p in Players.Values.Where(p => p.InMatch).ToList())
            {
                if (p.WantSteal) { p.WantSteal = false; TrySteal(p); }
                if (p.WantPass) { p.WantPass = false; TryPass(p); }
            }
            CheckScore();
            if (Phase == RrPhase.Playing && Now >= MatchEnds) EndMatch();
        }

        private int _lastScorer;

        private void CheckScore()
        {
            if (Holder == null || !Players.TryGetValue(Holder, out var h)) return;
            var shrine = Shrines[h.Team];
            if (Dist(h.Body.X, h.Body.Y, shrine.X, shrine.Y) > ShrineRadius) return;
            Score[h.Team]++;
            h.Scored++;
            _lastScorer = h.Team;
            Phase = RrPhase.Scored;
            PhaseUntil = Now + 3.5;
            Events.Add(new RrEvent { K = "score", A = h.Id, V = h.Team, X = R(shrine.X), Y = R(shrine.Y) });
        }

        private void TrySteal(RrPlayer p)
        {
            if (Holder == null || !Players.TryGetValue(Holder, out var h) || h.Team == p.Team || p.StealCd > 0) return;
            p.StealCd = 0.6;
            Events.Add(new RrEvent { K = "grab", A = p.Id, X = R(p.Body.X), Y = R(p.Body.Y) });
            if (Dist(p.Body.X, p.Body.Y, h.Body.X, h.Body.Y) > StealRange) return;
            if (h.ImmuneUntil > Now) { Events.Add(new RrEvent { K = "guard", A = h.Id, X = R(h.Body.X), Y = R(h.Body.Y) }); return; }
            // the player who just lost it cannot snatch it straight back from the thief
            if (p.LostTo == h.Id && Now - p.LostAt < 1.2) return;
            h.LostAt = Now;
            h.LostTo = p.Id;
            GiveRelicTo(p, h, "steal");
        }

        private void TryPass(RrPlayer p)
        {
            if (Holder != p.Id || p.PassCd > 0) return;
            var ax = Math.Cos(p.Aim);
            var ay = Math.Sin(p.Aim);
            RrPlayer? best = null;
            var bestScore = double.MaxValue;
            foreach (var m in Players.Values)
            {
                if (m == p || !m.InMatch || m.Team != p.Team) continue;
                var dx = m.Body.X - p.Body.X;
                var dy = m.Body.Y - p.Body.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d > PassRange || !LineOfSight(p.Body.X, p.Body.Y, m.Body.X, m.Body.Y)) continue;
                var angle = Math.Acos(Math.Clamp((dx * ax + dy * ay) / Math.Max(1, d), -1, 1));
                var score = angle * 60 + d * 0.2;
                if (score < bestScore) { bestScore = score; best = m; }
            }
            if (best == null) { Events.Add(new RrEvent { K = "nopass", A = p.Id }); p.PassCd = 0.3; return; }
            p.PassCd = 0.8;
            GiveRelicTo(best, p, "pass");
        }

        private void GiveRelicTo(RrPlayer? to, RrPlayer? from, string how)
        {
            Holder = to?.Id;
            if (to == null) return;
            to.ImmuneUntil = Now + (how == "steal" ? 0.8 : 0.5);
            to.PassCd = Math.Max(to.PassCd, 0.3);
            Events.Add(new RrEvent
            {
                K = how, A = from?.Id, B = to.Id,
                X = R(from?.Body.X ?? to.Body.X), Y = R(from?.Body.Y ?? to.Body.Y), V = 0
            });
        }

        private bool LineOfSight(double x1, double y1, double x2, double y2)
        {
            var d = Dist(x1, y1, x2, y2);
            var n = (int)Math.Ceiling(d / 4);
            for (var i = 1; i < n; i++)
            {
                var k = (double)i / n;
                if (Maze.Wall((int)Math.Floor((x1 + (x2 - x1) * k) / T), (int)Math.Floor((y1 + (y2 - y1) * k) / T))) return false;
            }
            return true;
        }

        private int[] Field(int tx, int ty)
        {
            var key = ty * RelicMaze.Cols + tx;
            if (!_fields.TryGetValue(key, out var f)) _fields[key] = f = Maze.DistanceFrom(tx, ty);
            return f;
        }

        // ------------------------------------------------------------------ bots
        private void BotThink(RrPlayer bot, RrPlayer? holder)
        {
            if (bot.BotDashTicks > 0 && --bot.BotDashTicks == 0) bot.Bits &= ~RelicPhysics.Dash;
            if (Now < bot.NextThink) return;
            bot.NextThink = Now + 0.1 + _random.NextDouble() * 0.06;
            var b = bot.Body;
            (double X, double Y) target;
            var wantDash = false;
            if (holder == null) target = (b.X, b.Y);
            else if (holder == bot)
            {
                target = Shrines[bot.Team];
                var threat = Players.Values.Where(e => e.InMatch && e.Team != bot.Team).OrderBy(e => Dist(e.Body.X, e.Body.Y, b.X, b.Y)).FirstOrDefault();
                var threatDist = threat == null ? 999 : Dist(threat.Body.X, threat.Body.Y, b.X, b.Y);
                if (threatDist < 34)
                {
                    // pass to a teammate in sight who has more room, else dash away
                    var mate = Players.Values.Where(m => m != bot && m.InMatch && m.Team == bot.Team && Dist(m.Body.X, m.Body.Y, b.X, b.Y) < RelicRoom.PassRange && LineOfSight(b.X, b.Y, m.Body.X, m.Body.Y))
                        .OrderByDescending(m => Players.Values.Where(e => e.InMatch && e.Team != bot.Team).Min(e => Dist(e.Body.X, e.Body.Y, m.Body.X, m.Body.Y))).FirstOrDefault();
                    if (mate != null && _random.NextDouble() < 0.6) { bot.Aim = Math.Atan2(mate.Body.Y - b.Y, mate.Body.X - b.X); bot.WantPass = true; }
                    else wantDash = true;
                }
            }
            else if (holder.Team == bot.Team)
            {
                // escort: stay a little ahead of the carrier, toward the shrine
                var near = Dist(holder.Body.X, holder.Body.Y, b.X, b.Y);
                target = near > 70 ? (holder.Body.X, holder.Body.Y) : Shrines[bot.Team];
            }
            else
            {
                target = (holder.Body.X, holder.Body.Y);
                var d = Dist(holder.Body.X, holder.Body.Y, b.X, b.Y);
                if (d < StealRange - 2 && bot.StealCd <= 0 && _random.NextDouble() < 0.85) bot.WantSteal = true;
                if (d < 60 && d > 24 && _random.NextDouble() < 0.15) wantDash = true;
            }
            var bits = Steer(b, target);
            if (wantDash && b.DashCd <= 0 && (bot.Bits & RelicPhysics.Dash) == 0) { bits |= RelicPhysics.Dash; bot.BotDashTicks = 3; }
            else bits |= bot.Bits & RelicPhysics.Dash;
            bot.Bits = bits;
            if (holder != bot) bot.Aim = Math.Atan2(target.Y - b.Y, target.X - b.X);
        }

        /// <summary>Walks toward a point along the maze: the neighbouring tile with the shortest path, then straight in.</summary>
        private int Steer(RelicBody b, (double X, double Y) target)
        {
            int tx = (int)Math.Floor(target.X / T), ty = (int)Math.Floor(target.Y / T);
            int bx = (int)Math.Floor(b.X / T), by = (int)Math.Floor(b.Y / T);
            double gx, gy;
            if (Math.Abs(bx - tx) + Math.Abs(by - ty) <= 1 && LineOfSight(b.X, b.Y, target.X, target.Y)) { gx = target.X; gy = target.Y; }
            else
            {
                var field = Field(Math.Clamp(tx, 0, RelicMaze.Cols - 1), Math.Clamp(ty, 0, RelicMaze.Height - 1));
                var here = field[by * RelicMaze.Cols + bx];
                (int X, int Y) next = (bx, by);
                var best = here < 0 ? int.MaxValue : here;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    int nx = bx + dx, ny = by + dy;
                    if (Maze.Wall(nx, ny)) continue;
                    if (dx != 0 && dy != 0 && (Maze.Wall(bx + dx, by) || Maze.Wall(bx, by + dy))) continue;
                    var v = field[ny * RelicMaze.Cols + nx];
                    if (v >= 0 && v < best) { best = v; next = (nx, ny); }
                }
                gx = next.X * T + T / 2.0;
                gy = next.Y * T + T / 2.0;
            }
            var bits = 0;
            if (gx > b.X + 2) bits |= RelicPhysics.Right;
            if (gx < b.X - 2) bits |= RelicPhysics.Left;
            if (gy > b.Y + 2) bits |= RelicPhysics.Down;
            if (gy < b.Y - 2) bits |= RelicPhysics.Up;
            return bits;
        }

        private static double Dist(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
        private static double R(double v) => Math.Round(v, 1);

        // ------------------------------------------------------------------ snapshot
        private string _rosterKey = "";
        private int _sinceRoster;
        private int _sentMaze = -1;

        /// <summary>One snapshot per team: the same game, but each team sees only its own shrine until a carrier
        /// is close enough to reveal it.</summary>
        public (object Team0, object Team1) Snapshots()
        {
            var timeLeft = Phase switch
            {
                RrPhase.Playing => Math.Max(0, MatchEnds - Now),
                RrPhase.Countdown or RrPhase.Scored or RrPhase.Over => Math.Max(0, PhaseUntil - Now),
                _ => 0
            };
            var ps = Players.Values.Select(p => new object[]
            {
                p.Id, R(p.Body.X), R(p.Body.Y), Math.Round(p.Body.DashT, 2), Math.Round(p.Body.DashX, 2), Math.Round(p.Body.DashY, 2),
                Math.Round(p.Body.DashCd, 2), Math.Round(p.Body.FaceX, 2), Math.Round(p.Body.FaceY, 2), p.Body.Speed, p.Body.PrevDash ? 1 : 0,
                p.LastSeq, Math.Round(p.Aim, 2), p.InMatch ? 1 : 0, Math.Round(p.StealCd, 1), p.ImmuneUntil > Now ? 1 : 0
            }).ToArray();
            object? roster = null;
            var key = string.Join(";", Players.Values.Select(p => $"{p.Id}|{p.Name}|{p.Team}|{p.Look}|{p.Scored}"));
            if (key != _rosterKey || ++_sinceRoster >= 15)
            {
                _rosterKey = key;
                _sinceRoster = 0;
                roster = Players.Values.Select(p => new { id = p.Id, n = p.Name, tm = p.Team, bot = p.IsBot, lk = p.Look, sc = p.Scored }).ToArray();
            }
            string[]? maze = null;
            if (_sentMaze != MazeVersion || _sinceRoster == 0) { maze = Maze.Rows; _sentMaze = MazeVersion; }
            var holder = Holder != null && Players.TryGetValue(Holder, out var h) ? h : null;
            object Build(int team)
            {
                var shrines = new List<object>();
                for (var t = 0; t < 2; t++)
                {
                    if (Phase == RrPhase.Lobby) break;
                    var revealed = t == team || (holder != null && holder.Team == t && Dist(holder.Body.X, holder.Body.Y, Shrines[t].X, Shrines[t].Y) < RevealRange) || Phase is RrPhase.Scored or RrPhase.Over;
                    if (revealed) shrines.Add(new object[] { t, R(Shrines[t].X), R(Shrines[t].Y) });
                }
                return new
                {
                    ph = Phase.ToString().ToLowerInvariant(),
                    now = R(Now),
                    tl = R(timeLeft),
                    sc = Score,
                    win = Winner,
                    mode = TeamSize,
                    owner = OwnerId,
                    hold = Holder,
                    ps,
                    ro = roster,
                    mz = maze,
                    mv = MazeVersion,
                    sh = shrines,
                    ev = Events.Select(e => new { k = e.K, a = e.A, b = e.B, x = e.X, y = e.Y, v = e.V }).ToArray()
                };
            }
            return (Build(0), Build(1));
        }
    }
}
