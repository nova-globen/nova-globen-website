namespace Lek.Server.Fuse
{
    /// <summary>
    /// A map seen from above: 32 x 18 tiles of 16 px. '#' is a wall (outer walls, room walls, furniture), '.' is
    /// floor, digits are spawn points. The browser draws the same rows. Every floor tile must be reachable from every
    /// other (tools/check-fuse-maps.mjs verifies it).
    /// </summary>
    public sealed class FuseMap
    {
        public const int Tile = 16;
        public const int Width = 32;
        public const int Height = 18;

        public static readonly FuseMap House = new("house", "The House", new[]
        {
            "################################",
            "#1.............#..............2#",
            "#..............#...............#",
            "#.....##.......#....####.......#",
            "#.....##.......#...............#",
            "#..............#...............#",
            "#......................#.......#",
            "#..............#.......#.......#",
            "#..............#...............#",
            "######..###########..###########",
            "#.......#.......#..............#",
            "#.......#.......#..............#",
            "#..##...#..##...#......##......#",
            "#.......#.......#......##......#",
            "#.......#...........#..........#",
            "#.......#...........#..........#",
            "#3.............#..............4#",
            "################################",
        });

        public static readonly FuseMap Office = new("office", "The Office", new[]
        {
            "################################",
            "#1......#..............#......2#",
            "#.......#..##....##....#.......#",
            "#..##...#..##....##....#..##...#",
            "#..##...................##.....#",
            "#.......#..##....##....#.......#",
            "#.......#..##....##....#.......#",
            "####.####..............####.####",
            "#..............................#",
            "#...####......####......####...#",
            "#..............................#",
            "####.####..............####.####",
            "#.......#..##....##....#.......#",
            "#.......#..##....##....#.......#",
            "#..##...................##.....#",
            "#..##...#..##....##....#..##...#",
            "#3......#..............#......4#",
            "################################",
        });

        public static readonly FuseMap Yard = new("yard", "The Yard", new[]
        {
            "################################",
            "#1............................2#",
            "#..............................#",
            "#...###......####......###.....#",
            "#...#..........##........#.....#",
            "#...#....##..........##..#.....#",
            "#........##..........##........#",
            "#..............####............#",
            "#......#.......####.......#....#",
            "#......#.......####.......#....#",
            "#..............####............#",
            "#........##..........##........#",
            "#...#....##..........##..#.....#",
            "#...#..........##........#.....#",
            "#...###......####......###.....#",
            "#..............................#",
            "#3............................4#",
            "################################",
        });

        public static readonly FuseMap[] All = { House, Office, Yard };

        public static FuseMap Find(string? id) => All.FirstOrDefault(item => item.Id == id) ?? House;

        public string Id { get; }
        public string Name { get; }
        public string[] Rows { get; }
        public (float X, float Y)[] Spawns { get; }
        private readonly List<(float X, float Y)> _itemSpots;

        private FuseMap(string id, string name, string[] rows)
        {
            Id = id;
            Name = name;
            Rows = rows;
            Spawns = FindSpawns(rows);
            _itemSpots = FindItemSpots(rows);
        }

        public bool IsWall(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return true;
            return Rows[ty][tx] == '#';
        }

        /// <summary>True when a circle of radius r centred at (x, y) overlaps a wall tile.</summary>
        public bool Blocked(float x, float y, float r)
        {
            var x0 = (int)MathF.Floor((x - r) / Tile);
            var x1 = (int)MathF.Floor((x + r) / Tile);
            var y0 = (int)MathF.Floor((y - r) / Tile);
            var y1 = (int)MathF.Floor((y + r) / Tile);
            for (var ty = y0; ty <= y1; ty++)
            {
                for (var tx = x0; tx <= x1; tx++)
                {
                    if (IsWall(tx, ty)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Breadth-first walking distance (in tiles) from the tile holding (x, y) to every floor tile; walls and
        /// unreachable tiles are int.MaxValue. Diagonal steps are not taken, so a path never cuts a corner.
        /// </summary>
        public int[] DistanceField(float x, float y)
        {
            var field = new int[Width * Height];
            Array.Fill(field, int.MaxValue);
            var sx = Math.Clamp((int)(x / Tile), 0, Width - 1);
            var sy = Math.Clamp((int)(y / Tile), 0, Height - 1);
            if (IsWall(sx, sy)) return field;
            var queue = new Queue<(int X, int Y)>();
            field[sy * Width + sx] = 0;
            queue.Enqueue((sx, sy));
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                var d = field[cy * Width + cx] + 1;
                foreach (var (dx, dy) in Steps)
                {
                    var nx = cx + dx; var ny = cy + dy;
                    if (IsWall(nx, ny)) continue;
                    var i = ny * Width + nx;
                    if (field[i] != int.MaxValue) continue;
                    field[i] = d;
                    queue.Enqueue((nx, ny));
                }
            }
            return field;
        }

        public static readonly (int X, int Y)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        public static int DistanceAt(int[] field, float x, float y)
        {
            var tx = (int)(x / Tile); var ty = (int)(y / Tile);
            if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return int.MaxValue;
            return field[ty * Width + tx];
        }

        /// <summary>A random floor tile centre with free space around it, for an item box.</summary>
        public (float X, float Y) RandomItemSpot(Random random) => _itemSpots[random.Next(_itemSpots.Count)];

        private static (float, float)[] FindSpawns(string[] rows)
        {
            var list = new List<(int Order, float X, float Y)>();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var c = rows[y][x];
                    if (char.IsDigit(c))
                    {
                        list.Add((c - '0', x * Tile + Tile / 2f, y * Tile + Tile / 2f));
                    }
                }
            }
            // extra spawn points spread over the floor, so up to ten players start apart
            var extras = new[] { (7, 5), (24, 6), (4, 13), (12, 14), (24, 11), (11, 2) };
            var order = 5;
            foreach (var (tx, ty) in extras)
            {
                var spot = NearestFloor(rows, tx, ty);
                list.Add((order++, spot.X * Tile + Tile / 2f, spot.Y * Tile + Tile / 2f));
            }
            return list.OrderBy(item => item.Order).Select(item => (item.X, item.Y)).ToArray();
        }

        private static (int X, int Y) NearestFloor(string[] rows, int tx, int ty)
        {
            for (var r = 0; r < 6; r++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    for (var dx = -r; dx <= r; dx++)
                    {
                        var x = tx + dx; var y = ty + dy;
                        if (x > 0 && y > 0 && x < Width - 1 && y < Height - 1 && rows[y][x] == '.') return (x, y);
                    }
                }
            }
            return (1, 1);
        }

        private static List<(float X, float Y)> FindItemSpots(string[] rows)
        {
            var spots = new List<(float, float)>();
            for (var y = 1; y < Height - 1; y++)
            {
                for (var x = 1; x < Width - 1; x++)
                {
                    if (rows[y][x] != '.') continue;
                    // free on all four sides, so a box never sits in a doorway or against a wall
                    if (rows[y][x - 1] == '#' || rows[y][x + 1] == '#' || rows[y - 1][x] == '#' || rows[y + 1][x] == '#') continue;
                    spots.Add((x * Tile + Tile / 2f, y * Tile + Tile / 2f));
                }
            }
            return spots;
        }
    }
}
