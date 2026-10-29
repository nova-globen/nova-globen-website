using System.Security.Cryptography;

namespace Lek.Server.Spark
{
    public enum SparkPhase { Lobby, Countdown, Playing, Over }

    /// <summary>
    /// A character: a primary attack and one ability, each with a cooldown; the ability has a windup during which
    /// the character visibly "winds up" (you can see what is coming) and cannot move. Numbers are tuned so
    /// no character out-damages another over time; they differ in range, speed and what the ability does.
    /// </summary>
    public sealed record SparkKind(string Id, string Name, string Element, string Primary, string Ability,
        float PrimaryCooldown, float AbilityCooldown, float Windup)
    {
        public static readonly SparkKind Blaze = new("blaze", "Blaze", "fire", "Fireball: a slow heavy shot (34)", "Dash: a burst forward, untouchable for a blink", 0.6f, 5f, 0.15f);
        public static readonly SparkKind Frost = new("frost", "Frost", "ice", "Shard: a fast light shot (22) that slows", "Nova: a ring around you (25) that slows everyone it touches", 0.45f, 6f, 0.45f);
        public static readonly SparkKind Bolt = new("bolt", "Bolt", "lightning", "Zap: a short instant arc (18)", "Blink: vanish and reappear four tiles ahead", 0.32f, 5f, 0.3f);
        public static readonly SparkKind[] All = { Blaze, Frost, Bolt };
        public static SparkKind Find(string? id) => All.FirstOrDefault(item => item.Id == id) ?? Blaze;
    }

    public sealed class SparkPlayer
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public bool IsBot { get; init; }
        public SparkKind Kind { get; set; } = SparkKind.Blaze;
        public float X { get; set; }
        public float Y { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
        public float Aim { get; set; }         // radians
        public bool WantFire { get; set; }
        public bool WantAbility { get; set; }
        public int Hp { get; set; } = 100;
        public bool Alive { get; set; } = true;
        public bool InMatch { get; set; }
        public double RespawnAt { get; set; }
        public double PrimaryReady { get; set; }
        public double AbilityReady { get; set; }
        public double CastUntil { get; set; }  // winding up the ability until then
        public double DashUntil { get; set; }
        public double SlowUntil { get; set; }
        public double InvulnUntil { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Color { get; set; }
        public double NextBotThink { get; set; }
        public float BotDirX { get; set; }
        public float BotDirY { get; set; }
        public int? UserId { get; set; }
        public int TenantId { get; set; }
        public int SiteId { get; set; }
    }

    public sealed class SparkShot
    {
        public required string Owner { get; init; }
        public required string Kind { get; init; } // fireball, shard
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; init; }
        public float VY { get; init; }
        public int Damage { get; init; }
        public double Dies { get; init; }
    }

    public sealed class SparkEvent
    {
        public required string Kind { get; init; } // start, go, hit, kill, cast, zap, nova, blink, dash, respawn, over
        public string? A { get; init; }
        public string? B { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float X2 { get; init; }
        public float Y2 { get; init; }
    }

    /// <summary>One arena room: free-for-all, first to the kill target or the best score when time runs out.</summary>
    public sealed class SparkRoom
    {
        public const float Radius = 6f;
        public const float Speed = 100f;
        public const int MinPlayers = 2;
        public const int MaxPlayers = 6;
        public const int KillTarget = 10;
        public const double MatchSeconds = 180;
        public const double RespawnSeconds = 3;

