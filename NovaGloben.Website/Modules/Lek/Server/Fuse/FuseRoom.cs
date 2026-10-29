using System.Security.Cryptography;

namespace Lek.Server.Fuse
{
    public enum FusePhase { Lobby, Countdown, Playing, Boom, Over }

    public sealed class FusePlayer
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public string? ConnectionId { get; set; }
        public bool IsBot { get; init; }
        public float X { get; set; }
        public float Y { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
        public int Facing { get; set; } // 0 down, 1 left, 2 right, 3 up
        public bool InMatch { get; set; }
        public bool Alive { get; set; } = true;
        public bool IsJoker { get; set; }
        public bool CanThrow { get; set; }     // the strong arm: granted when the bomb is taken straight from the Joker
        public string? ImmuneFrom { get; set; }
        public double ImmuneUntil { get; set; }
        public double SpeedUntil { get; set; }
        public double SlowUntil { get; set; }
        public double ShieldUntil { get; set; }
        public double NextBotThink { get; set; }
        public float BotDirX { get; set; }
        public float BotDirY { get; set; }
        public string? BotTargetId { get; set; }
        public double BotTargetUntil { get; set; }
        public int Wins { get; set; }
        public int Color { get; set; }
        public int? UserId { get; set; }      // the Stomme account, when signed in
        public int TenantId { get; set; }
        public int SiteId { get; set; }
        public int TotalWins { get; set; }     // the account's wins across all rooms (loaded on join, bumped on a win)
        public int TotalMatches { get; set; }

        public void ClearEffects()
        {
            ImmuneFrom = null;
            SpeedUntil = SlowUntil = ShieldUntil = 0;
            CanThrow = false;
        }
    }

    /// <summary>A box on the floor. Kinds: speed (run faster), slow (run slower), shield (cannot receive the bomb).</summary>
    public sealed class FuseItem
    {
        public required int Id { get; init; }
        public required string Kind { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public double ExpiresAt { get; init; }
    }

    /// <summary>A bomb not in anyone's hands: thrown and flying, or lying where it landed.</summary>
    public sealed class LooseBomb
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public float Range { get; set; }       // distance still to fly; 0 = on the floor
        public string? ThrownBy { get; set; }
    }

    public sealed record FuseMatchResult(int TenantId, int SiteId, int UserId, bool Won);

    public sealed class FuseEvent
    {
        public required string Kind { get; init; } // start, go, pass, boom, over, pickup, throw, joker, blast
        public string? A { get; init; }
        public string? B { get; init; }
    }

    /// <summary>The options a room's owner sets in the lobby.</summary>
    public sealed class FuseOptions
    {
        public int Bombs { get; set; } = 1;        // 2 needs at least 6 players in the round, else it falls back to 1
        public bool Mystery { get; set; }          // boxes all look the same until taken
        public bool Joker { get; set; }            // one player wants to explode
    }

    /// <summary>
    /// One room of Fuse: the players, the bombs, the items, the phase clock. Everything is driven by <see cref="Tick"/>
    /// from the loop; the hub only writes inputs and membership. The caller holds <see cref="Sync"/> around every access.
    /// </summary>
    public sealed class FuseRoom
    {
        public const float Radius = 6f;
        public const float Speed = 92f;
        public const float HolderSpeed = 108f;
        public const float BotSpeed = 84f;
        public const int MinPlayers = 3;
        public const int MaxPlayers = 10;
        public const int TwoBombsMinPlayers = 6;
        public const double PassImmunity = 1.0;
        public const double SpeedDuration = 5.0;
        public const double SlowDuration = 4.0;
        public const double ShieldDuration = 5.0;
        public const double ItemLifetime = 20.0;
        public const int MaxItems = 3;
        public const float ThrowRange = 80f;        // five tiles
        public const float ThrowSpeed = 260f;
        public const float BlastRadius = 44f;       // the Joker's explosion
        private static readonly string[] ItemKinds = { "speed", "speed", "shield", "shield", "slow" };

