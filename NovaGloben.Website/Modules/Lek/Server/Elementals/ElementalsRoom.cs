using System.Security.Cryptography;

namespace Lek.Server.Elementals
{
    public enum ElPhase { Lobby, Countdown, Playing, RoundEnd, Over }

    public sealed class ElPlayer
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public bool IsBot { get; init; }
        public bool AutoBot { get; init; }
        public KindDef Kind { get; set; } = KindDef.Cactus;
        public int Team { get; set; }
        public Body Body { get; } = new();
        public int Hp { get; set; }
        public bool Alive { get; set; }
        public bool InMatch { get; set; }
        public int Weapon { get; set; }
        public int Facing { get; set; } = 1;
        public double Aim { get; set; }
        public int Bits { get; set; }
        public int LastSeq { get; set; }
        public Queue<(int Seq, int Bits, double Aim)> Inputs { get; } = new();
        public int? WantAbility { get; set; }
        public int? WantWeapon { get; set; }

        public double AttackCd { get; set; }
        public double[] AbilityCd { get; } = new double[3];
        public double SwingAt { get; set; } = -10;
        public double SlowUntil, SpeedUntil, PowerUntil, HasteUntil, RegenUntil, NextRegen, BurnUntil, NextBurn;
        public double HazardReady, FrozenUntil, StunUntil, CastUntil, PadTick;
        public int PendingAbility { get; set; } = -1;
        public string? PendingId { get; set; }
        public string[] Loadout { get; set; } = KindDef.Cactus.DefaultLoadout;
        public string[] Arms { get; set; } = KindDef.Cactus.DefaultArms;
        public WeaponDef CurrentWeapon => Kind.Weapon(Arms[Weapon]) ?? Kind.Weapons[0];
        public double MarkX, MarkY, MarkUntil;
        public double ShieldUntil, ArmorUntil, ThornUntil, NextThornSkin, ThirstUntil, FireDashUntil;
        public int ArmorLeft { get; set; }
        public int HitCount { get; set; }
        public HashSet<string> DashHit { get; } = new();
        public bool Has(string ability) => Loadout.Contains(ability);

        public int Kills { get; set; }
        public int Damage { get; set; }

        public double NextThink { get; set; }
        public int BotJumpTicks { get; set; }
        public double LastX { get; set; }

