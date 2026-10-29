namespace Lek.Server.Spark
{
    /// <summary>The arena: 32 x 18 tiles of 16 px. '#' is a wall (cover), '.' floor, digits are spawn points.</summary>
    public sealed class SparkMap
    {
        public const int Tile = 16;
        public const int Width = 32;
        public const int Height = 18;

        public static readonly SparkMap Arena = new("arena", "The Arena", new[]
        {
            "################################",
            "#1............................2#",
            "#..............................#",
            "#...##....#..........#....##...#",
            "#...##....#..........#....##...#",
            "#..............................#",
            "#.......###..........###.......#",
            "#..............####............#",
            "#....#.........#..#.........#..#",
            "#....#.........#..#.........#..#",
            "#..............####............#",
            "#.......###..........###.......#",
            "#..............................#",
            "#...##....#..........#....##...#",
            "#...##....#..........#....##...#",
            "#..............................#",
            "#3............................4#",
            "################################",
        });

        public string Id { get; }
        public string Name { get; }
        public string[] Rows { get; }
        public (float X, float Y)[] Spawns { get; }

        private SparkMap(string id, string name, string[] rows)
        {
            Id = id;
            Name = name;
            Rows = rows;
            var list = new List<(int Order, float X, float Y)>();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (char.IsDigit(rows[y][x])) list.Add((rows[y][x] - '0', x * Tile + Tile / 2f, y * Tile + Tile / 2f));
                }
            }
            // mid-edge spawns too, so six players start apart
            list.Add((5, 15 * Tile + 8, 2 * Tile + 8));
            list.Add((6, 15 * Tile + 8, 15 * Tile + 8));
            Spawns = list.OrderBy(item => item.Order).Select(item => (item.X, item.Y)).ToArray();
        }

        public bool IsWall(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) return true;
            return Rows[ty][tx] == '#';
        }

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

        /// <summary>True when a straight line between two points crosses no wall (sampled every 4 px).</summary>
        public bool Clear(float ax, float ay, float bx, float by)
        {
            var dx = bx - ax; var dy = by - ay;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            var steps = Math.Max(1, (int)(len / 4));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                if (IsWall((int)((ax + dx * t) / Tile), (int)((ay + dy * t) / Tile))) return false;
            }
            return true;
        }
    }
}