        public object Sync { get; } = new();
        public string Code { get; }
        public string? OwnerId { get; set; }
        public FuseMap Map { get; private set; } = FuseMap.House;
        public FuseOptions Options { get; } = new();
        public Dictionary<string, FusePlayer> Players { get; } = new();
        public List<FuseItem> Items { get; } = new();
        public List<LooseBomb> Loose { get; } = new();
        public FusePhase Phase { get; private set; } = FusePhase.Lobby;
        public double Now { get; private set; }
        public double PhaseUntil { get; private set; }
        public double FuseEnds { get; private set; }
        public HashSet<string> Holders { get; } = new();
        public string? WinnerId { get; private set; }
        public string? JokerId { get; private set; }
        public int Round { get; private set; }
        public List<FuseEvent> Events { get; } = new();
        public List<FuseMatchResult> Results { get; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        private int _botCounter;
        private int _itemCounter;
        private double _nextItemAt;
        private readonly Dictionary<(int, int), int[]> _fields = new(); // distance fields computed this tick, by source tile
        private readonly Random _random = new(RandomNumberGenerator.GetInt32(int.MaxValue));

        public FuseRoom(string code)
        {
            Code = code;
        }

        public int HumanCount => Players.Values.Count(item => !item.IsBot);

        public string? SetMap(string id)
        {
            if (Phase != FusePhase.Lobby) return "The map can only change in the lobby.";
            Map = FuseMap.Find(id);
            var i = 0;
            foreach (var player in Players.Values)
            {
                var spawn = Map.Spawns[i++ % Map.Spawns.Length];
                player.X = spawn.X;
                player.Y = spawn.Y;
            }
            return null;
        }

        public string? SetOptions(int bombs, bool mystery, bool joker)
        {
            if (Phase != FusePhase.Lobby) return "Options can only change in the lobby.";
            Options.Bombs = Math.Clamp(bombs, 1, 2);
            Options.Mystery = mystery;
            Options.Joker = joker;
            return null;
        }

        public FusePlayer AddPlayer(string id, string name, string? connectionId, bool isBot)
        {
            var player = new FusePlayer
            {
                Id = id,
                Name = name,
                ConnectionId = connectionId,
                IsBot = isBot,
                // joining mid-match: straight into the round (you are never the holder), so a shared link is instant fun
                InMatch = Phase is FusePhase.Countdown or FusePhase.Playing or FusePhase.Boom,
                Alive = true,
                Color = NextColor()
            };
            var spawn = Map.Spawns[Players.Count % Map.Spawns.Length];
            player.X = spawn.X;
            player.Y = spawn.Y;
            Players[id] = player;
            if (OwnerId == null && !isBot) OwnerId = id;
            LastActivity = DateTime.UtcNow;
            return player;
        }

        public FusePlayer AddBot()
        {
            _botCounter++;
            return AddPlayer("bot-" + _botCounter, "Bot " + _botCounter, null, true);
        }

        public void RemovePlayer(string id)
        {
            if (!Players.Remove(id)) return;
            if (OwnerId == id)
            {
                OwnerId = Players.Values.FirstOrDefault(item => !item.IsBot)?.Id;
            }
            if (Holders.Remove(id) && Phase is FusePhase.Countdown or FusePhase.Playing)
            {
                // the holder walked out: the bomb goes to a random player still in the round
                var candidates = InRound().Where(item => !Holders.Contains(item.Id)).ToList();
                if (candidates.Count > 0) Holders.Add(candidates[_random.Next(candidates.Count)].Id);
            }
            if (JokerId == id) JokerId = null;
            if (Phase is FusePhase.Countdown or FusePhase.Playing && InRound().Count() <= 1)
            {
                EndMatch();
            }
            LastActivity = DateTime.UtcNow;
        }

        public bool RemoveLastBot()
        {
            var bot = Players.Values.LastOrDefault(item => item.IsBot);
            if (bot == null) return false;
            RemovePlayer(bot.Id);
            return true;
        }

        public string? Start()
        {
            if (Phase != FusePhase.Lobby) return "A match is already running.";
            while (Players.Count < MinPlayers)
            {
                AddBot();
            }
            Round = 0;
            WinnerId = null;
            JokerId = null;
            foreach (var player in Players.Values)
            {
                player.InMatch = true;
                player.Alive = true;
                player.IsJoker = false;
            }
            if (Options.Joker)
            {
                var all = Players.Values.ToList();
                var joker = all[_random.Next(all.Count)];
                joker.IsJoker = true;
                JokerId = joker.Id;
                Events.Add(new FuseEvent { Kind = "joker", A = joker.Id });
            }
            BeginRound();
            Events.Add(new FuseEvent { Kind = "start" });
            return null;
        }

        /// <summary>The strong arm: the holder throws the bomb the way they face. Only a player who took it from the Joker can.</summary>
        public string? Throw(string playerId)
        {
            if (Phase != FusePhase.Playing) return null;
            if (!Players.TryGetValue(playerId, out var player) || !Holders.Contains(playerId)) return "You do not hold the bomb.";
            if (!player.CanThrow) return "Only a bomb taken from the Joker can be thrown.";
            Holders.Remove(playerId);
            player.CanThrow = false;
            var (dx, dy) = player.Facing switch { 0 => (0f, 1f), 1 => (-1f, 0f), 2 => (1f, 0f), _ => (0f, -1f) };
            Loose.Add(new LooseBomb { X = player.X, Y = player.Y, VX = dx * ThrowSpeed, VY = dy * ThrowSpeed, Range = ThrowRange, ThrownBy = playerId });
            Events.Add(new FuseEvent { Kind = "throw", A = playerId });
            return null;
        }

        private IEnumerable<FusePlayer> InRound() => Players.Values.Where(item => item.InMatch && item.Alive);

        private void BeginRound()
        {
            Round++;
            Items.Clear();
            Loose.Clear();
            Holders.Clear();
            var inRound = InRound().ToList();
            var spawns = Map.Spawns.OrderBy(_ => _random.Next()).ToList();
            for (var i = 0; i < inRound.Count; i++)
            {
                inRound[i].X = spawns[i % spawns.Count].X;
                inRound[i].Y = spawns[i % spawns.Count].Y;
                inRound[i].InputX = inRound[i].InputY = 0;
                inRound[i].ClearEffects();
                inRound[i].BotTargetId = null;
            }
            var bombs = Options.Bombs == 2 && inRound.Count >= TwoBombsMinPlayers ? 2 : 1;
            foreach (var holder in inRound.OrderBy(_ => _random.Next()).Take(bombs))
            {
                Holders.Add(holder.Id);
            }
            Phase = FusePhase.Countdown;
            PhaseUntil = Now + 3.0;
        }

        private void EndMatch(string? winnerId = null)
        {
            WinnerId = winnerId ?? InRound().FirstOrDefault()?.Id;
            if (WinnerId != null && Players.TryGetValue(WinnerId, out var winner)) winner.Wins++;
            foreach (var player in Players.Values.Where(item => item.InMatch && item.UserId != null))
            {
                var won = player.Id == WinnerId;
                player.TotalMatches++;
                if (won) player.TotalWins++;
                Results.Add(new FuseMatchResult(player.TenantId, player.SiteId, player.UserId!.Value, won));
            }
            Phase = FusePhase.Over;
            PhaseUntil = Now + 6.0;
            Holders.Clear();
            Loose.Clear();
            Items.Clear();
            Events.Add(new FuseEvent { Kind = "over", A = WinnerId });
        }

        public void Tick(double now, float dt)
        {
            Now = now;
            switch (Phase)
            {
                case FusePhase.Lobby:
                    MovePlayers(dt, false);
                    break;
                case FusePhase.Countdown:
                    if (Now >= PhaseUntil)
                    {
                        Phase = FusePhase.Playing;
                        // the hidden fuse: the music stops somewhere between 15 and 40 seconds from now
                        FuseEnds = Now + 15.0 + _random.NextDouble() * 25.0;
                        _nextItemAt = Now + 3.0 + _random.NextDouble() * 3.0;
                        Events.Add(new FuseEvent { Kind = "go" });
                    }
                    break;
                case FusePhase.Playing:
                    _fields.Clear();
                    MovePlayers(dt, true);
                    MoveLooseBombs(dt);
                    SpawnItems();
                    PickUpItems();
                    PassBombs();
                    if (Now >= FuseEnds) Boom();
                    break;
                case FusePhase.Boom:
                    if (Now >= PhaseUntil)
                    {
                        if (InRound().Count() <= 1) EndMatch();
                        else BeginRound();
                    }
                    break;
                case FusePhase.Over:
                    if (Now >= PhaseUntil)
                    {
                        Phase = FusePhase.Lobby;
                        JokerId = null;
                        foreach (var player in Players.Values)
                        {
                            player.InMatch = false;
                            player.Alive = true;
                            player.IsJoker = false;
                            player.ClearEffects();
                        }
                    }
                    break;
            }
        }

        private void Boom()
        {
            Phase = FusePhase.Boom;
            PhaseUntil = Now + 2.5;
            // a bomb still in the air or on the floor goes off where it is: the nearest player within the blast is out
            foreach (var loose in Loose)
            {
                var victim = InRound().OrderBy(item => Dist2(item.X, item.Y, loose.X, loose.Y)).FirstOrDefault();
                if (victim != null && Dist2(victim.X, victim.Y, loose.X, loose.Y) <= BlastRadius * BlastRadius)
                {
                    victim.Alive = false;
                    Events.Add(new FuseEvent { Kind = "boom", A = victim.Id });
                }
            }
            Loose.Clear();
            foreach (var holderId in Holders.ToList())
            {
                if (!Players.TryGetValue(holderId, out var victim)) continue;
                if (victim.IsJoker)
                {
                    // the Joker's blast takes everyone nearby with him; if it takes anyone at all, the Joker wins
                    var caught = InRound().Where(item => item.Id != victim.Id && Dist2(item.X, item.Y, victim.X, victim.Y) <= BlastRadius * BlastRadius).ToList();
                    victim.Alive = false;
                    foreach (var other in caught) other.Alive = false;
                    Events.Add(new FuseEvent { Kind = "blast", A = victim.Id, B = caught.Count.ToString() });
                    if (caught.Count > 0)
                    {
                        EndMatch(victim.Id);
                        return;
                    }
                }
                else
                {
                    victim.Alive = false;
                    Events.Add(new FuseEvent { Kind = "boom", A = victim.Id });
                }
            }
            Holders.Clear();
            // the last non-Joker standing has beaten the Joker: the match is theirs
            if (JokerId != null && Players.TryGetValue(JokerId, out var joker) && joker.Alive)
            {
                var others = InRound().Where(item => !item.IsJoker).ToList();
                if (others.Count == 1) EndMatch(others[0].Id);
            }
        }

        private static float Dist2(float ax, float ay, float bx, float by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

        private void MoveLooseBombs(float dt)
        {
            for (var i = Loose.Count - 1; i >= 0; i--)
            {
                var bomb = Loose[i];
                if (bomb.Range > 0)
                {
                    var step = Math.Min(bomb.Range, ThrowSpeed * dt);
                    var nx = bomb.X + bomb.VX / ThrowSpeed * step;
                    var ny = bomb.Y + bomb.VY / ThrowSpeed * step;
                    if (Map.Blocked(nx, ny, 3f)) { bomb.Range = 0; }
                    else { bomb.X = nx; bomb.Y = ny; bomb.Range -= step; }
                }
                // whoever it reaches takes it: in flight anyone but the thrower, on the floor anyone at all
                var catcher = InRound()
                    .Where(item => !Holders.Contains(item.Id) && (bomb.Range <= 0 || item.Id != bomb.ThrownBy))
                    .Where(item => Dist2(item.X, item.Y, bomb.X, bomb.Y) <= (Radius + 5) * (Radius + 5))
                    .OrderBy(item => Dist2(item.X, item.Y, bomb.X, bomb.Y))
                    .FirstOrDefault();
                if (catcher != null)
                {
                    Holders.Add(catcher.Id);
                    catcher.CanThrow = false;
                    Loose.RemoveAt(i);
                    Events.Add(new FuseEvent { Kind = "pass", A = bomb.ThrownBy, B = catcher.Id });
                }
            }
        }

        private void SpawnItems()
        {
            Items.RemoveAll(item => Now >= item.ExpiresAt);
            if (Now < _nextItemAt || Items.Count >= MaxItems) return;
            _nextItemAt = Now + 6.0 + _random.NextDouble() * 4.0;
            var spot = Map.RandomItemSpot(_random);
            if (Items.Any(item => MathF.Abs(item.X - spot.X) < 24 && MathF.Abs(item.Y - spot.Y) < 24)) return;
            Items.Add(new FuseItem
            {
                Id = ++_itemCounter,
                Kind = ItemKinds[_random.Next(ItemKinds.Length)],
                X = spot.X,
                Y = spot.Y,
                ExpiresAt = Now + ItemLifetime
            });
        }

        private void PickUpItems()
        {
            if (Items.Count == 0) return;
            foreach (var player in InRound())
            {
                for (var i = Items.Count - 1; i >= 0; i--)
                {
                    var item = Items[i];
                    var reach = Radius + 7;
                    if (Dist2(item.X, item.Y, player.X, player.Y) > reach * reach) continue;
                    Items.RemoveAt(i);
                    switch (item.Kind)
                    {
                        case "speed": player.SpeedUntil = Now + SpeedDuration; player.SlowUntil = 0; break;
                        case "slow": player.SlowUntil = Now + SlowDuration; player.SpeedUntil = 0; break;
                        case "shield": player.ShieldUntil = Now + ShieldDuration; break;
                    }
                    Events.Add(new FuseEvent { Kind = "pickup", A = player.Id, B = item.Kind });
                }
            }
        }

        public float SpeedOf(FusePlayer player)
        {
            var speed = player.IsBot ? BotSpeed : Speed;
            if (Holders.Contains(player.Id)) speed = player.IsBot ? BotSpeed * 1.2f : HolderSpeed;
            if (Now < player.SpeedUntil) speed *= 1.6f;
            if (Now < player.SlowUntil) speed *= 0.55f;
            return speed;
        }

        private void MovePlayers(float dt, bool inRound)
        {
            foreach (var player in Players.Values)
            {
                if (inRound && !(player.InMatch && player.Alive)) continue;
                if (player.IsBot)
                {
                    if (inRound) ThinkBot(player);
                    else { player.InputX = player.InputY = 0; }
                }
                var ix = player.InputX;
                var iy = player.InputY;
                var len = MathF.Sqrt(ix * ix + iy * iy);
                if (len < 0.05f) continue;
                if (len > 1f) { ix /= len; iy /= len; }
                var speed = SpeedOf(player);
                var nx = player.X + ix * speed * dt;
                var ny = player.Y + iy * speed * dt;
                if (!Map.Blocked(nx, player.Y, Radius)) player.X = nx;
                if (!Map.Blocked(player.X, ny, Radius)) player.Y = ny;
                player.Facing = MathF.Abs(ix) > MathF.Abs(iy) ? (ix > 0 ? 2 : 1) : (iy > 0 ? 0 : 3);
            }
        }

        private bool CanReceive(FusePlayer other, FusePlayer holder)
        {
            if (other.Id == holder.Id) return false;
            if (Holders.Contains(other.Id)) return false;
            if (other.ImmuneFrom == holder.Id && Now < other.ImmuneUntil) return false;
            if (Now < other.ShieldUntil) return false;
            return true;
        }

        private void PassBombs()
        {
            foreach (var holderId in Holders.ToList())
            {
                if (!Players.TryGetValue(holderId, out var holder)) continue;
                foreach (var other in InRound())
                {
                    if (!CanReceive(other, holder)) continue;
                    var reach = Radius * 2 + 2;
                    if (Dist2(other.X, other.Y, holder.X, holder.Y) <= reach * reach)
                    {
                        Holders.Remove(holderId);
                        Holders.Add(other.Id);
                        holder.ImmuneFrom = other.Id;
                        holder.ImmuneUntil = Now + PassImmunity;
                        holder.CanThrow = false;
                        other.CanThrow = holder.IsJoker;   // the strong arm comes with a bomb taken from the Joker
                        Events.Add(new FuseEvent { Kind = "pass", A = holder.Id, B = other.Id });
                        break;
                    }
                }
            }
        }

        private FusePlayer? NearestHolder(FusePlayer bot)
        {
            FusePlayer? best = null;
            var bestD = float.MaxValue;
            foreach (var id in Holders)
            {
                if (!Players.TryGetValue(id, out var holder) || holder.Id == bot.Id) continue;
                var d = Dist2(holder.X, holder.Y, bot.X, bot.Y);
                if (d < bestD) { bestD = d; best = holder; }
            }
            return best;
        }

        private void ThinkBot(FusePlayer bot)
        {
            if (Now < bot.NextBotThink)
            {
                bot.InputX = bot.BotDirX;
                bot.InputY = bot.BotDirY;
                return;
            }
            bot.NextBotThink = Now + 0.12 + _random.NextDouble() * 0.1;
            float wantX = 0, wantY = 0;
            if (Holders.Contains(bot.Id))
            {
                var target = PickChaseTarget(bot);
                if (target != null)
                {
                    var fromMe = FieldFrom(bot.X, bot.Y);
                    if (FuseMap.DistanceAt(fromMe, target.X, target.Y) <= 1) { wantX = target.X - bot.X; wantY = target.Y - bot.Y; }
                    else (wantX, wantY) = StepTowards(FieldFrom(target.X, target.Y), bot, descend: true);
                }
                // a bomb from the Joker: fling it back at him when he is in front and near
                if (bot.CanThrow && JokerId != null && Players.TryGetValue(JokerId, out var joker) && joker.Alive)
                {
                    var (fx, fy) = bot.Facing switch { 0 => (0f, 1f), 1 => (-1f, 0f), 2 => (1f, 0f), _ => (0f, -1f) };
                    var dx = joker.X - bot.X; var dy = joker.Y - bot.Y;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > 0 && dist < ThrowRange && (dx * fx + dy * fy) / dist > 0.9f) Throw(bot.Id);
                }
            }
            else
            {
                var holder = NearestHolder(bot);
                if (holder == null)
                {
                    // bombs are loose: the Joker wants one, everyone else keeps clear of them
                    var loose = Loose.OrderBy(item => Dist2(item.X, item.Y, bot.X, bot.Y)).FirstOrDefault();
                    if (loose != null)
                    {
                        (wantX, wantY) = StepTowards(FieldFrom(loose.X, loose.Y), bot, descend: bot.IsJoker);
                    }
                }
                else if (bot.IsJoker)
                {
                    // the Joker wants the bomb: walk to its holder
                    (wantX, wantY) = StepTowards(FieldFrom(holder.X, holder.Y), bot, descend: true);
                }
                else
                {
                    // flee: step onto the neighbouring tile farthest (by walking distance) from the nearest holder
                    var fromHolder = FieldFrom(holder.X, holder.Y);
                    var myDist = FuseMap.DistanceAt(fromHolder, bot.X, bot.Y);
                    if (myDist < 9)
                    {
                        (wantX, wantY) = StepTowards(fromHolder, bot, descend: false);
                    }
                    else
                    {
                        // far enough: wander, with a pull towards a good box nearby
                        wantX = (float)(_random.NextDouble() * 2 - 1);
                        wantY = (float)(_random.NextDouble() * 2 - 1);
                        var box = Items.Where(item => item.Kind != "slow" || Options.Mystery)
                            .OrderBy(item => Dist2(item.X, item.Y, bot.X, bot.Y))
                            .FirstOrDefault();
                        if (box != null && MathF.Abs(box.X - bot.X) + MathF.Abs(box.Y - bot.Y) < 160)
                        {
                            (wantX, wantY) = StepTowards(FieldFrom(box.X, box.Y), bot, descend: true);
                            wantX *= 2; wantY *= 2;
                        }
                    }
                }
            }
            var wl = MathF.Sqrt(wantX * wantX + wantY * wantY);
            if (wl < 0.001f) { bot.BotDirX = bot.BotDirY = 0; bot.InputX = bot.InputY = 0; return; }
            wantX /= wl; wantY /= wl;

            // pick the free direction closest to the wanted one (16 candidates, two look-ahead distances)
            float bestScore = float.MinValue, bx = 0, by = 0;
            for (var i = 0; i < 16; i++)
            {
                var a = i * MathF.PI * 2 / 16;
                var cx = MathF.Cos(a);
                var cy = MathF.Sin(a);
                var score = cx * wantX + cy * wantY + (float)(_random.NextDouble() * 0.15);
                if (Map.Blocked(bot.X + cx * 22, bot.Y + cy * 22, Radius)) score -= 2f;
                if (Map.Blocked(bot.X + cx * 10, bot.Y + cy * 10, Radius)) score -= 2f;
                if (score > bestScore) { bestScore = score; bx = cx; by = cy; }
            }
            bot.BotDirX = bx;
            bot.BotDirY = by;
            bot.InputX = bx;
            bot.InputY = by;
        }

        /// <summary>
        /// Who a bot with the bomb hunts: humans before bots, near before far (by walking distance), and never the same
        /// target for more than a few seconds, so nobody is safe just because they stay in another room.
        /// </summary>
        private FusePlayer? PickChaseTarget(FusePlayer bot)
        {
            var current = bot.BotTargetId != null && Players.TryGetValue(bot.BotTargetId, out var c) && c.InMatch && c.Alive && CanReceive(c, bot) ? c : null;
            if (current != null && Now < bot.BotTargetUntil) return current;

            var fromMe = FieldFrom(bot.X, bot.Y);
            var candidates = InRound().Where(item => CanReceive(item, bot)).ToList();
            FusePlayer? best = null;
            var bestScore = double.MaxValue;
            foreach (var other in candidates)
            {
                if (current != null && other.Id == current.Id && candidates.Count > 1) continue; // time to switch
                var d = FuseMap.DistanceAt(fromMe, other.X, other.Y);
                if (d == int.MaxValue) continue;
                var score = d * (other.IsBot ? 1.0 : 0.5) + _random.NextDouble() * 3;
                if (score < bestScore) { bestScore = score; best = other; }
            }
            bot.BotTargetId = best?.Id;
            bot.BotTargetUntil = Now + 4.0 + _random.NextDouble() * 3.0;
            return best;
        }

        private int[] FieldFrom(float x, float y)
        {
            var key = ((int)(x / FuseMap.Tile), (int)(y / FuseMap.Tile));
            if (!_fields.TryGetValue(key, out var field))
            {
                field = Map.DistanceField(x, y);
                _fields[key] = field;
            }
            return field;
        }

        /// <summary>
        /// The direction from the bot to the centre of the best neighbouring tile: the lowest distance when descending
        /// (chasing the field's source), the highest when not (fleeing it). Staying put is a candidate too, so a bot in
        /// a dead end does not jitter. When fleeing, one tile of look-ahead keeps a bot out of pockets.
        /// </summary>
        private (float X, float Y) StepTowards(int[] field, FusePlayer bot, bool descend)
        {
            var tx = (int)(bot.X / FuseMap.Tile);
            var ty = (int)(bot.Y / FuseMap.Tile);
            var bestScore = descend ? int.MaxValue : int.MinValue;
            var bestX = tx; var bestY = ty;
            foreach (var (dx, dy) in FuseMap.Steps.Append((0, 0)))
            {
                var nx = tx + dx; var ny = ty + dy;
                if (Map.IsWall(nx, ny)) continue;
                var d = field[ny * FuseMap.Width + nx];
                if (d == int.MaxValue) continue;
                if (!descend)
                {
                    var ahead = d;
                    foreach (var (ex, ey) in FuseMap.Steps)
                    {
                        var ax = nx + ex; var ay = ny + ey;
                        if (!Map.IsWall(ax, ay) && field[ay * FuseMap.Width + ax] != int.MaxValue) ahead = Math.Max(ahead, field[ay * FuseMap.Width + ax]);
                    }
                    d = d * 2 + ahead;
                }
                var better = descend ? d < bestScore : d > bestScore;
                if (better || (d == bestScore && _random.Next(2) == 0)) { bestScore = d; bestX = nx; bestY = ny; }
            }
            var cx = bestX * FuseMap.Tile + FuseMap.Tile / 2f;
            var cy = bestY * FuseMap.Tile + FuseMap.Tile / 2f;
            return (cx - bot.X, cy - bot.Y);
        }

        private int NextColor()
        {
            var used = Players.Values.Select(item => item.Color).ToHashSet();
            for (var i = 0; i < 10; i++)
            {
                if (!used.Contains(i)) return i;
            }
            return _random.Next(10);
        }

        private float Remaining(double until) => until > Now ? MathF.Round((float)(until - Now), 1) : 0f;

        public object Snapshot()
        {
            return new
            {
                code = Code,
                map = Map.Id,
                phase = Phase.ToString().ToLowerInvariant(),
                round = Round,
                owner = OwnerId,
                bombs = Holders.ToArray(),
                loose = Loose.Select(item => new { x = MathF.Round(item.X, 1), y = MathF.Round(item.Y, 1), air = item.Range > 0 }).ToArray(),
                joker = JokerId,
                winner = WinnerId,
                options = new { bombs = Options.Bombs, mystery = Options.Mystery, joker = Options.Joker },
                countdown = Phase == FusePhase.Countdown ? Math.Max(0, PhaseUntil - Now) : 0,
                players = Players.Values.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    bot = item.IsBot,
                    x = MathF.Round(item.X, 1),
                    y = MathF.Round(item.Y, 1),
                    f = item.Facing,
                    alive = item.Alive,
                    inMatch = item.InMatch,
                    wins = item.Wins,
                    tw = item.UserId != null ? item.TotalWins : -1,
                    color = item.Color,
                    sp = Remaining(item.SpeedUntil),
                    sl = Remaining(item.SlowUntil),
                    sh = Remaining(item.ShieldUntil),
                    arm = item.CanThrow,
                    speed = MathF.Round(SpeedOf(item), 1)
                }).ToArray(),
                items = Items.Select(item => new { id = item.Id, kind = Options.Mystery ? "mystery" : item.Kind, x = item.X, y = item.Y }).ToArray(),
                events = Events.Select(item => new { kind = item.Kind, a = item.A, b = item.B }).ToArray()
            };
        }
    }
}