        public int? UserId { get; set; }
        public int TenantId { get; set; }
        public int SiteId { get; set; }
        public int TotalWins { get; set; }
    }

    public sealed class ElShot
    {
        public int Id { get; init; }
        public required string Kind { get; init; } // cactusbomb, spine, shard, fireball
        public required string Owner { get; init; }
        public int Team { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double VX { get; set; }
        public double VY { get; set; }
        public double Gravity { get; init; }
        public int Damage { get; init; }
        public double Knock { get; init; }
        public double Dies { get; init; }
    }

    /// <summary>Things in the arena: pots, planted cacti, fire pillars, and marked strikes waiting to land.</summary>
    public sealed class ElThing
    {
        public int Id { get; init; }
        public required string Kind { get; init; } // pot, plant, pillar, meteor, judgement
        public string? Owner { get; init; }
        public int Team { get; init; } = -1;
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; init; }
        public double H { get; init; }
        public double Start { get; init; }
        public double Until { get; set; }
        public double NextTick { get; set; }
        public bool Broken { get; set; }
        public Body? Body { get; init; }
        public int Bits { get; init; }
        public int Facing { get; init; } = 1;
    }

    public sealed class ElEvent
    {
        public required string K { get; init; }
        public string? A { get; init; }
        public string? B { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
        public double V { get; init; }
        public string? S { get; init; }
    }

    public sealed record ElMatchResult(int TenantId, int SiteId, int UserId, bool Won);

    /// <summary>
    /// One Elementals room: lobby, then a best-of-three match of 1:30 rounds between two teams of 1 to 3, run at a
    /// fixed 30 Hz by <see cref="ElementalsLoop"/>. Movement comes from <see cref="ElementalsPhysics"/>, combat,
    /// hazards and abilities live here. All numbers are in pixels, seconds and half hearts.
    /// </summary>
    public sealed class ElementalsRoom
    {
        public const int MaxPlayers = 6;
        public const double RoundSeconds = 90;
        public const int DrawMargin = 8; // more than 4 hearts ahead at the bell wins
        private const double T = ElementalsMap.Tile;
        private const double Dt = ElementalsPhysics.Dt;
        private const double IcicleSpeed = 420;
        private static readonly string[] PadEffects = { "heal", "speed", "power", "haste" };
        private static readonly string[] PotEffects = { "speed", "regen", "power", "haste", "heal" };

        public object Sync { get; } = new();
        public string Code { get; }
        public string? OwnerId { get; set; }
        public int TeamSize { get; private set; } = 1;
        public string MapChoice { get; private set; } = "random";
        public ElementalsMap Map { get; private set; } = ElementalsMap.Desert;
        public Dictionary<string, ElPlayer> Players { get; } = new();
        public List<ElShot> Shots { get; } = new();
        public List<ElThing> Things { get; } = new();
        public ElPhase Phase { get; private set; } = ElPhase.Lobby;
        public double Now { get; private set; }
        public double PhaseUntil { get; private set; }
        public double RoundEnds { get; private set; }
        public double RoundStarted { get; private set; }
        public int Round { get; private set; }
        public int[] Wins { get; } = new int[2];
        public int Winner { get; private set; } = -1;
        public List<ElEvent> Events { get; } = new();
        public List<ElMatchResult> Results { get; } = new();
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public long Tick { get; private set; }
        public long LastSentTick { get; set; } = -100;

        private readonly char[] _dyn = new char[ElementalsMap.Cols * ElementalsMap.Height];
        private readonly Dictionary<int, double> _dynUntil = new();
        private readonly Dictionary<int, int> _dynTeam = new();
        private readonly Func<int, int, char> _cell;
        private readonly Random _random = new(RandomNumberGenerator.GetInt32(int.MaxValue));
        private int _nextId = 1;
        private int _botCounter;

        public ElementalsRoom(string code)
        {
            Code = code;
            _cell = Cell;
        }

        public int HumanCount => Players.Values.Count(item => !item.IsBot);

        // ------------------------------------------------------------------ cells
        public char Cell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= ElementalsMap.Cols || y >= ElementalsMap.Height) return '#';
            var i = y * ElementalsMap.Cols + x;
            var d = _dyn[i];
            var b = Map.Cells[i];
            if (d == 'i') return ElementalsPhysics.IsSolid(b) ? '~' : b;
            return d != '\0' ? d : b;
        }

        private bool SetDyn(int x, int y, char kind, double seconds, int team)
        {
            if (x < 1 || y < 1 || x >= ElementalsMap.Cols - 1 || y >= ElementalsMap.Height - 1) return false;
            var i = y * ElementalsMap.Cols + x;
            _dyn[i] = kind;
            _dynUntil[i] = Now + seconds;
            _dynTeam[i] = team;
            return true;
        }

        private bool CellBlockedByPlayer(int x, int y)
        {
            foreach (var p in Players.Values)
            {
                if (!p.InMatch || !p.Alive) continue;
                var b = p.Body;
                if (b.X + b.W / 2 > x * T && b.X - b.W / 2 < (x + 1) * T && b.Y > y * T && b.Y - b.H < (y + 1) * T) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ lobby
        public ElPlayer AddPlayer(string id, string name, bool isBot, bool autoBot = false, int? team = null)
        {
            var player = new ElPlayer
            {
                Id = id,
                Name = name,
                IsBot = isBot,
                AutoBot = autoBot,
                Kind = KindDef.All[_random.Next(KindDef.All.Length)]
            };
            player.Loadout = isBot ? player.Kind.Pool.OrderBy(_ => _random.Next()).Take(3).Select(a => a.Id).ToArray() : player.Kind.DefaultLoadout;
            player.Arms = player.Kind.DefaultArms;
            player.Team = team ?? (Players.Values.Count(p => p.Team == 0) <= Players.Values.Count(p => p.Team == 1) ? 0 : 1);
            Players[id] = player;
            if (OwnerId == null && !isBot) OwnerId = id;
            LastActivity = DateTime.UtcNow;
            return player;
        }

        public void RemovePlayer(string id)
        {
            if (!Players.Remove(id)) return;
            if (OwnerId == id) OwnerId = Players.Values.FirstOrDefault(item => !item.IsBot)?.Id;
            if (Phase != ElPhase.Lobby)
            {
                if (HumanCount == 0) BackToLobby();
                else if (Players.Values.Where(p => p.InMatch).Select(p => p.Team).Distinct().Count() < 2) EndMatch();
            }
            LastActivity = DateTime.UtcNow;
        }

        public string? SetKind(string playerId, string kindId)
        {
            if (!Players.TryGetValue(playerId, out var player)) return "You are not in the room.";
            if (Phase != ElPhase.Lobby) return "Characters are picked in the lobby.";
            player.Kind = KindDef.Find(kindId);
            player.Loadout = player.Kind.DefaultLoadout;
            player.Arms = player.Kind.DefaultArms;
            return null;
        }

        public string? SetArms(string playerId, string[]? ids)
        {
            if (!Players.TryGetValue(playerId, out var player)) return "You are not in the room.";
            if (Phase != ElPhase.Lobby) return "Weapons are picked in the lobby.";
            if (ids == null || ids.Length != 2 || ids[0] == ids[1] || ids.Any(id => player.Kind.Weapon(id) == null)) return "Pick two different weapons.";
            player.Arms = ids;
            return null;
        }

        public string? SetLoadout(string playerId, string[]? ids)
        {
            if (!Players.TryGetValue(playerId, out var player)) return "You are not in the room.";
            if (Phase != ElPhase.Lobby) return "Abilities are picked in the lobby.";
            if (ids == null || ids.Length != 3 || ids.Distinct().Count() != 3 || ids.Any(id => player.Kind.Ability(id) == null)) return "Pick three different abilities of your hero.";
            player.Loadout = ids;
            return null;
        }

        public string? SetTeam(string playerId, int team)
        {
            if (!Players.TryGetValue(playerId, out var player)) return "You are not in the room.";
            if (Phase != ElPhase.Lobby) return "Teams are picked in the lobby.";
            player.Team = team == 1 ? 1 : 0;
            return null;
        }

        public string? SetMode(int size)
        {
            if (Phase != ElPhase.Lobby) return "The match is running.";
            TeamSize = Math.Clamp(size, 1, 3);
            return null;
        }

        public string? SetMap(string id)
        {
            if (Phase != ElPhase.Lobby) return "The match is running.";
            MapChoice = id == "random" || ElementalsMap.Find(id) == null ? "random" : id;
            return null;
        }

        public string? Start()
        {
            if (Phase != ElPhase.Lobby) return "A match is already running.";
            var humans = Players.Values.Where(p => !p.IsBot).ToList();
            if (humans.Count > TeamSize * 2) return $"Too many players for {TeamSize}v{TeamSize}: pick a bigger mode.";
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            // move extras to the other side when one side is over the team size
            for (var team = 0; team < 2; team++)
            {
                var side = humans.Where(p => p.Team == team).ToList();
                while (side.Count > TeamSize)
                {
                    var moved = side[^1];
                    moved.Team = 1 - team;
                    side.RemoveAt(side.Count - 1);
                }
            }
            for (var team = 0; team < 2; team++)
            {
                while (Players.Values.Count(p => p.Team == team) < TeamSize)
                {
                    _botCounter++;
                    AddPlayer("bot-" + _botCounter, BotName(), true, true, team);
                }
            }
            Map = MapChoice == "random" ? ElementalsMap.All[_random.Next(ElementalsMap.All.Length)] : ElementalsMap.Find(MapChoice) ?? ElementalsMap.Desert;
            Round = 0;
            Wins[0] = Wins[1] = 0;
            Winner = -1;
            foreach (var p in Players.Values) { p.InMatch = true; p.Kills = 0; p.Damage = 0; }
            StartRound();
            return null;
        }

        private string BotName()
        {
            string[] names = { "Rustam", "Sohrab", "Arash", "Gordafarid", "Kaveh", "Tahmineh", "Babak", "Simin", "Zal", "Roudabeh" };
            var used = Players.Values.Select(p => p.Name).ToHashSet();
            var free = names.Where(n => !used.Contains(n + " (bot)")).ToArray();
            return (free.Length > 0 ? free[_random.Next(free.Length)] : "Bot " + _botCounter) + " (bot)";
        }

        private void StartRound()
        {
            Round++;
            Shots.Clear();
            Things.Clear();
            Array.Clear(_dyn);
            _dynUntil.Clear();
            _dynTeam.Clear();
            foreach (var pot in Map.Pots) Things.Add(new ElThing { Id = _nextId++, Kind = "pot", X = pot.X, Y = pot.Y, W = 10, H = 11 });
            var index = new int[2];
            foreach (var p in Players.Values.Where(p => p.InMatch).OrderBy(p => p.IsBot))
            {
                var spawns = Map.Spawns[p.Team];
                var spot = spawns[index[p.Team]++ % spawns.Count];
                var b = p.Body;
                b.X = spot.X; b.Y = spot.Y; b.VX = b.VY = 0;
                b.W = p.Kind.W; b.H = p.Kind.H;
                b.Grounded = false; b.Ground = '.'; b.Coyote = 0; b.Jumping = false; b.PrevJump = false;
                b.DashT = 0; b.DashVX = 0; b.LockT = 0; b.Speed = 1; b.LandV = 0;
                p.Hp = p.Kind.MaxHp;
                p.Alive = true;
                p.Facing = p.Team == 0 ? 1 : -1;
                p.Aim = p.Team == 0 ? 0 : Math.PI;
                p.Weapon = 0;
                p.AttackCd = 0;
                Array.Clear(p.AbilityCd);
                p.SlowUntil = p.SpeedUntil = p.PowerUntil = p.HasteUntil = p.RegenUntil = p.BurnUntil = 0;
                p.FrozenUntil = p.StunUntil = p.CastUntil = 0;
                p.PendingAbility = -1;
                p.WantAbility = null;
                p.ShieldUntil = p.ArmorUntil = p.ThornUntil = p.ThirstUntil = p.FireDashUntil = 0;
                p.ArmorLeft = 0;
                p.HitCount = 0;
                p.NextThornSkin = Now + 3 + 5;
                b.FlyT = 0;
                p.MarkUntil = 0;
            }
            Phase = ElPhase.Countdown;
            PhaseUntil = Now + 3;
            Events.Add(new ElEvent { K = "round", V = Round });
        }

        private void EndRound(int winner, string reason)
        {
            if (winner >= 0) Wins[winner]++;
            Phase = ElPhase.RoundEnd;
            PhaseUntil = Now + 4;
            Events.Add(new ElEvent { K = "roundend", V = winner, S = reason });
        }

        private void EndMatch()
        {
            Winner = Wins[0] == Wins[1] ? -1 : (Wins[0] > Wins[1] ? 0 : 1);
            Phase = ElPhase.Over;
            PhaseUntil = Now + 8;
            Events.Add(new ElEvent { K = "over", V = Winner });
            foreach (var p in Players.Values.Where(p => p.InMatch && p.UserId != null))
            {
                Results.Add(new ElMatchResult(p.TenantId, p.SiteId, p.UserId!.Value, Winner == p.Team));
                if (Winner == p.Team) p.TotalWins++;
            }
        }

        private void BackToLobby()
        {
            foreach (var bot in Players.Values.Where(p => p.AutoBot).ToList()) Players.Remove(bot.Id);
            foreach (var p in Players.Values) { p.InMatch = false; p.Alive = false; }
            Shots.Clear();
            Things.Clear();
            Array.Clear(_dyn);
            Phase = ElPhase.Lobby;
        }

        // ------------------------------------------------------------------ input
        public void QueueInput(ElPlayer player, int seq, int bits, double aim)
        {
            if (seq <= player.LastSeq && player.Inputs.Count == 0 && seq > 0 && player.LastSeq - seq < 1000) return;
            player.Inputs.Enqueue((seq, bits, aim));
            while (player.Inputs.Count > 20) player.Inputs.Dequeue();
            LastActivity = DateTime.UtcNow;
        }

        // ------------------------------------------------------------------ tick
        public void Step()
        {
            Now += Dt;
            Tick++;
            switch (Phase)
            {
                case ElPhase.Lobby:
                    foreach (var p in Players.Values) { p.Inputs.Clear(); }
                    return;
                case ElPhase.Countdown when Now >= PhaseUntil:
                    Phase = ElPhase.Playing;
                    RoundStarted = Now;
                    RoundEnds = Now + RoundSeconds;
                    Events.Add(new ElEvent { K = "go" });
                    break;
                case ElPhase.RoundEnd when Now >= PhaseUntil:
                    if (Wins[0] >= 2 || Wins[1] >= 2 || Round >= 3) EndMatch();
                    else StartRound();
                    return;
                case ElPhase.Over when Now >= PhaseUntil:
                    BackToLobby();
                    return;
            }
            if (Phase == ElPhase.Over) return;

            var fighting = Phase == ElPhase.Playing;
            ExpireDyn();
            foreach (var p in Players.Values.ToList())
            {
                if (!p.InMatch) { p.Inputs.Clear(); continue; }
                if (!p.Alive) { DrainInputs(p); continue; }
                if (p.IsBot) BotThink(p);
                StepPlayer(p, fighting);
            }
            if (fighting)
            {
                StepShots();
                StepThings();
                CheckRoundEnd();
            }
        }

        private void DrainInputs(ElPlayer p)
        {
            while (p.Inputs.Count > 0) p.LastSeq = p.Inputs.Dequeue().Seq;
        }

        private void StepPlayer(ElPlayer p, bool fighting)
        {
            var b = p.Body;
            var haste = p.HasteUntil > Now ? 2 : 1;
            p.AttackCd = Math.Max(0, p.AttackCd - Dt);
            for (var i = 0; i < 3; i++) p.AbilityCd[i] = Math.Max(0, p.AbilityCd[i] - Dt * haste);
            b.Speed = (p.SpeedUntil > Now ? 1.45 : 1) * (p.SlowUntil > Now ? 0.6 : 1);
            var mask = Phase is ElPhase.Countdown ? 0 : ~0;

            var steps = 1;
            if (!p.IsBot && p.Inputs.Count > 3) steps = 2;
            for (var s = 0; s < steps; s++)
            {
                if (!p.IsBot && p.Inputs.Count > 0)
                {
                    var input = p.Inputs.Dequeue();
                    p.LastSeq = input.Seq;
                    p.Bits = input.Bits;
                    if (!double.IsNaN(input.Aim)) p.Aim = input.Aim;
                }
                ElementalsPhysics.Step(b, p.Bits & mask, _cell);
            }
            // facing: the way you move, unless you are attacking or casting, then the way you aim (Terraria's rule;
            // a keyboard player turns with A and D, a mouse player hits where the mouse points)
            if (b.LockT <= 0)
            {
                var moveDir = ((p.Bits & ElementalsPhysics.Right) != 0 ? 1 : 0) - ((p.Bits & ElementalsPhysics.Left) != 0 ? 1 : 0);
                var usingAim = (p.Bits & ElementalsPhysics.Attack) != 0 || p.CastUntil > Now || Now - p.SwingAt < 0.3;
                var cosAim = Math.Cos(p.Aim);
                if (!usingAim && moveDir != 0) p.Facing = moveDir;
                else if (Math.Abs(cosAim) > 0.05) p.Facing = cosAim >= 0 ? 1 : -1;
            }

            if (p.WantWeapon is int w) { p.Weapon = w; p.WantWeapon = null; p.AttackCd = Math.Max(p.AttackCd, 0.15); }
            if (!fighting) { p.WantAbility = null; return; }

            Hazards(p);
            if (!p.Alive) return;
            Buffs(p);
            if (p.Has("thornskin") && Now >= p.NextThornSkin)
            {
                p.ThornUntil = Now + 7;
                p.NextThornSkin = Now + 30;
                Events.Add(new ElEvent { K = "cast", A = p.Id, S = "thornskin" });
            }
            if (p.FireDashUntil > Now)
            {
                foreach (var e in Enemies(p).ToList())
                {
                    if (p.DashHit.Contains(e.Id) || !Overlaps(e.Body, b.X - b.W / 2 - 4, b.Y - b.H, b.W + 8, b.H)) continue;
                    p.DashHit.Add(e.Id);
                    Hurt(e, 2, p, "firedash", Math.Sign(b.DashVX) * 160, -160);
                }
            }

            var canAct = b.LockT <= 0 && p.CastUntil <= Now && b.FlyT <= 0;
            if (p.PendingAbility >= 0 && p.CastUntil <= Now) FinishCast(p);
            if (canAct && (p.Bits & ElementalsPhysics.Attack & mask) != 0 && p.AttackCd <= 0) Attack(p);
            if (p.WantAbility is int a)
            {
                p.WantAbility = null;
                if (canAct && a is >= 0 and < 3 && p.AbilityCd[a] <= 0) UseAbility(p, a);
            }
        }

        // ------------------------------------------------------------------ hazards and buffs
        private void Hazards(ElPlayer p)
        {
            var b = p.Body;
            if (ElementalsPhysics.Touches(b, _cell, 'L'))
            {
                if (p.BurnUntil <= Now) p.NextBurn = Now;
                p.BurnUntil = Now + 1.0;
            }
            if (p.BurnUntil > Now && Now >= p.NextBurn)
            {
                p.NextBurn = Now + 0.5;
                Hurt(p, 1, null, "burn", 0, 0);
                if (!p.Alive) return;
            }
            if (b.LandV > IcicleSpeed && ElementalsPhysics.Touches(b, _cell, 'I'))
            {
                Hurt(p, 4, null, "icicle", 0, -200);
                if (!p.Alive) return;
            }
            if (p.HazardReady > Now) return;
            if (ElementalsPhysics.Touches(b, _cell, '^'))
            {
                p.HazardReady = Now + 0.8;
                Hurt(p, 2, null, "spikes", 0, -340);
                return;
            }
            var cactusTouch = ElementalsPhysics.Touches(b, _cell, 'C') || TouchesEnemyCage(p) || TouchesEnemyPlant(p);
            if (cactusTouch)
            {
                p.HazardReady = Now + 0.8;
                var away = b.VX != 0 ? -Math.Sign(b.VX) : -p.Facing;
                Hurt(p, 1, null, "cactus", away * 230, -220);
            }
        }

        private bool TouchesEnemyCage(ElPlayer p)
        {
            var b = p.Body;
            var c0 = (int)Math.Floor((b.X - b.W / 2 - 1) / T);
            var c1 = (int)Math.Floor((b.X + b.W / 2 + 1) / T);
            var r0 = (int)Math.Floor((b.Y - b.H) / T);
            var r1 = (int)Math.Floor((b.Y + 1) / T);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                {
                    if (c < 0 || r < 0 || c >= ElementalsMap.Cols || r >= ElementalsMap.Height) continue;
                    var i = r * ElementalsMap.Cols + c;
                    if (_dyn[i] == 'c' && _dynTeam.TryGetValue(i, out var team) && team != p.Team) return true;
                }
            return false;
        }

        private bool TouchesEnemyPlant(ElPlayer p)
        {
            foreach (var t in Things)
            {
                if (t.Kind != "plant" || t.Team == p.Team) continue;
                if (Overlaps(p.Body, t.X - t.W / 2, t.Y - t.H, t.W, t.H)) return true;
            }
            return false;
        }

        private void Buffs(ElPlayer p)
        {
            if (p.RegenUntil > Now && Now >= p.NextRegen)
            {
                p.NextRegen = Now + 2;
                Heal(p, 1);
            }
            var b = p.Body;
            if (!b.Grounded || b.Ground != 'P') return;
            var cell = (int)Math.Floor(b.Y / T) * ElementalsMap.Cols + (int)Math.Floor(b.X / T);
            for (var k = 0; k < Map.Pads.Count; k++)
            {
                if (!Map.Pads[k].Contains(cell)) continue;
                var effect = PadEffect(k);
                switch (effect)
                {
                    case "heal":
                        if (Now >= p.PadTick) { p.PadTick = Now + 1; Heal(p, 1); }
                        break;
                    case "speed": p.SpeedUntil = Math.Max(p.SpeedUntil, Now + 3); break;
                    case "power": p.PowerUntil = Math.Max(p.PowerUntil, Now + 3); break;
                    case "haste": p.HasteUntil = Math.Max(p.HasteUntil, Now + 3); break;
                }
                break;
            }
        }

        private string PadEffect(int pad)
        {
            var cycle = Phase == ElPhase.Playing ? (int)((Now - RoundStarted) / 20) : 0;
            return PadEffects[(pad + cycle) % PadEffects.Length];
        }

        private void Heal(ElPlayer p, int amount)
        {
            if (p.Hp >= p.Kind.MaxHp) return;
            p.Hp = Math.Min(p.Kind.MaxHp, p.Hp + amount);
            Events.Add(new ElEvent { K = "heal", A = p.Id, V = amount, X = R(p.Body.X), Y = R(p.Body.Y - p.Body.H) });
        }

        // ------------------------------------------------------------------ damage
        private int Power(ElPlayer? attacker, int amount) =>
            attacker != null && attacker.PowerUntil > Now ? (int)Math.Ceiling(amount * 1.5) : amount;

        private void Hurt(ElPlayer target, int amount, ElPlayer? attacker, string source, double knockX, double knockY, bool melee = false)
        {
            if (!target.Alive || amount <= 0) return;
            amount = Power(attacker, amount);
            if (attacker != null && target.ShieldUntil > Now && Math.Sign(attacker.Body.X - target.Body.X) == target.Facing)
            {
                target.ShieldUntil = 0;
                Events.Add(new ElEvent { K = "block", B = target.Id, X = R(target.Body.X + target.Facing * 8), Y = R(target.Body.Y - target.Body.H / 2) });
                return;
            }
            if (target.ArmorUntil > Now && target.ArmorLeft > 0)
            {
                var absorbed = Math.Min(amount, target.ArmorLeft);
                target.ArmorLeft -= absorbed;
                amount -= absorbed;
                Events.Add(new ElEvent { K = "absorb", B = target.Id, V = absorbed, X = R(target.Body.X), Y = R(target.Body.Y - target.Body.H / 2) });
                if (amount <= 0) return;
            }
            target.Hp -= amount;
            if (attacker != null) attacker.Damage += amount;
            var b = target.Body;
            if (knockX != 0 || knockY != 0)
            {
                b.VX = knockX;
                if (knockY != 0) { b.VY = knockY; b.Grounded = false; b.Jumping = false; }
                b.DashT = 0;
            }
            Events.Add(new ElEvent { K = "hit", A = attacker?.Id, B = target.Id, V = amount, S = source, X = R(b.X), Y = R(b.Y - b.H / 2) });
            if (attacker != null && target.Alive)
            {
                if (melee && attacker.Has("frostbite")) target.SlowUntil = Math.Max(target.SlowUntil, Now + 0.8);
                if (melee && attacker.Has("kindling")) { if (target.BurnUntil <= Now) target.NextBurn = Now + 0.5; target.BurnUntil = Math.Max(target.BurnUntil, Now + 0.6); }
                if (attacker.Has("static") && ++attacker.HitCount % 3 == 0)
                {
                    target.Body.LockT = Math.Max(target.Body.LockT, 0.4);
                    target.StunUntil = Now + 0.4;
                }
                if (attacker.ThirstUntil > Now) Heal(attacker, 1);
                if (melee && target.ThornUntil > Now && attacker.Alive) Hurt(attacker, 1, target, "thornskin", Math.Sign(attacker.Body.X - b.X) * 140, -120);
            }
            if (target.Hp <= 0)
            {
                target.Hp = 0;
                target.Alive = false;
                target.PendingAbility = -1;
                if (attacker != null) attacker.Kills++;
                Events.Add(new ElEvent { K = "death", A = attacker?.Id, B = target.Id, S = source, X = R(b.X), Y = R(b.Y - b.H / 2) });
            }
        }

        private IEnumerable<ElPlayer> Enemies(ElPlayer p) => Players.Values.Where(o => o.InMatch && o.Alive && o.Team != p.Team);

        private static bool Overlaps(Body b, double x, double y, double w, double h) =>
            b.X + b.W / 2 > x && b.X - b.W / 2 < x + w && b.Y > y && b.Y - b.H < y + h;

        private static bool CircleHits(Body b, double cx, double cy, double r)
        {
            var nx = Math.Clamp(cx, b.X - b.W / 2, b.X + b.W / 2);
            var ny = Math.Clamp(cy, b.Y - b.H, b.Y);
            return (nx - cx) * (nx - cx) + (ny - cy) * (ny - cy) <= r * r;
        }

        private bool SolidAt(double x, double y) => ElementalsPhysics.IsSolid(Cell((int)Math.Floor(x / T), (int)Math.Floor(y / T)));

        private bool LineOfSight(double x1, double y1, double x2, double y2)
        {
            var d = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            var n = (int)Math.Ceiling(d / 4);
            for (var i = 1; i < n; i++)
            {
                var k = (double)i / n;
                if (SolidAt(x1 + (x2 - x1) * k, y1 + (y2 - y1) * k)) return false;
            }
            return true;
        }

        private (double X, double Y) Hand(ElPlayer p) => (p.Body.X + Math.Cos(p.Aim) * 8, p.Body.Y - p.Body.H * 0.6 + Math.Sin(p.Aim) * 8);

        /// <summary>The first point along the aim (from the chest, up to <paramref name="range"/>) before a solid cell.</summary>
        private (double X, double Y) AimPoint(ElPlayer p, double range)
        {
            var x = p.Body.X;
            var y = p.Body.Y - p.Body.H * 0.6;
            var dx = Math.Cos(p.Aim);
            var dy = Math.Sin(p.Aim);
            for (double d = 0; d < range; d += 4)
            {
                var nx = x + dx * 4;
                var ny = y + dy * 4;
                if (SolidAt(nx, ny)) break;
                x = nx; y = ny;
            }
            return (x, y);
        }

        /// <summary>The floor under a point: the top of the first solid or platform cell at or below it, within a few rows.</summary>
        private double? GroundBelow(double x, double y, int rows = 12)
        {
            var c = (int)Math.Floor(x / T);
            var r0 = (int)Math.Floor(y / T);
            for (var r = r0; r < r0 + rows && r < ElementalsMap.Height; r++)
            {
                var ch = Cell(c, r);
                if (ElementalsPhysics.IsSolid(ch) || ch == '=') return r * T;
            }
            return null;
        }

        // ------------------------------------------------------------------ attacks
        private void Attack(ElPlayer p)
        {
            var weapon = p.CurrentWeapon;
            p.AttackCd = weapon.Cooldown;
            var b = p.Body;
            if (weapon.Melee)
            {
                p.SwingAt = Now;
                var dx = Math.Cos(p.Aim);
                var dy = Math.Sin(p.Aim);
                var cx = b.X + dx * weapon.Reach * 0.55;
                var cy = b.Y - b.H * 0.55 + dy * weapon.Reach * 0.55;
                var radius = weapon.Reach * 0.65;
                Events.Add(new ElEvent { K = "swing", A = p.Id, S = weapon.Id });
                var landed = false;
                foreach (var e in Enemies(p).ToList())
                {
                    if (!CircleHits(e.Body, cx, cy, radius)) continue;
                    var side = Math.Sign(e.Body.X - b.X);
                    if (side == 0) side = p.Facing;
                    // a negative knock pulls (the magnet glove)
                    Hurt(e, weapon.Damage, p, weapon.Id, side * weapon.Knock, weapon.Knock < 0 ? -140 : -weapon.Knock * 0.45 - 80, true);
                    landed = true;
                }
                if (landed && weapon.Id == "pixelstick") p.SpeedUntil = Math.Max(p.SpeedUntil, Now + 2);
                PopDecoys(p, d => CircleHits(d.Body!, cx, cy, radius));
                foreach (var pot in Things.Where(t => t.Kind == "pot" && !t.Broken).ToList())
                {
                    var px = Math.Clamp(cx, pot.X - pot.W / 2, pot.X + pot.W / 2);
                    var py = Math.Clamp(cy, pot.Y - pot.H, pot.Y);
                    if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= radius * radius) BreakPot(pot, p);
                }
                return;
            }

            var (hx, hy) = Hand(p);
            if (weapon.Shot == "bolt")
            {
                var (ex, ey) = (hx, hy);
                ElPlayer? hit = null;
                var dx = Math.Cos(p.Aim);
                var dy = Math.Sin(p.Aim);
                for (double d = 0; d < weapon.Reach; d += 4)
                {
                    var nx = ex + dx * 4;
                    var ny = ey + dy * 4;
                    if (SolidAt(nx, ny)) break;
                    ex = nx; ey = ny;
                    hit = Enemies(p).FirstOrDefault(e => Overlaps(e.Body, ex - 2, ey - 2, 4, 4));
                    if (hit != null) break;
                    var pot = Things.FirstOrDefault(t => t.Kind == "pot" && !t.Broken && ex > t.X - t.W / 2 && ex < t.X + t.W / 2 && ey > t.Y - t.H && ey < t.Y);
                    if (pot != null) { BreakPot(pot, p); break; }
                }
                Events.Add(new ElEvent { K = "zap", A = p.Id, X = R(hx), Y = R(hy), X2 = R(ex), Y2 = R(ey) });
                if (hit != null) Hurt(hit, weapon.Damage, p, weapon.Id, Math.Sign(hit.Body.X - b.X) * weapon.Knock, 0);
                return;
            }

            var gravity = weapon.Shot switch { "cactusbomb" => 900, "cube" => 700, "arrow" => 260, _ => 0 };
            Shots.Add(new ElShot
            {
                Id = _nextId++, Kind = weapon.Shot, Owner = p.Id, Team = p.Team, X = hx, Y = hy,
                VX = Math.Cos(p.Aim) * weapon.ShotSpeed, VY = Math.Sin(p.Aim) * weapon.ShotSpeed,
                Gravity = gravity, Damage = weapon.Damage, Knock = weapon.Knock, Dies = Now + (weapon.Shot == "bubble" ? 3.5 : 2.5)
            });
            Events.Add(new ElEvent { K = "shoot", A = p.Id, S = weapon.Shot });
        }

        private void Spine(ElPlayer owner, double x, double y, double dx, double dy, int damage, double speed, double life)
        {
            Shots.Add(new ElShot
            {
                Id = _nextId++, Kind = "spine", Owner = owner.Id, Team = owner.Team, X = x, Y = y,
                VX = dx * speed, VY = dy * speed, Damage = damage, Knock = 120, Dies = Now + life
            });
        }

        private void StepShots()
        {
            for (var i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                Players.TryGetValue(s.Owner, out var owner);
                s.VY += s.Gravity * Dt;
                var steps = (int)Math.Ceiling(Math.Max(Math.Abs(s.VX), Math.Abs(s.VY)) * Dt / 4);
                if (steps < 1) steps = 1;
                var done = false;
                for (var k = 0; k < steps && !done; k++)
                {
                    s.X += s.VX * Dt / steps;
                    s.Y += s.VY * Dt / steps;
                    if (SolidAt(s.X, s.Y))
                    {
                        if (s.Kind == "cactusbomb" && owner != null) Burst(s, owner);
                        if (s.Kind == "bubble") SetDyn((int)Math.Floor(s.X / T), (int)Math.Floor(s.Y / T), 'i', 4, s.Team);
                        if (s.Kind == "cube") GlitchPlatform(s);
                        Events.Add(new ElEvent { K = "pop", S = s.Kind, X = R(s.X), Y = R(s.Y) });
                        done = true;
                        break;
                    }
                    foreach (var e in Players.Values)
                    {
                        if (!e.InMatch || !e.Alive || e.Team == s.Team) continue;
                        if (!Overlaps(e.Body, s.X - 3, s.Y - 3, 6, 6)) continue;
                        var side = Math.Sign(s.VX);
                        Hurt(e, s.Damage, owner, s.Kind, side * s.Knock, s.Knock > 0 ? -100 : 0);
                        if (s.Kind is "shard" or "bubble") e.SlowUntil = Math.Max(e.SlowUntil, Now + 1.2);
                        Events.Add(new ElEvent { K = "pop", S = s.Kind, X = R(s.X), Y = R(s.Y) });
                        done = true;
                        break;
                    }
                    if (done) break;
                    if (owner != null && PopDecoys(owner, d => Overlaps(d.Body!, s.X - 3, s.Y - 3, 6, 6)))
                    {
                        done = true;
                        break;
                    }
                    var pot = Things.FirstOrDefault(t => t.Kind == "pot" && !t.Broken && s.X > t.X - t.W / 2 && s.X < t.X + t.W / 2 && s.Y > t.Y - t.H && s.Y < t.Y);
                    if (pot != null && owner != null)
                    {
                        BreakPot(pot, owner);
                        if (s.Kind == "cactusbomb") Burst(s, owner);
                        done = true;
                    }
                }
                if (done || Now >= s.Dies) Shots.RemoveAt(i);
            }
        }

        private void Burst(ElShot s, ElPlayer owner)
        {
            // step back out of the wall so the thorns start in the air
            var x = s.X - s.VX * Dt * 0.5;
            var y = s.Y - s.VY * Dt * 0.5;
            for (var k = 0; k < 8; k++)
            {
                var a = k * Math.PI / 4;
                Spine(owner, x, y, Math.Cos(a), Math.Sin(a), 1, 260, 0.45);
            }
            Events.Add(new ElEvent { K = "boom", S = "thorns", X = R(x), Y = R(y) });
        }

        private void BreakPot(ElThing pot, ElPlayer by)
        {
            pot.Broken = true;
            pot.Until = Now + 20;
            var effect = PotEffects[_random.Next(PotEffects.Length)];
            switch (effect)
            {
                case "speed": by.SpeedUntil = Now + 8; break;
                case "regen": by.RegenUntil = Now + 8; by.NextRegen = Now + 1; break;
                case "power": by.PowerUntil = Now + 8; break;
                case "haste": by.HasteUntil = Now + 6; break;
                case "heal": Heal(by, 1); break;
            }
            Events.Add(new ElEvent { K = "pot", A = by.Id, S = effect, X = R(pot.X), Y = R(pot.Y - 6) });
        }

        // ------------------------------------------------------------------ abilities
        private static readonly Dictionary<string, double> Windups = new()
        {
            ["freeze"] = 0.4, ["pillar"] = 0.25, ["meteor"] = 0.2, ["judgement"] = 0.2, ["chain"] = 0.15, ["clap"] = 0.25, ["drain"] = 0.2
        };

        private void UseAbility(ElPlayer p, int slot)
        {
            var ability = p.Kind.Ability(p.Loadout[slot]);
            if (ability == null || ability.Passive) return;
            if (Windups.TryGetValue(ability.Id, out var windup))
            {
                p.AbilityCd[slot] = ability.Cooldown;
                p.PendingAbility = slot;
                p.PendingId = ability.Id;
                p.CastUntil = Now + windup;
                Events.Add(new ElEvent { K = "cast", A = p.Id, S = ability.Id, V = windup });
                return;
            }
            if (Perform(p, ability.Id))
            {
                p.AbilityCd[slot] = ability.Cooldown;
                Events.Add(new ElEvent { K = "cast", A = p.Id, S = ability.Id });
            }
        }

        private void FinishCast(ElPlayer p)
        {
            var id = p.PendingId;
            p.PendingAbility = -1;
            p.PendingId = null;
            if (id != null && p.Alive) Perform(p, id);
        }

        /// <summary>Does the ability; false when it cannot happen here (the cooldown is then not spent).</summary>
        private bool Perform(ElPlayer p, string id)
        {
            var b = p.Body;
            var cx = b.X;
            var cy = b.Y - b.H / 2;
            switch (id)
            {
                case "thorns":
                    foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, -1.0), (0.0, 1.0) })
                        Spine(p, cx, cy, dx, dy, 2, 330, 0.5);
                    return true;

                case "cage":
                {
                    if (!b.Grounded) return false;
                    var c = (int)Math.Floor(b.X / T);
                    var r = (int)Math.Floor(b.Y / T); // the row under the feet
                    var grow = 0;
                    for (var k = 1; k <= 3; k++)
                    {
                        // the pillar cell and the two cells the body will occupy above it must be free
                        if (ElementalsPhysics.IsSolid(Cell(c, r - k)) || ElementalsPhysics.IsSolid(Cell(c, r - k - 2))) break;
                        if (OtherPlayerIn(p, c, r - k)) break;
                        grow = k;
                    }
                    if (grow == 0) return false;
                    for (var k = 1; k <= grow; k++) SetDyn(c, r - k, 'c', 5, p.Team);
                    b.X = c * T + T / 2.0;
                    b.Y = (r - grow) * T;
                    b.VX = b.VY = 0;
                    Heal(p, 1);
                    Events.Add(new ElEvent { K = "grow", A = p.Id, X = R(b.X), Y = r * T, V = grow });
                    return true;
                }

                case "garden":
                {
                    var planted = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var x = b.X + p.Facing * (2 * T + k * 2 * T);
                        if (x < T || x > (ElementalsMap.Cols - 1) * T) break;
                        if (!LineOfSight(cx, cy, x, cy)) break;
                        var ground = GroundBelow(x, b.Y - 4, 8);
                        if (ground == null) continue;
                        Things.Add(new ElThing { Id = _nextId++, Kind = "plant", Owner = p.Id, Team = p.Team, X = x, Y = ground.Value, W = 10, H = 15, Start = Now, Until = Now + 12 });
                        planted++;
                    }
                    return planted > 0;
                }

                case "slide":
                {
                    b.DashT = 0.26;
                    b.DashVX = p.Facing * 430;
                    b.LockT = 0;
                    if (b.Grounded)
                    {
                        var r = (int)Math.Floor(b.Y / T);
                        var c0 = (int)Math.Floor(b.X / T);
                        for (var k = 0; k <= 7; k++)
                        {
                            var c = c0 + p.Facing * k;
                            if (ElementalsPhysics.IsSolid(Map.At(c, r)) && Map.At(c, r) != 'P') SetDyn(c, r, 'i', 5, p.Team);
                            if (ElementalsPhysics.IsSolid(Cell(c, r - 1))) break;
                        }
                    }
                    return true;
                }

                case "wall":
                {
                    var c = (int)Math.Floor((b.X + p.Facing * 2 * T) / T);
                    var ground = GroundBelow(c * T + T / 2.0, b.Y - 4, 6);
                    if (ground == null) return false;
                    var r = (int)(ground.Value / T);
                    var built = 0;
                    for (var k = 1; k <= 3; k++)
                    {
                        if (ElementalsPhysics.IsSolid(Cell(c, r - k)) || Cell(c, r - k) == '=' && k > 1) break;
                        if (CellBlockedByPlayer(c, r - k)) break;
                        SetDyn(c, r - k, 'w', 6, p.Team);
                        built++;
                    }
                    if (built > 0) Events.Add(new ElEvent { K = "wall", A = p.Id, X = c * T + T / 2.0, Y = r * T, V = built });
                    return built > 0;
                }

                case "freeze":
                    foreach (var e in Enemies(p).ToList())
                    {
                        if (!CircleHits(e.Body, cx, cy, 64)) continue;
                        Hurt(e, 2, p, "freeze", 0, 0);
                        e.Body.LockT = Math.Max(e.Body.LockT, 1.3);
                        e.FrozenUntil = Now + 1.3;
                    }
                    Events.Add(new ElEvent { K = "boom", A = p.Id, S = "freeze", X = R(cx), Y = R(cy), V = 64 });
                    return true;

                case "rocket":
                    b.VY = -580;
                    b.Grounded = false;
                    b.Jumping = false;
                    b.DashT = 0;
                    foreach (var e in Enemies(p).ToList())
                    {
                        if (!CircleHits(e.Body, b.X, b.Y - 4, 34)) continue;
                        Hurt(e, 2, p, "rocket", Math.Sign(e.Body.X - b.X) * 220, -200);
                    }
                    Events.Add(new ElEvent { K = "boom", A = p.Id, S = "rocket", X = R(b.X), Y = R(b.Y), V = 34 });
                    return true;

                case "pillar":
                {
                    var (ax, ay) = AimPoint(p, 8 * T);
                    var ground = GroundBelow(ax, ay, 10);
                    if (ground == null) return false;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "pillar", Owner = p.Id, Team = p.Team, X = ax, Y = ground.Value, W = 24, H = 64, Start = Now, Until = Now + 3, NextTick = Now });
                    return true;
                }

                case "meteor":
                {
                    var (ax, ay) = AimPoint(p, 14 * T);
                    var ground = GroundBelow(ax, ay, 16) ?? ay;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "meteor", Owner = p.Id, Team = p.Team, X = ax, Y = ground, W = 80, H = 80, Start = Now, Until = Now + 1.0 });
                    return true;
                }

                case "step":
                {
                    var dx = Math.Cos(p.Aim);
                    var dy = Math.Sin(p.Aim);
                    double bestX = b.X, bestY = b.Y;
                    for (double d = 4; d <= 80; d += 4)
                    {
                        var x = b.X + dx * d;
                        var y = b.Y + dy * d;
                        if (BoxSolid(x, y, b.W, b.H)) break;
                        bestX = x; bestY = y;
                    }
                    Events.Add(new ElEvent { K = "blink", A = p.Id, X = R(b.X), Y = R(b.Y - b.H / 2), X2 = R(bestX), Y2 = R(bestY - b.H / 2) });
                    b.X = bestX;
                    b.Y = bestY;
                    b.VY = Math.Min(b.VY, 0) * 0.3;
                    b.Grounded = false;
                    return true;
                }

                case "chain":
                {
                    var first = Enemies(p).Where(e => Dist(e.Body, cx, cy) < 170 && LineOfSight(cx, cy, e.Body.X, e.Body.Y - e.Body.H / 2))
                        .OrderBy(e => Dist(e.Body, cx, cy)).FirstOrDefault();
                    if (first == null)
                    {
                        Events.Add(new ElEvent { K = "fizzle", A = p.Id });
                        return true;
                    }
                    var fx = first.Body.X;
                    var fy = first.Body.Y - first.Body.H / 2;
                    Events.Add(new ElEvent { K = "zap", A = p.Id, S = "chain", X = R(cx), Y = R(cy), X2 = R(fx), Y2 = R(fy) });
                    Hurt(first, 2, p, "chain", 0, 0);
                    first.Body.LockT = Math.Max(first.Body.LockT, 0.3);
                    first.StunUntil = Now + 0.3;
                    var second = Enemies(p).Where(e => e != first && Dist(e.Body, fx, fy) < 110 && LineOfSight(fx, fy, e.Body.X, e.Body.Y - e.Body.H / 2))
                        .OrderBy(e => Dist(e.Body, fx, fy)).FirstOrDefault();
                    if (second != null)
                    {
                        Events.Add(new ElEvent { K = "zap", A = p.Id, S = "chain", X = R(fx), Y = R(fy), X2 = R(second.Body.X), Y2 = R(second.Body.Y - second.Body.H / 2) });
                        Hurt(second, 1, p, "chain", 0, 0);
                        second.Body.LockT = Math.Max(second.Body.LockT, 0.3);
                        second.StunUntil = Now + 0.3;
                    }
                    return true;
                }

                case "shield":
                    p.ShieldUntil = Now + 2;
                    return true;

                case "regrow":
                    Heal(p, 2);
                    return true;

                case "armor":
                    p.ArmorUntil = Now + 5;
                    p.ArmorLeft = 2;
                    return true;

                case "blizzard":
                {
                    var (ax, ay) = AimPoint(p, 12 * T);
                    var ground = GroundBelow(ax, ay, 12) ?? ay;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "blizzard", Owner = p.Id, Team = p.Team, X = ax, Y = ground, W = 80, H = 72, Start = Now, Until = Now + 4, NextTick = Now + 0.5 });
                    return true;
                }

                case "firedash":
                    b.DashT = 0.22;
                    b.DashVX = p.Facing * 440;
                    b.LockT = 0;
                    p.FireDashUntil = Now + 0.26;
                    p.DashHit.Clear();
                    return true;

                case "inferno":
                    Things.Add(new ElThing { Id = _nextId++, Kind = "inferno", Owner = p.Id, Team = p.Team, X = b.X, Y = b.Y, W = 84, H = 60, Start = Now, Until = Now + 3, NextTick = Now + 0.2 });
                    return true;

                case "clap":
                    foreach (var e in Enemies(p).ToList())
                    {
                        if (!CircleHits(e.Body, cx, cy, 54)) continue;
                        var side = Math.Sign(e.Body.X - b.X);
                        Hurt(e, 2, p, "clap", (side == 0 ? p.Facing : side) * 360, -260);
                    }
                    Events.Add(new ElEvent { K = "boom", A = p.Id, S = "clap", X = R(cx), Y = R(cy), V = 54 });
                    return true;

                case "bloodpool":
                {
                    var (ax, ay) = AimPoint(p, 9 * T);
                    var ground = GroundBelow(ax, ay, 12);
                    if (ground == null) return false;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "bloodpool", Owner = p.Id, Team = p.Team, X = ax, Y = ground.Value, W = 64, H = 10, Start = Now, Until = Now + 5 });
                    return true;
                }

                case "thirst":
                    p.ThirstUntil = Now + 6;
                    return true;

                case "bat":
                    b.FlyT = 3;
                    b.Jumping = false;
                    return true;

                case "drain":
                {
                    var victim = Enemies(p).Where(e => Dist(e.Body, cx, cy) < 170 && LineOfSight(cx, cy, e.Body.X, e.Body.Y - e.Body.H / 2))
                        .OrderBy(e => Dist(e.Body, cx, cy)).FirstOrDefault();
                    if (victim == null) { Events.Add(new ElEvent { K = "fizzle", A = p.Id }); return true; }
                    Events.Add(new ElEvent { K = "zap", A = p.Id, S = "drain", X = R(victim.Body.X), Y = R(victim.Body.Y - victim.Body.H / 2), X2 = R(cx), Y2 = R(cy) });
                    Hurt(victim, 2, p, "drain", 0, 0);
                    Heal(p, 2);
                    return true;
                }

                case "rewind":
                    if (p.MarkUntil > Now)
                    {
                        if (BoxSolid(p.MarkX, p.MarkY, b.W, b.H)) return false;
                        Events.Add(new ElEvent { K = "blink", A = p.Id, X = R(b.X), Y = R(b.Y - b.H / 2), X2 = R(p.MarkX), Y2 = R(p.MarkY - b.H / 2), S = "rewind" });
                        b.X = p.MarkX;
                        b.Y = p.MarkY;
                        b.VX = b.VY = 0;
                        b.DashT = 0;
                        b.Grounded = false;
                        p.MarkUntil = 0;
                        return true;
                    }
                    p.MarkX = b.X;
                    p.MarkY = b.Y;
                    p.MarkUntil = Now + 10;
                    Events.Add(new ElEvent { K = "mark", A = p.Id, X = R(b.X), Y = R(b.Y) });
                    return false; // marking is free; the cooldown starts when you snap back

                case "decoy":
                {
                    var body = new Body { X = b.X, Y = b.Y, VX = b.VX, VY = b.VY, W = b.W, H = b.H, Grounded = b.Grounded, Ground = b.Ground };
                    var bits = p.Bits & (ElementalsPhysics.Left | ElementalsPhysics.Right);
                    if (bits == 0) bits = p.Facing > 0 ? ElementalsPhysics.Right : ElementalsPhysics.Left;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "decoy", Owner = p.Id, Team = p.Team, X = b.X, Y = b.Y, W = b.W, H = b.H, Start = Now, Until = Now + 2, Body = body, Bits = bits, Facing = (bits & ElementalsPhysics.Right) != 0 ? 1 : -1 });
                    return true;
                }

                case "window":
                {
                    var (ax, ay) = AimPoint(p, 5 * T);
                    var c = (int)Math.Floor(ax / T);
                    var r = (int)Math.Floor(ay / T);
                    var placed = 0;
                    for (var k = -1; k <= 1; k++)
                    {
                        if (Cell(c + k, r) != '.' || CellBlockedByPlayer(c + k, r)) continue;
                        SetDyn(c + k, r, '=', 3, p.Team);
                        placed++;
                    }
                    return placed > 0;
                }

                case "judgement":
                {
                    var (ax, ay) = AimPoint(p, 16 * T);
                    var ground = GroundBelow(ax, ay, 22) ?? ay;
                    Things.Add(new ElThing { Id = _nextId++, Kind = "judgement", Owner = p.Id, Team = p.Team, X = ax, Y = ground, W = 26, H = ground, Start = Now, Until = Now + 0.9 });
                    return true;
                }
            }
            return false;
        }

        private void GlitchPlatform(ElShot s)
        {
            // step back to the last air cell along the flight and lay two cells of light there
            var x = s.X - s.VX * Dt * 0.5;
            var y = s.Y - s.VY * Dt * 0.5;
            var c = (int)Math.Floor(x / T);
            var r = (int)Math.Floor(y / T);
            for (var k = 0; k <= 1; k++)
            {
                var cc = c + (s.VX >= 0 ? -k : k);
                if (Cell(cc, r) == '.' && !CellBlockedByPlayer(cc, r)) SetDyn(cc, r, '=', 2, s.Team);
            }
        }

        /// <summary>Pops the enemy decoys of <paramref name="attacker"/> that the test hits; true when one popped.</summary>
        private bool PopDecoys(ElPlayer attacker, Func<ElThing, bool> hit)
        {
            var popped = false;
            for (var i = Things.Count - 1; i >= 0; i--)
            {
                var t = Things[i];
                if (t.Kind != "decoy" || t.Team == attacker.Team || !hit(t)) continue;
                Things.RemoveAt(i);
                Events.Add(new ElEvent { K = "poof", A = t.Owner, X = R(t.Body!.X), Y = R(t.Body.Y - t.Body.H / 2) });
                popped = true;
            }
            return popped;
        }

        private bool OtherPlayerIn(ElPlayer me, int c, int r)
        {
            foreach (var p in Players.Values)
            {
                if (p == me || !p.InMatch || !p.Alive) continue;
                if (Overlaps(p.Body, c * T, r * T, T, T)) return true;
            }
            return false;
        }

        private bool BoxSolid(double x, double feet, double w, double h)
        {
            for (var yy = feet - h + 1; yy <= feet - 1; yy += 6)
            {
                if (SolidAt(x - w / 2 + 1, yy) || SolidAt(x + w / 2 - 1, yy)) return true;
            }
            return SolidAt(x - w / 2 + 1, feet - 1) || SolidAt(x + w / 2 - 1, feet - 1) || feet > ElementalsMap.Height * T - T || feet - h < T;
        }

        private static double Dist(Body b, double x, double y)
        {
            var dx = b.X - x;
            var dy = b.Y - b.H / 2 - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void StepThings()
        {
            for (var i = Things.Count - 1; i >= 0; i--)
            {
                var t = Things[i];
                Players.TryGetValue(t.Owner ?? "", out var owner);
                switch (t.Kind)
                {
                    case "pot":
                        if (t.Broken && Now >= t.Until) { t.Broken = false; Events.Add(new ElEvent { K = "potback", X = R(t.X), Y = R(t.Y) }); }
                        continue;
                    case "plant":
                        if (Now >= t.Until) Things.RemoveAt(i);
                        continue;
                    case "decoy":
                        if (Now >= t.Until) { Things.RemoveAt(i); Events.Add(new ElEvent { K = "poof", A = t.Owner, X = R(t.Body!.X), Y = R(t.Body.Y - t.Body.H / 2) }); continue; }
                        ElementalsPhysics.Step(t.Body!, t.Bits, _cell);
                        t.X = t.Body!.X;
                        t.Y = t.Body.Y;
                        continue;
                    case "pillar":
                        if (Now >= t.Until) { Things.RemoveAt(i); continue; }
                        if (Now >= t.NextTick && Now - t.Start >= 0.3)
                        {
                            t.NextTick = Now + 0.5;
                            foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                                if (Overlaps(e.Body, t.X - t.W / 2, t.Y - t.H, t.W, t.H)) Hurt(e, 1, owner, "pillar", 0, 0);
                        }
                        continue;
                    case "blizzard":
                        if (Now >= t.Until) { Things.RemoveAt(i); continue; }
                        foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                        {
                            if (!Overlaps(e.Body, t.X - t.W / 2, t.Y - t.H, t.W, t.H)) continue;
                            e.SlowUntil = Math.Max(e.SlowUntil, Now + 0.4);
                            if (Now >= t.NextTick) Hurt(e, 1, owner, "blizzard", 0, 0);
                        }
                        if (Now >= t.NextTick) t.NextTick = Now + 1;
                        continue;
                    case "inferno":
                        if (Now >= t.Until || owner == null || !owner.Alive) { Things.RemoveAt(i); continue; }
                        t.X = owner.Body.X;
                        t.Y = owner.Body.Y;
                        if (Now >= t.NextTick)
                        {
                            t.NextTick = Now + 0.5;
                            foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                                if (CircleHits(e.Body, t.X, t.Y - 14, 42)) Hurt(e, 1, owner, "inferno", 0, 0);
                        }
                        continue;
                    case "bloodpool":
                        if (Now >= t.Until) { Things.RemoveAt(i); continue; }
                        foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                            if (Overlaps(e.Body, t.X - t.W / 2, t.Y - t.H, t.W, t.H + 1)) e.SlowUntil = Math.Max(e.SlowUntil, Now + 0.3);
                        continue;
                    case "meteor":
                        if (Now < t.Until) continue;
                        Things.RemoveAt(i);
                        foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                            if (CircleHits(e.Body, t.X, t.Y - 8, 42)) Hurt(e, 5, owner, "meteor", Math.Sign(e.Body.X - t.X) * 260, -320);
                        foreach (var pot in Things.Where(x => x.Kind == "pot" && !x.Broken && Math.Abs(x.X - t.X) < 42 && Math.Abs(x.Y - t.Y) < 42).ToList())
                            if (owner != null) BreakPot(pot, owner);
                        Events.Add(new ElEvent { K = "boom", A = t.Owner, S = "meteor", X = R(t.X), Y = R(t.Y), V = 42 });
                        continue;
                    case "judgement":
                        if (Now < t.Until) continue;
                        Things.RemoveAt(i);
                        foreach (var e in Players.Values.Where(e => e.InMatch && e.Alive && e.Team != t.Team).ToList())
                        {
                            if (!Overlaps(e.Body, t.X - t.W / 2, 0, t.W, t.Y + 1)) continue;
                            Hurt(e, 4, owner, "judgement", 0, -150);
                            e.Body.LockT = Math.Max(e.Body.LockT, 0.6);
                            e.StunUntil = Now + 0.6;
                        }
                        Events.Add(new ElEvent { K = "boom", A = t.Owner, S = "judgement", X = R(t.X), Y = R(t.Y), V = t.W / 2 });
                        continue;
                }
            }
        }

        private void ExpireDyn()
        {
            if (_dynUntil.Count == 0) return;
            foreach (var (i, until) in _dynUntil.ToList())
            {
                if (Now < until) continue;
                _dyn[i] = '\0';
                _dynUntil.Remove(i);
                _dynTeam.Remove(i);
            }
        }

        private void CheckRoundEnd()
        {
            var alive = new int[2];
            foreach (var p in Players.Values) if (p.InMatch && p.Alive) alive[p.Team]++;
            if (alive[0] == 0 || alive[1] == 0)
            {
                EndRound(alive[0] == alive[1] ? -1 : (alive[0] > 0 ? 0 : 1), "knockout");
                return;
            }
            if (Now < RoundEnds) return;
            var hp = new int[2];
            foreach (var p in Players.Values) if (p.InMatch && p.Alive) hp[p.Team] += p.Hp;
            var diff = hp[0] - hp[1];
            EndRound(Math.Abs(diff) > DrawMargin ? (diff > 0 ? 0 : 1) : -1, "time");
        }

        // ------------------------------------------------------------------ bots
        private void BotThink(ElPlayer bot)
        {
            var b = bot.Body;
            if (bot.BotJumpTicks > 0)
            {
                bot.BotJumpTicks--;
                if (bot.BotJumpTicks == 0) bot.Bits &= ~ElementalsPhysics.Jump;
            }
            if (Now < bot.NextThink) return;
            bot.NextThink = Now + 0.12 + _random.NextDouble() * 0.08;

            var cx = b.X;
            var cy = b.Y - b.H / 2;
            var target = Enemies(bot).OrderBy(e => Dist(e.Body, cx, cy)).FirstOrDefault();
            var keepJump = bot.Bits & ElementalsPhysics.Jump;
            if (target == null || Phase != ElPhase.Playing) { bot.Bits = keepJump; return; }
            var tx = target.Body.X;
            var ty = target.Body.Y - target.Body.H / 2;
            var dx = tx - cx;
            var dy = ty - cy;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var sight = LineOfSight(cx, cy, tx, ty);

            var meleeSlot = bot.Kind.Weapon(bot.Arms[0])?.Melee == true ? 0 : 1;
            var wantWeapon = dist < 80 ? meleeSlot : 1 - meleeSlot;
            if (bot.Weapon != wantWeapon && _random.NextDouble() < 0.35) bot.WantWeapon = wantWeapon;
            var weapon = bot.CurrentWeapon;

            var dir = 0;
            if (weapon.Melee) dir = Math.Abs(dx) > weapon.Reach * 0.5 ? Math.Sign(dx) : 0;
            else if (Math.Abs(dx) > 190 || !sight) dir = Math.Sign(dx);
            else if (Math.Abs(dx) < 70) dir = -Math.Sign(dx);
            else dir = _random.NextDouble() < 0.5 ? 0 : (_random.NextDouble() < 0.5 ? -1 : 1);

            var want = false;
            var fr = (int)Math.Floor((b.Y - 1) / T);
            var inHazard = ElementalsPhysics.Touches(b, _cell, 'L') || ElementalsPhysics.Touches(b, _cell, '^') || ElementalsPhysics.Touches(b, _cell, 'C');
            if (inHazard)
            {
                // get out toward the nearer clear side
                var c0 = (int)Math.Floor(b.X / T);
                int left = 0, right = 0;
                while (left < 30 && HazardColumn(c0 - left, fr)) left++;
                while (right < 30 && HazardColumn(c0 + right, fr)) right++;
                dir = left <= right ? -1 : 1;
                want = true;
            }
            else if (dir != 0 && b.Grounded)
            {
                var fc = (int)Math.Floor((b.X + dir * (b.W / 2 + 6)) / T);
                var wall = ElementalsPhysics.IsSolid(Cell(fc, fr)) || ElementalsPhysics.IsSolid(Cell(fc, fr - 1));
                var run = 0;
                for (var k = 0; k < 6; k++)
                {
                    var c = fc + dir * k;
                    var at = Cell(c, fr);
                    var floor = Cell(c, fr + 1);
                    var hole = !ElementalsPhysics.IsSolid(floor) && floor != '=';
                    var bad = at is '^' or 'C' or 'L' || floor is 'L' || (hole && LandsOnHazard(c, fr + 1));
                    if (!bad) break;
                    run++;
                }
                if (run > 3) dir = 0;                 // a wide pit: hold the edge
                else if (run > 0 || wall) want = true; // a short hazard or a step: jump it
                if (dir != 0 && Math.Abs(b.X - bot.LastX) < 0.5) want = true;
            }
            if (dy < -36 && b.Grounded && _random.NextDouble() < 0.45) want = true;
            bot.LastX = b.X;

            var bits = 0;
            if (dir < 0) bits |= ElementalsPhysics.Left;
            if (dir > 0) bits |= ElementalsPhysics.Right;
            if (want && (bot.Bits & ElementalsPhysics.Jump) == 0)
            {
                bits |= ElementalsPhysics.Jump;
                bot.BotJumpTicks = 6 + _random.Next(8);
            }
            else bits |= keepJump;
            if (dy > 40 && b.Grounded && b.Ground == '=' && !LandsOnHazard((int)Math.Floor(b.X / T), fr + 2)) bits |= ElementalsPhysics.Down;

            var aim = Math.Atan2(dy, dx);
            if (weapon.Shot is "cactusbomb" or "cube") aim = Math.Atan2(dy - Math.Abs(dx) * 0.45, dx);
            else if (weapon.Shot == "arrow") aim = Math.Atan2(dy - Math.Abs(dx) * 0.12, dx);
            aim += (_random.NextDouble() - 0.5) * 0.25;
            bot.Aim = aim;

            var inRange = weapon.Melee ? dist < weapon.Reach + 14 : sight && dist < 280;
            if (inRange) bits |= ElementalsPhysics.Attack;
            bot.Bits = bits;

            if (_random.NextDouble() < 0.12)
            {
                var slot = _random.Next(3);
                if (bot.AbilityCd[slot] <= 0 && AbilityMakesSense(bot.Loadout[slot], dist, sight, dy)) bot.WantAbility = slot;
            }
        }

        private bool HazardColumn(int c, int r) => Cell(c, r) is 'L' or '^' or 'C' || Cell(c, r + 1) is 'L';

        /// <summary>Whether something falling down column <paramref name="c"/> from row <paramref name="r"/> lands in lava or on spikes.</summary>
        private bool LandsOnHazard(int c, int r)
        {
            for (var y = r; y < ElementalsMap.Height; y++)
            {
                var ch = Cell(c, y);
                if (ch is 'L' or '^' or 'C') return true;
                if (ElementalsPhysics.IsSolid(ch) || ch == '=') return false;
            }
            return false;
        }

        private static bool AbilityMakesSense(string id, double dist, bool sight, double dy) => id switch
        {
            "thorns" or "freeze" or "rocket" or "clap" or "inferno" => dist < 70,
            "shield" or "armor" or "thirst" => dist < 90,
            "regrow" => true,
            "firedash" => dist < 110 && Math.Abs(dy) < 24,
            "blizzard" or "bloodpool" or "drain" => dist < 170 && sight,
            "bat" => dist > 160,
            "decoy" => dist < 120,
            "rewind" => true,
            "window" => dy < -40,
            "cage" => dist < 60,
            "garden" or "wall" => dist < 140 && Math.Abs(dy) < 30,
            "slide" or "step" => dist > 110,
            "chain" => dist < 160 && sight,
            "pillar" or "meteor" or "judgement" => dist < 220 && sight,
            _ => false
        };

        // ------------------------------------------------------------------ snapshot
        private static double R(double v) => Math.Round(v, 1);

        public object Snapshot()
        {
            var timeLeft = Phase switch
            {
                ElPhase.Playing => Math.Max(0, RoundEnds - Now),
                ElPhase.Countdown => Math.Max(0, PhaseUntil - Now),
                ElPhase.RoundEnd or ElPhase.Over => Math.Max(0, PhaseUntil - Now),
                _ => 0
            };
            var dyn = new List<object>();
            foreach (var i in _dynUntil.Keys) dyn.Add(new object[] { i, _dyn[i].ToString() });
            return new
            {
                ph = Phase.ToString().ToLowerInvariant(),
                now = R(Now),
                rd = Round,
                wins = Wins,
                win = Winner,
                tl = R(timeLeft),
                mode = TeamSize,
                map = Phase == ElPhase.Lobby ? MapChoice : Map.Id,
                owner = OwnerId,
                pads = Phase == ElPhase.Lobby ? Array.Empty<string>() : Enumerable.Range(0, Map.Pads.Count).Select(PadEffect).ToArray(),
                // players as compact arrays (order in PlayerFields); names, heroes and loadouts only when they change
                ps = Players.Values.Select(p => new object?[]
                {
                    p.Id, R(p.Body.X), R(p.Body.Y), R(p.Body.VX), R(p.Body.VY), p.Body.Grounded ? 1 : 0, p.Body.Ground.ToString(),
                    Math.Round(p.Body.Coyote, 2), p.Body.PrevJump ? 1 : 0, p.Body.Jumping ? 1 : 0, Math.Round(p.Body.DashT, 2), R(p.Body.DashVX),
                    Math.Round(p.Body.FlyT, 2), Math.Round(p.Body.LockT, 2), Math.Round(p.Body.Speed, 2), p.Weapon, p.Facing, Math.Round(p.Aim, 2),
                    Math.Round(Math.Min(9, Now - p.SwingAt), 2), Math.Round(p.AttackCd, 1), p.Hp, p.Alive ? 1 : 0, Status(p), p.LastSeq,
                    Math.Round(p.AbilityCd[0], 1), Math.Round(p.AbilityCd[1], 1), Math.Round(p.AbilityCd[2], 1), p.InMatch ? 1 : 0,
                    p.MarkUntil > Now ? new[] { R(p.MarkX), R(p.MarkY) } : null
                }).ToArray(),
                ro = RosterIfChanged(),
                sh = Shots.Select(s => new { i = s.Id, k = s.Kind, x = R(s.X), y = R(s.Y), vx = R(s.VX), vy = R(s.VY), tm = s.Team }).ToArray(),
                th = Things.Where(t => !(t.Kind == "pot" && t.Broken)).Select(t => new { i = t.Id, k = t.Kind, x = R(t.X), y = R(t.Y), w = t.W, h = t.H, tm = t.Team, age = R(Now - t.Start), left = R(t.Until - Now), o = t.Owner, f = t.Facing, vx = t.Body == null ? 0 : R(t.Body.VX), g = t.Body?.Grounded == true ? 1 : 0 }).ToArray(),
                dy = dyn,
                ev = Events.Select(e => new { k = e.K, a = e.A, b = e.B, x = e.X, y = e.Y, x2 = e.X2, y2 = e.Y2, v = e.V, s = e.S }).ToArray()
            };
        }

        private string _rosterKey = "";
        private int _sinceRoster;

        /// <summary>Who is who (name, hero, team, loadout): sent when it changes and once in a while for late joiners.</summary>
        private object? RosterIfChanged()
        {
            var key = string.Join(";", Players.Values.Select(p => $"{p.Id}|{p.Name}|{p.Kind.Id}|{p.Team}|{string.Join(",", p.Loadout)}|{string.Join(",", p.Arms)}|{p.TotalWins}|{p.Kills}"));
            if (key == _rosterKey && ++_sinceRoster < 15) return null;
            _rosterKey = key;
            _sinceRoster = 0;
            return Players.Values.Select(p => new { id = p.Id, n = p.Name, k = p.Kind.Id, tm = p.Team, bot = p.IsBot, mh = p.Kind.MaxHp, lo = p.Loadout, wp = p.Arms, tw = p.TotalWins, kl = p.Kills }).ToArray();
        }

        private int Status(ElPlayer p)
        {
            var s = 0;
            if (p.SlowUntil > Now) s |= 1;
            if (p.SpeedUntil > Now) s |= 2;
            if (p.PowerUntil > Now) s |= 4;
            if (p.HasteUntil > Now) s |= 8;
            if (p.RegenUntil > Now) s |= 16;
            if (p.BurnUntil > Now) s |= 32;
            if (p.FrozenUntil > Now) s |= 64;
            if (p.StunUntil > Now) s |= 128;
            if (p.CastUntil > Now) s |= 256;
            if (p.ShieldUntil > Now) s |= 512;
            if (p.ArmorUntil > Now && p.ArmorLeft > 0) s |= 1024;
            if (p.ThornUntil > Now) s |= 2048;
            if (p.ThirstUntil > Now) s |= 4096;
            if (p.Body.FlyT > 0) s |= 8192;
            return s;
        }
    }
}
