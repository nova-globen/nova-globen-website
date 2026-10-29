namespace Lek.Server.RelicRush
{
    /// <summary>
    /// A temple labyrinth of 40 x 22 tiles of 16 px, made from a 13 x 7 grid of rooms (corridors two tiles wide, walls one
    /// tile). The left half is a random maze, mirrored to the right so both teams get the same ground, then opened up:
    /// most dead ends get a second exit and a few extra doors make loops, so there is never only one way to a shrine.
    /// </summary>
    public sealed class RelicMaze
    {
        public const int Tile = 16, Cols = 40, Height = 22, CellsX = 13, CellsY = 7;

        public int Seed { get; }
        public char[] Cells { get; } = new char[Cols * Height];

        public RelicMaze(int seed)
        {
            Seed = seed;
            var random = new Random(seed);
            Array.Fill(Cells, '#');
            for (var cy = 0; cy < CellsY; cy++)
                for (var cx = 0; cx < CellsX; cx++)
                    Carve(cx * 3 + 1, cy * 3 + 1, 2, 2);

            // a spanning tree over the left half (columns 0..6, the centre column included), then mirrored
            var half = CellsX / 2;
            var seen = new bool[CellsX, CellsY];
            var stack = new Stack<(int X, int Y)>();
            stack.Push((random.Next(half + 1), random.Next(CellsY)));
            seen[stack.Peek().X, stack.Peek().Y] = true;
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (stack.Count > 0)
            {
                var (x, y) = stack.Peek();
                var options = dirs.Select(d => (X: x + d.Item1, Y: y + d.Item2))
                    .Where(n => n.X >= 0 && n.X <= half && n.Y >= 0 && n.Y < CellsY && !seen[n.X, n.Y]).ToList();
                if (options.Count == 0) { stack.Pop(); continue; }
                var next = options[random.Next(options.Count)];
                Open(x, y, next.X, next.Y);
                seen[next.X, next.Y] = true;
                stack.Push(next);
            }
            // braid: most dead ends get another way out, and a few random doors make loops
            for (var cy = 0; cy < CellsY; cy++)
                for (var cx = 0; cx <= half; cx++)
                {
                    var exits = dirs.Count(d => IsOpen(cx, cy, cx + d.Item1, cy + d.Item2));
                    if ((exits <= 1 && random.NextDouble() < 0.75) || random.NextDouble() < 0.12)
                    {
                        var closed = dirs.Select(d => (X: cx + d.Item1, Y: cy + d.Item2))
                            .Where(n => n.X >= 0 && n.X <= half && n.Y >= 0 && n.Y < CellsY && !IsOpen(cx, cy, n.X, n.Y)).ToList();
                        if (closed.Count > 0) { var n = closed[random.Next(closed.Count)]; Open(cx, cy, n.X, n.Y); }
                    }
                }
            // mirror the left half onto the right
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Cols / 2; x++)
                    Cells[y * Cols + (Cols - 1 - x)] = Cells[y * Cols + x];
        }

        private void Carve(int x, int y, int w, int h)
        {
            for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                    if (xx > 0 && yy > 0 && xx < Cols - 1 && yy < Height - 1) Cells[yy * Cols + xx] = '.';
        }

        /// <summary>Opens the wall between two neighbouring rooms.</summary>
        private void Open(int ax, int ay, int bx, int by)
        {
            if (ax == bx) Carve(ax * 3 + 1, Math.Max(ay, by) * 3, 2, 1);
            else Carve(Math.Max(ax, bx) * 3, ay * 3 + 1, 1, 2);
        }

        private bool IsOpen(int ax, int ay, int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= CellsX || by >= CellsY) return false;
            return ax == bx ? Cells[Math.Max(ay, by) * 3 * Cols + ax * 3 + 1] == '.' : Cells[(ay * 3 + 1) * Cols + Math.Max(ax, bx) * 3] == '.';
        }

        public bool Wall(int x, int y) => x < 0 || y < 0 || x >= Cols || y >= Height || Cells[y * Cols + x] == '#';

        public string[] Rows => Enumerable.Range(0, Height).Select(y => new string(Cells, y * Cols, Cols)).ToArray();

        /// <summary>The middle of a room, in pixels.</summary>
        public static (double X, double Y) RoomCenter(int cx, int cy) => ((cx * 3 + 2) * Tile, (cy * 3 + 2) * Tile);

        /// <summary>Walking distance in tiles from one tile to every tile (BFS), -1 for walls and unreachable tiles.</summary>
        public int[] DistanceFrom(int tx, int ty)
        {
            var dist = new int[Cols * Height];
            Array.Fill(dist, -1);
            if (Wall(tx, ty)) return dist;
            var queue = new Queue<int>();
            dist[ty * Cols + tx] = 0;
            queue.Enqueue(ty * Cols + tx);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % Cols, y = i / Cols;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (Wall(nx, ny)) continue;
                    var j = ny * Cols + nx;
                    if (dist[j] >= 0) continue;
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
            return dist;
        }
    }
}