        public object Sync { get; } = new();
        public string Code { get; }
        public string? OwnerId { get; set; }
        public SparkMap Map { get; } = SparkMap.Arena;
        public Dictionary<string, SparkPlayer> Players { get; } = new();
        public List<SparkShot> Shots { get; } = new();
        public SparkPhase Phase { get; private set; } = SparkPhase.Lobby;
        public double Now { get; private set; }
        public double PhaseUntil { get; private set; }
        public double MatchEnds { get; private set; }
        public string? WinnerId { get; private set; }
        public List<SparkEvent> Events { get; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        private int _botCounter;
        private readonly Random _random = new(RandomNumberGenerator.GetInt32(int.MaxValue));

        public SparkRoom(string code)
        {
            Code = code;
        }

        public int HumanCount => Players.Values.Count(item => !item.IsBot);

        public SparkPlayer AddPlayer(string id, string name, bool isBot)
        {
            var player = new SparkPlayer
            {
                Id = id,
                Name = name,
                IsBot = isBot,
                Kind = SparkKind.All[Players.Count % SparkKind.All.Length],
                Color = NextColor(),
                InMatch = Phase is SparkPhase.Countdown or SparkPhase.Playing,
                Alive = true
            };
            var spawn = Map.Spawns[Players.Count % Map.Spawns.Length];
            player.X = spawn.X;
            player.Y = spawn.Y;
            Players[id] = player;
            if (OwnerId == null && !isBot) OwnerId = id;
            LastActivity = DateTime.UtcNow;
            return player;
        }

        public SparkPlayer AddBot()
        {
            _botCounter++;
            return AddPlayer("bot-" + _botCounter, "Bot " + _botCounter, true);
        }

        public bool RemoveLastBot()
        {
            var bot = Players.Values.LastOrDefault(item => item.IsBot);
            if (bot == null) return false;
            RemovePlayer(bot.Id);
            return true;
        }

        public void RemovePlayer(string id)
        {
            if (!Players.Remove(id)) return;
            if (OwnerId == id) OwnerId = Players.Values.FirstOrDefault(item => !item.IsBot)?.Id;
            if (Phase is SparkPhase.Countdown or SparkPhase.Playing && Players.Values.Count(item => item.InMatch) < 2) EndMatch();
            LastActivity = DateTime.UtcNow;
        }

        public string? SetKind(string playerId, string kindId)
        {
            if (!Players.TryGetValue(playerId, out var player)) return "You are not in the room.";
            if (Phase != SparkPhase.Lobby) return "Characters are picked in the lobby.";
            player.Kind = SparkKind.Find(kindId);
            return null;
        }

        public string? Start()
        {
            if (Phase != SparkPhase.Lobby) return "A match is already running.";
            while (Players.Count < MinPlayers) AddBot();
            WinnerId = null;
            var spawns = Map.Spawns.OrderBy(_ => _random.Next()).ToList();
            var i = 0;
            foreach (var player in Players.Values)
            {
                player.InMatch = true;
                player.Alive = true;
                player.Hp = 100;
                player.Kills = player.Deaths = 0;
                player.PrimaryReady = player.AbilityReady = player.CastUntil = player.DashUntil = player.SlowUntil = 0;
                player.X = spawns[i % spawns.Count].X;
                player.Y = spawns[i % spawns.Count].Y;
                i++;
            }
            Shots.Clear();
            Phase = SparkPhase.Countdown;
            PhaseUntil = Now + 3.0;
            Events.Add(new SparkEvent { Kind = "start" });
            return null;
        }

        private void EndMatch()
        {
            var best = Players.Values.Where(item => item.InMatch).OrderByDescending(item => item.Kills).ThenBy(item => item.Deaths).FirstOrDefault();
            WinnerId = best?.Id;
            Phase = SparkPhase.Over;
            PhaseUntil = Now + 7.0;
            Shots.Clear();
            Events.Add(new SparkEvent { Kind = "over", A = WinnerId });
        }

        public void Tick(double now, float dt)
        {
            Now = now;
            switch (Phase)
            {
                case SparkPhase.Lobby:
                    MovePlayers(dt, false);
                    break;
                case SparkPhase.Countdown:
                    if (Now >= PhaseUntil)
                    {
                        Phase = SparkPhase.Playing;
                        MatchEnds = Now + MatchSeconds;
                        Events.Add(new SparkEvent { Kind = "go" });
                    }
                    break;
                case SparkPhase.Playing:
                    MovePlayers(dt, true);
                    Act();
                    MoveShots(dt);
                    Respawns();
                    if (Now >= MatchEnds || Players.Values.Any(item => item.InMatch && item.Kills >= KillTarget)) EndMatch();
                    break;
                case SparkPhase.Over:
                    if (Now >= PhaseUntil)
                    {
                        Phase = SparkPhase.Lobby;
                        foreach (var player in Players.Values) { player.InMatch = false; player.Alive = true; player.Hp = 100; }
                    }
                    break;
            }
        }

        private IEnumerable<SparkPlayer> Fighting() => Players.Values.Where(item => item.InMatch && item.Alive);

        private void MovePlayers(float dt, bool fighting)
        {
            foreach (var player in Players.Values)
            {
                if (fighting && !(player.InMatch && player.Alive)) continue;
                if (player.IsBot && fighting) ThinkBot(player);
                if (Now < player.CastUntil) continue; // winding up: rooted
                var ix = player.InputX; var iy = player.InputY;
                var speed = Speed;
                if (Now < player.DashUntil) { ix = MathF.Cos(player.Aim); iy = MathF.Sin(player.Aim); speed = 320f; }
                else if (Now < player.SlowUntil) speed *= 0.6f;
                var len = MathF.Sqrt(ix * ix + iy * iy);
                if (len < 0.05f) continue;
                if (len > 1f) { ix /= len; iy /= len; }
                var nx = player.X + ix * speed * dt;
                var ny = player.Y + iy * speed * dt;
                if (!Map.Blocked(nx, player.Y, Radius)) player.X = nx;
                if (!Map.Blocked(player.X, ny, Radius)) player.Y = ny;
            }
        }

        private void Act()
        {
            foreach (var player in Fighting())
            {
                // an ability that finished winding up resolves now
                if (player.CastUntil > 0 && Now >= player.CastUntil)
                {
                    player.CastUntil = 0;
                    Resolve(player);
                }
                if (Now < player.CastUntil) continue;
                if (player.WantAbility && Now >= player.AbilityReady)
                {
                    player.WantAbility = false;
                    player.AbilityReady = Now + player.Kind.AbilityCooldown;
                    player.CastUntil = Now + player.Kind.Windup;
                    Events.Add(new SparkEvent { Kind = "cast", A = player.Id, B = player.Kind.Id });
                    continue;
                }
                player.WantAbility = false;
                if (player.WantFire && Now >= player.PrimaryReady)
                {
                    player.PrimaryReady = Now + player.Kind.PrimaryCooldown;
                    Fire(player);
                }
                if (!player.IsBot) player.WantFire = false; // humans send fire per press; bots hold
            }
        }

        private void Fire(SparkPlayer player)
        {
            var cos = MathF.Cos(player.Aim); var sin = MathF.Sin(player.Aim);
            switch (player.Kind.Id)
            {
                case "blaze":
                    Shots.Add(new SparkShot { Owner = player.Id, Kind = "fireball", X = player.X + cos * 8, Y = player.Y + sin * 8, VX = cos * 210, VY = sin * 210, Damage = 34, Dies = Now + 1.4 });
                    break;
                case "frost":
                    Shots.Add(new SparkShot { Owner = player.Id, Kind = "shard", X = player.X + cos * 8, Y = player.Y + sin * 8, VX = cos * 320, VY = sin * 320, Damage = 22, Dies = Now + 1.0 });
                    break;
                case "bolt":
                    // an instant arc, 90 px, the first enemy within 10 px of the line
                    var ex = player.X + cos * 90; var ey = player.Y + sin * 90;
                    SparkPlayer? hit = null; var bestT = float.MaxValue;
                    foreach (var other in Fighting())
                    {
                        if (other.Id == player.Id || Now < other.InvulnUntil) continue;
                        var t = ((other.X - player.X) * cos + (other.Y - player.Y) * sin);
                        if (t < 0 || t > 90) continue;
                        var px = player.X + cos * t; var py = player.Y + sin * t;
                        if ((other.X - px) * (other.X - px) + (other.Y - py) * (other.Y - py) > 10 * 10) continue;
                        if (!Map.Clear(player.X, player.Y, other.X, other.Y)) continue;
                        if (t < bestT) { bestT = t; hit = other; }
                    }
                    if (hit != null) { ex = hit.X; ey = hit.Y; Damage(hit, player, 18); }
                    Events.Add(new SparkEvent { Kind = "zap", A = player.Id, X = player.X, Y = player.Y, X2 = ex, Y2 = ey });
                    break;
            }
        }

        private void Resolve(SparkPlayer player)
        {
            switch (player.Kind.Id)
            {
                case "blaze":
                    player.DashUntil = Now + 0.22;
                    player.InvulnUntil = Now + 0.3;
                    Events.Add(new SparkEvent { Kind = "dash", A = player.Id });
                    break;
                case "frost":
                    foreach (var other in Fighting().Where(item => item.Id != player.Id).ToList())
                    {
                        var d2 = (other.X - player.X) * (other.X - player.X) + (other.Y - player.Y) * (other.Y - player.Y);
                        if (d2 <= 60 * 60 && Now >= other.InvulnUntil) { other.SlowUntil = Now + 2.0; Damage(other, player, 25); }
                    }
                    Events.Add(new SparkEvent { Kind = "nova", A = player.Id, X = player.X, Y = player.Y });
                    break;
                case "bolt":
                    var cos = MathF.Cos(player.Aim); var sin = MathF.Sin(player.Aim);
                    var fromX = player.X; var fromY = player.Y;
                    for (var d = 64f; d > 8; d -= 8)
                    {
                        var nx = player.X + cos * d; var ny = player.Y + sin * d;
                        if (!Map.Blocked(nx, ny, Radius) && Map.Clear(player.X, player.Y, nx, ny)) { player.X = nx; player.Y = ny; break; }
                    }
                    Events.Add(new SparkEvent { Kind = "blink", A = player.Id, X = fromX, Y = fromY, X2 = player.X, Y2 = player.Y });
                    break;
            }
        }

        private void MoveShots(float dt)
        {
            for (var i = Shots.Count - 1; i >= 0; i--)
            {
                var shot = Shots[i];
                shot.X += shot.VX * dt;
                shot.Y += shot.VY * dt;
                if (Now >= shot.Dies || Map.Blocked(shot.X, shot.Y, 2f)) { Shots.RemoveAt(i); continue; }
                foreach (var other in Fighting())
                {
                    if (other.Id == shot.Owner || Now < other.InvulnUntil) continue;
                    var d2 = (other.X - shot.X) * (other.X - shot.X) + (other.Y - shot.Y) * (other.Y - shot.Y);
                    if (d2 > (Radius + 3) * (Radius + 3)) continue;
                    if (Players.TryGetValue(shot.Owner, out var owner))
                    {
                        if (shot.Kind == "shard") other.SlowUntil = Now + 1.5;
                        Damage(other, owner, shot.Damage);
                    }
                    Shots.RemoveAt(i);
                    break;
                }
            }
        }

        private void Damage(SparkPlayer victim, SparkPlayer by, int amount)
        {
            if (!victim.Alive) return;
            victim.Hp -= amount;
            Events.Add(new SparkEvent { Kind = "hit", A = by.Id, B = victim.Id, X = victim.X, Y = victim.Y });
            if (victim.Hp > 0) return;
            victim.Hp = 0;
            victim.Alive = false;
            victim.Deaths++;
            victim.RespawnAt = Now + RespawnSeconds;
            victim.CastUntil = 0;
            by.Kills++;
            Events.Add(new SparkEvent { Kind = "kill", A = by.Id, B = victim.Id, X = victim.X, Y = victim.Y });
        }

        private void Respawns()
        {
            foreach (var player in Players.Values.Where(item => item.InMatch && !item.Alive && Now >= item.RespawnAt))
            {
                // the spawn farthest from everyone alive
                var spawn = Map.Spawns.OrderByDescending(s => Fighting().Select(f => (f.X - s.X) * (f.X - s.X) + (f.Y - s.Y) * (f.Y - s.Y)).DefaultIfEmpty(0).Min()).First();
                player.X = spawn.X; player.Y = spawn.Y;
                player.Hp = 100;
                player.Alive = true;
                player.InvulnUntil = Now + 1.0;
                player.SlowUntil = 0;
                Events.Add(new SparkEvent { Kind = "respawn", A = player.Id });
            }
        }

        private void ThinkBot(SparkPlayer bot)
        {
            if (Now < bot.NextBotThink) { bot.InputX = bot.BotDirX; bot.InputY = bot.BotDirY; return; }
            bot.NextBotThink = Now + 0.15 + _random.NextDouble() * 0.1;
            var target = Fighting().Where(item => item.Id != bot.Id)
                .OrderBy(item => (item.X - bot.X) * (item.X - bot.X) + (item.Y - bot.Y) * (item.Y - bot.Y)).FirstOrDefault();
            bot.WantFire = false;
            if (target == null) { bot.BotDirX = bot.BotDirY = 0; return; }
            var dx = target.X - bot.X; var dy = target.Y - bot.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            var sees = Map.Clear(bot.X, bot.Y, target.X, target.Y);
            // aim a little ahead of a moving target, with a human-like wobble
            bot.Aim = MathF.Atan2(dy + target.InputY * 10, dx + target.InputX * 10) + (float)(_random.NextDouble() - 0.5) * 0.2f;
            var ideal = bot.Kind.Id == "bolt" ? 60f : 110f;
            float wx, wy;
            if (!sees) { wx = dx / dist; wy = dy / dist; }
            else if (dist > ideal + 20) { wx = dx / dist; wy = dy / dist; }
            else if (dist < ideal - 20) { wx = -dx / dist; wy = -dy / dist; }
            else { wx = -dy / dist; wy = dx / dist; if (_random.Next(2) == 0) { wx = -wx; wy = -wy; } } // strafe
            if (sees && (bot.Kind.Id != "bolt" || dist < 85)) bot.WantFire = true;
            if (sees && Now >= bot.AbilityReady)
            {
                if (bot.Kind.Id == "frost" && dist < 55) bot.WantAbility = true;
                if (bot.Kind.Id == "bolt" && dist > 120) bot.WantAbility = true;
                if (bot.Kind.Id == "blaze" && bot.Hp < 40) { bot.Aim = MathF.Atan2(-dy, -dx); bot.WantAbility = true; }
            }
            // steer around walls
            float bestScore = float.MinValue, bx = 0, by = 0;
            for (var i = 0; i < 12; i++)
            {
                var a = i * MathF.PI * 2 / 12;
                var cx = MathF.Cos(a); var cy = MathF.Sin(a);
                var score = cx * wx + cy * wy + (float)(_random.NextDouble() * 0.2);
                if (Map.Blocked(bot.X + cx * 20, bot.Y + cy * 20, Radius)) score -= 2f;
                if (score > bestScore) { bestScore = score; bx = cx; by = cy; }
            }
            bot.BotDirX = bx; bot.BotDirY = by;
            bot.InputX = bx; bot.InputY = by;
        }

        private int NextColor()
        {
            var used = Players.Values.Select(item => item.Color).ToHashSet();
            for (var i = 0; i < 10; i++) if (!used.Contains(i)) return i;
            return _random.Next(10);
        }

        public object Snapshot()
        {
            return new
            {
                code = Code,
                phase = Phase.ToString().ToLowerInvariant(),
                owner = OwnerId,
                winner = WinnerId,
                countdown = Phase == SparkPhase.Countdown ? Math.Max(0, PhaseUntil - Now) : 0,
                timeLeft = Phase == SparkPhase.Playing ? Math.Max(0, MatchEnds - Now) : 0,
                target = KillTarget,
                players = Players.Values.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    bot = item.IsBot,
                    kind = item.Kind.Id,
                    x = MathF.Round(item.X, 1),
                    y = MathF.Round(item.Y, 1),
                    aim = MathF.Round(item.Aim, 2),
                    hp = item.Hp,
                    alive = item.Alive,
                    inMatch = item.InMatch,
                    kills = item.Kills,
                    deaths = item.Deaths,
                    color = item.Color,
                    cast = item.CastUntil > Now ? MathF.Round((float)(item.CastUntil - Now), 2) : 0f,
                    cd1 = Math.Max(0, MathF.Round((float)(item.PrimaryReady - Now), 2)),
                    cd2 = Math.Max(0, MathF.Round((float)(item.AbilityReady - Now), 2)),
                    slow = item.SlowUntil > Now,
                    dash = item.DashUntil > Now,
                    inv = item.InvulnUntil > Now,
                    respawn = !item.Alive && item.InMatch ? Math.Max(0, item.RespawnAt - Now) : 0
                }).ToArray(),
                shots = Shots.Select(item => new { kind = item.Kind, x = MathF.Round(item.X, 1), y = MathF.Round(item.Y, 1), a = MathF.Round(MathF.Atan2(item.VY, item.VX), 2) }).ToArray(),
                events = Events.Select(item => new { kind = item.Kind, a = item.A, b = item.B, x = item.X, y = item.Y, x2 = item.X2, y2 = item.Y2 }).ToArray()
            };
        }
    }
}
