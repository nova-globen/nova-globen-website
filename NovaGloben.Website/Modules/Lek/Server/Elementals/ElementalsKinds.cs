namespace Lek.Server.Elementals
{
    /// <summary>A weapon. Damage is in half hearts. Melee weapons hit an arc of <see cref="Reach"/> px; ranged ones fire <see cref="Shot"/>.</summary>
    public sealed record WeaponDef(string Id, string Name, bool Melee, int Damage, double Cooldown, double Reach, double Knock, string Shot, double ShotSpeed, string Note);

    /// <summary>An ability in a hero's pool. Passives work on their own and take a loadout slot without a key.</summary>
    public sealed record AbilityDef(string Id, string Name, double Cooldown, string Note, bool Passive = false);

    /// <summary>
    /// A hero. Hearts trade against damage, cooldowns and hitbox: more health means something else is worse.
    /// Each hero has a pool of abilities; before a match the player picks any three of them (chosen for synergy),
    /// and they sit on R, T and Y in the order picked.
    /// </summary>
    public sealed record KindDef(string Id, string Name, string Element, int Hearts, double W, double H, string Tradeoff, WeaponDef[] Weapons, AbilityDef[] Pool)
    {
        public int MaxHp => Hearts * 2;

        public string[] DefaultLoadout => Pool.Take(3).Select(a => a.Id).ToArray();

        /// <summary>Weapons anyone can carry: middling numbers, so a hero's own weapons stay the interesting pick.</summary>
        public static readonly WeaponDef[] Common =
        {
            new("sword", "Sword", true, 2, 0.7, 24, 150, "", 0, "Common. Close: 1 heart every 0.7 s"),
            new("bow", "Bow", false, 2, 1.4, 0, 90, "arrow", 400, "Common. An arrow that drops a little: 1 heart every 1.4 s")
        };

        /// <summary>The two weapons a hero starts with: its own first two.</summary>
        public string[] DefaultArms => Weapons.Take(2).Select(w => w.Id).ToArray();

        public WeaponDef? Weapon(string? id) => Weapons.Concat(Common).FirstOrDefault(w => w.Id == id);

        public AbilityDef? Ability(string? id) => Pool.FirstOrDefault(a => a.Id == id);

        public static readonly KindDef Cactus = new("cactus", "Cactus Man", "nature", 8, 14, 30, "Slow ranged weapon",
            new[]
            {
                new WeaponDef("nail", "Cactus Nail", true, 2, 0.5, 22, 150, "", 0, "Close: 1 heart every 0.5 s"),
                new WeaponDef("thrower", "Cactus Thrower", false, 4, 4.0, 0, 120, "cactusbomb", 330, "Lobbed cactus: 2 hearts direct, else 8 thorns of half a heart")
            },
            new[]
            {
                new AbilityDef("thorns", "Thorn Burst", 10, "Thorns fly in four directions, 1 heart each"),
                new AbilityDef("cage", "Cactus Cage", 20, "A cactus pillar lifts you 3 tiles for 5 s; it stings enemies; heals half a heart"),
                new AbilityDef("garden", "Cactus Garden", 30, "Plants 4 cacti ahead for 12 s: half a heart and a push to enemies"),
                new AbilityDef("shield", "Cactus Shield", 10, "For 2 s the first hit from the front is blocked; head and feet stay open"),
                new AbilityDef("regrow", "Root Regen", 20, "Heal 1 heart"),
                new AbilityDef("thornskin", "Thorn Skin", 30, "Passive: every 30 s your skin bristles for 7 s; whoever hits you up close takes half a heart", true)
            });

        public static readonly KindDef Ice = new("ice", "Ice Man", "ice", 7, 14, 30, "Average",
            new[]
            {
                new WeaponDef("frostblade", "Frost Blade", true, 2, 0.55, 26, 160, "", 0, "Close: 1 heart every 0.55 s, long reach"),
                new WeaponDef("icestaff", "Ice Shard", false, 1, 0.7, 0, 60, "shard", 430, "Half a heart and slows for 1.2 s")
            },
            new[]
            {
                new AbilityDef("slide", "Frost Slide", 10, "Slide 7 tiles, leaving ice behind you for 5 s"),
                new AbilityDef("wall", "Ice Wall", 20, "A wall 3 tiles high in front of you for 6 s; blocks shots"),
                new AbilityDef("freeze", "Deep Freeze", 30, "After a short glow: 1 heart and frozen 1.3 s around you"),
                new AbilityDef("armor", "Ice Armor", 20, "For 5 s the next 2 half hearts of damage are absorbed"),
                new AbilityDef("blizzard", "Blizzard", 30, "A storm where you aim for 4 s: slows, half a heart every second"),
                new AbilityDef("frostbite", "Frostbite", 0, "Passive: your close hits slow for 0.8 s", true)
            });

        public static readonly KindDef Fire = new("fire", "Fireman", "fire", 6, 14, 30, "Fewest hearts, hits hardest",
            new[]
            {
                new WeaponDef("flameaxe", "Flame Axe", true, 3, 0.8, 24, 190, "", 0, "Close: 1.5 hearts every 0.8 s"),
                new WeaponDef("firewand", "Fireball", false, 2, 1.1, 0, 120, "fireball", 300, "1 heart")
            },
            new[]
            {
                new AbilityDef("rocket", "Rocket Jump", 10, "A blast under your feet throws you high; 1 heart to anyone next to you"),
                new AbilityDef("pillar", "Flame Pillar", 20, "A column of fire where you aim for 3 s, half a heart every half second"),
                new AbilityDef("meteor", "Meteor", 30, "The marked spot is hit 1 s later: 2.5 hearts"),
                new AbilityDef("firedash", "Fire Dash", 10, "Dash 6 tiles; 1 heart to everyone you pass through"),
                new AbilityDef("inferno", "Inferno Ring", 30, "Fire around you for 3 s: half a heart every half second to anyone close"),
                new AbilityDef("kindling", "Kindling", 0, "Passive: your close hits set the target burning (half a heart more)", true)
            });

        public static readonly KindDef Thunder = new("thunder", "God of Thunder", "lightning", 9, 16, 32, "Most hearts, biggest hitbox, slow hammer",
            new[]
            {
                new WeaponDef("hammer", "Thunder Hammer", true, 3, 1.1, 26, 230, "", 0, "Close: 1.5 hearts every 1.1 s, big knockback"),
                new WeaponDef("stormrod", "Storm Bolt", false, 1, 0.7, 150, 40, "bolt", 0, "An instant short bolt, half a heart")
            },
            new[]
            {
                new AbilityDef("step", "Storm Step", 10, "Teleport 5 tiles toward your aim"),
                new AbilityDef("chain", "Chain Lightning", 20, "1 heart to the nearest enemy in sight, then half a heart to one more; both stunned 0.3 s"),
                new AbilityDef("judgement", "Judgement", 30, "The marked column is struck 0.9 s later: 2 hearts and a short stun"),
                new AbilityDef("clap", "Thunderclap", 20, "A blast around you: 1 heart and a big push"),
                new AbilityDef("static", "Static", 0, "Passive: every third hit you land stuns for 0.4 s", true)
            });

        public static readonly KindDef Vampire = new("vampire", "Vampire", "blood", 7, 14, 30, "Heals only by hitting",
            new[]
            {
                new WeaponDef("claws", "Claws", true, 2, 0.45, 20, 120, "", 0, "Close: 1 heart every 0.45 s, short reach"),
                new WeaponDef("bloodbolt", "Blood Bolt", false, 1, 0.65, 0, 60, "blood", 380, "Half a heart")
            },
            new[]
            {
                new AbilityDef("bloodpool", "Blood Pool", 20, "Throws blood where you aim: a pool for 5 s that slows enemies"),
                new AbilityDef("thirst", "Thirst", 20, "For 6 s every hit you land heals you half a heart"),
                new AbilityDef("bat", "Bat Form", 20, "Become a bat for 3 s: fly fast, cannot attack"),
                new AbilityDef("drain", "Life Drain", 30, "Drains the nearest enemy in sight: 1 heart taken, 1 heart healed")
            });

        public static readonly KindDef Patch = new("patch", "PATCH", "tech", 7, 14, 30, "Low damage; wins by moving people and space",
            new[]
            {
                new WeaponDef("pixelstick", "Pixel Stick", true, 3, 0.8, 24, 200, "", 0, "Close: 1.5 hearts, knocks back; a hit makes you quicker for 2 s"),
                new WeaponDef("bubble", "Bubble Blaster", false, 1, 1.2, 0, 30, "bubble", 190, "Slow bubbles: half a heart and a slow; on a wall they leave ice"),
                new WeaponDef("magnet", "Magnet Glove", true, 1, 1.5, 40, -220, "", 0, "Close pulse: half a heart and pulls the target to you"),
                new WeaponDef("glitch", "Glitch Launcher", false, 1, 2.0, 0, 60, "cube", 300, "A cube that becomes a small platform for 2 s where it lands")
            },
            new[]
            {
                new AbilityDef("rewind", "Ctrl+Z", 30, "Press to mark where you stand (for 10 s); press again to snap back there"),
                new AbilityDef("decoy", "Alt+Tab", 20, "A hologram of you keeps running your way for 2 s; one hit pops it"),
                new AbilityDef("window", "New Window", 30, "A platform of light where you aim, for 3 s; anyone can stand on it")
            });

        public static readonly KindDef[] All = { Cactus, Ice, Fire, Thunder, Vampire, Patch };

        public static KindDef Find(string? id) => All.FirstOrDefault(item => item.Id == id) ?? Cactus;
    }
}
