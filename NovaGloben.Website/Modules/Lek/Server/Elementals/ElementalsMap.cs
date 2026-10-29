namespace Lek.Server.Elementals
{
    /// <summary>
    /// A one-screen arena of 40 x 22 tiles of 16 px. Each map is written as its left half and mirrored, so both teams
    /// always get the same ground (a fairness rule). Cell codes:
    /// <c>#</c> ground, <c>=</c> one-way platform, <c>~</c> ice (solid, slippery), <c>M</c> mud (solid, slow, low jump),
    /// <c>P</c> magic pad (solid, its colour is its effect), <c>^</c> spikes (1 heart), <c>C</c> cactus (half a heart),
    /// <c>I</c> icicles (2 hearts when you land on them from high up), <c>L</c> lava (half a heart every half second
    /// while you burn), <c>o</c> a pot, <c>a</c> a spawn of the left team (mirrored to the right team), <c>.</c> air.
    /// </summary>
    public sealed class ElementalsMap
    {
        public const int Tile = 16;
        public const int Cols = 40;
        public const int Height = 22;

        public string Id { get; }
        public string Name { get; }
        public string Theme { get; }
        public string[] Rows { get; }
        public char[] Cells { get; }
        public List<(double X, double Y)>[] Spawns { get; } = { new(), new() };
        public List<(double X, double Y)> Pots { get; } = new();
        /// <summary>Contiguous magic pads, each a list of cell indices.</summary>
        public List<List<int>> Pads { get; } = new();

        private ElementalsMap(string id, string name, string theme, string[] half)
        {
            Id = id;
            Name = name;
            Theme = theme;
            Rows = half.Select(row =>
            {
                var mirrored = new string(row.Reverse().Select(c => c == 'a' ? 'b' : c).ToArray());
                return row + mirrored;
            }).ToArray();
            Cells = new char[Cols * Height];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Cols; x++)
                {
                    var c = Rows[y][x];
                    switch (c)
                    {
                        case 'a': Spawns[0].Add(Feet(x, y)); c = '.'; break;
                        case 'b': Spawns[1].Add(Feet(x, y)); c = '.'; break;
                        case 'o': Pots.Add(Feet(x, y)); c = '.'; break;
                    }
                    Cells[y * Cols + x] = c;
                }
            }
            // order the right team's spawns the same way as the left's (nearest the wall first)
            Spawns[1].Reverse();
            var seen = new bool[Cells.Length];
            for (var i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] != 'P' || seen[i]) continue;
                var pad = new List<int>();
                for (var j = i; j < Cells.Length && Cells[j] == 'P' && j / Cols == i / Cols; j++) { seen[j] = true; pad.Add(j); }
                Pads.Add(pad);
            }
        }

        private static (double X, double Y) Feet(int x, int y) => (x * Tile + Tile / 2.0, (y + 1) * Tile);

        public char At(int x, int y) => x < 0 || y < 0 || x >= Cols || y >= Height ? '#' : Cells[y * Cols + x];

        public static readonly ElementalsMap Desert = new("desert", "Desert", "desert", new[]
        {
            "####################",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#..............o....",
            "#............=======",
            "#...................",
            "#...................",
            "#....o..............",
            "#...######..........",
            "#...................",
            "#...................",
            "#..........=====....",
            "#...................",
            "#a.a.a.......C......",
            "########.C.#####....",
            "################MMMM",
            "####################",
            "####################"
        });

        public static readonly ElementalsMap Volcano = new("volcano", "Volcano", "volcano", new[]
        {
            "####################",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#.................o.",
            "#...=====......=====",
            "#...................",
            "#...................",
            "#...................",
            "#.........====......",
            "#..o................",
            "#.####..............",
            "#...................",
            "#.............======",
            "#a.a.a..............",
            "#######.............",
            "#######...###LLLLLLL",
            "####################",
            "####################"
        });

        public static readonly ElementalsMap Antarctica = new("antarctica", "South Pole", "antarctica", new[]
        {
            "####################",
            "#...................",
            "#...................",
            "#..................o",
            "#.................~~",
            "#...................",
            "#..............###..",
            "#...................",
            "#...................",
            "#...........~~~.....",
            "#...................",
            "#...................",
            "#........###........",
            "#...................",
            "#...................",
            "#.....~~~...........",
            "#...................",
            "#...................",
            "#a.a.a......IIIIIIII",
            "######~~~~~~########",
            "####################",
            "####################"
        });

        public static readonly ElementalsMap Castle = new("castle", "Castle", "castle", new[]
        {
            "####################",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...................",
            "#...o...............",
            "#..======...........",
            "#...................",
            "#...................",
            "#...........o.......",
            "#..........#######PP",
            "#...................",
            "#...................",
            "#...................",
            "#.....=====.........",
            "#...................",
            "#...................",
            "#a.a.a..........^^^^",
            "#########PP#########",
            "####################",
            "####################"
        });

        public static readonly ElementalsMap[] All = { Desert, Volcano, Antarctica, Castle };

        public static ElementalsMap? Find(string? id) => All.FirstOrDefault(item => item.Id == id);
    }
}
