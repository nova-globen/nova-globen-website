namespace Lek.Server.Noir
{
    /// <summary>A model placed on the map: its centre, quarter turns about Y, extra scale and whether it blocks.</summary>
    public sealed record NoirProp(string Model, double X, double Z, int Turns = 0, double Scale = 1, bool Solid = true);

    public sealed record NoirPickup(int Kind, double X, double Z);   // kind: weapon index, or -1 for painkillers

    /// <summary>
    /// "The Docks": one snowy night map, 50 x 40 metres, built from Quaternius' Toon Shooter Game Kit (CC0). Everything
    /// solid is an axis-aligned box [x0, z0, x1, z1, height]; walls are plain boxes drawn as brick, props are models the
    /// client fits onto their box. Model sizes are the kit's own (measured from the GLB files) times <see cref="Kit"/>,
    /// which makes the kit's 2.2-unit characters 1.8 m tall.
    /// </summary>
    public static class NoirMap
    {
        public const double Width = 50, Depth = 40, Kit = 0.82;
        public const double WallHeight = 4.2, RoomHeight = 3.4;

        // kit-unit sizes: width (x), height (y), depth (z)
        private static readonly Dictionary<string, (double W, double H, double D)> Sizes = new()
        {
            ["Shipping_Container"] = (4.36, 2.13, 2.09),
            ["Container_Small"] = (2.19, 2.13, 2.09),
            ["Crate"] = (0.79, 0.79, 0.79),
            ["Sack_Trench"] = (3.16, 1.19, 0.78),
            ["Sack_Trench_Small"] = (2.32, 0.93, 0.75),
            ["Barrier_Single"] = (1.89, 0.63, 0.66),
            ["Broken_Car"] = (2.64, 1.76, 5.49),
            ["Dumpster"] = (2.44, 2.03, 1.30),
            ["Pipes"] = (1.00, 0.96, 4.27),
            ["Cardboard_Boxes_3"] = (0.98, 0.95, 0.92),
            ["Cardboard_Boxes_4"] = (1.26, 2.25, 1.21),
            ["Sofa"] = (3.35, 1.32, 1.46),
            ["Tires"] = (1.76, 1.09, 1.14),
            ["Gas_Tank"] = (1.01, 1.39, 1.01),
            ["Water_Tank_Floor"] = (1.76, 1.91, 4.57),
            ["Exploding_Barrel"] = (0.78, 1.02, 0.78),
        };

        /// <summary>Brick walls: the yard's border and the warehouse in the middle, with four doorways.</summary>
        public static readonly double[][] Walls =
        {
            new[] { -1.0, -1.0, Width + 1, 0, WallHeight },
            new[] { -1.0, Depth, Width + 1, Depth + 1, WallHeight },
            new[] { -1.0, 0, 0, Depth, WallHeight },
            new[] { Width, 0, Width + 1, Depth, WallHeight },
            // warehouse, x 19..31, z 15..25
            new[] { 19.0, 24.4, 23.5, 25, RoomHeight }, new[] { 26.5, 24.4, 31, 25, RoomHeight },
            new[] { 19.0, 15, 23.5, 15.6, RoomHeight }, new[] { 26.5, 15, 31, 15.6, RoomHeight },
            new[] { 19.0, 15.6, 19.6, 18.5, RoomHeight }, new[] { 19.0, 21.5, 19.6, 24.4, RoomHeight },
            new[] { 30.4, 15.6, 31, 18.5, RoomHeight }, new[] { 30.4, 21.5, 31, 24.4, RoomHeight },
        };

        public static readonly NoirProp[] Props =
        {
            // shipping containers at the four corners, two standing guard beside the warehouse
            new("Shipping_Container", 8, 8, 0, 1.4), new("Shipping_Container", 8, 32, 0, 1.4),
            new("Shipping_Container", 42, 8, 0, 1.4), new("Shipping_Container", 42, 32, 0, 1.4),
            new("Shipping_Container", 13.5, 20, 1, 1.4), new("Shipping_Container", 36.5, 20, 1, 1.4),
            new("Container_Small", 4, 20, 0, 1.3), new("Container_Small", 46, 20, 0, 1.3),
            // low cover: sandbags, barriers, crates, a wreck
            new("Sack_Trench", 25, 8), new("Sack_Trench", 25, 32),
            new("Sack_Trench_Small", 8, 13.2), new("Sack_Trench_Small", 42, 26.8),
            new("Barrier_Single", 17, 10, 1, 1.3), new("Barrier_Single", 33, 30, 1, 1.3),
            new("Barrier_Single", 33, 10, 1, 1.3), new("Barrier_Single", 17, 30, 1, 1.3),
            new("Crate", 16.6, 26.4, 0, 1.5), new("Crate", 17.6, 26.4, 0, 1.5), new("Crate", 16.6, 27.4, 0, 1.5),
            new("Crate", 33.4, 13.6, 0, 1.5), new("Crate", 32.4, 13.6, 0, 1.5), new("Crate", 33.4, 12.6, 0, 1.5),
            new("Broken_Car", 25, 3.2, 1), new("Broken_Car", 25, 36.8, 1),
            new("Dumpster", 3.2, 2.2), new("Dumpster", 46.8, 37.8, 2),
            new("Pipes", 46.5, 9), new("Pipes", 3.5, 31),
            new("Tires", 37.6, 2.6), new("Tires", 12.4, 37.4, 2),
            new("Water_Tank_Floor", 2, 9, 0), new("Gas_Tank", 47.8, 30.5),
            // inside the warehouse
            new("Sofa", 25, 23.2, 2), new("Cardboard_Boxes_3", 21, 23), new("Cardboard_Boxes_4", 29.2, 16.8),
            new("Cardboard_Boxes_3", 29.2, 23.2), new("Crate", 20.8, 16.8, 0, 1.5),
        };

        /// <summary>Exploding barrels: block bullets, not players; a shot sets them off.</summary>
        public static readonly (double X, double Z)[] Barrels =
        {
            (11, 20), (39, 20), (25, 12.6), (25, 27.4), (6.2, 27.5), (43.8, 12.5)
        };

        public static readonly NoirPickup[] Pickups =
        {
            new(3, 25, 20),                       // the rifle, in the middle of the warehouse
            new(1, 8, 16), new(1, 42, 24),        // shotguns
            new(2, 16, 34), new(2, 34, 6),        // SMGs
            new(-1, 2.5, 37.5), new(-1, 47.5, 2.5), new(-1, 17, 20), new(-1, 33, 20)  // painkillers
        };

        public static readonly (double X, double Z)[] Spawns =
        {
            (4, 13), (4, 27), (46, 13), (46, 27), (15, 4), (35, 4), (15, 36), (35, 36),
            (22, 18), (28, 22), (9, 24), (41, 16)
        };

        /// <summary>Decoration the client draws without collisions: street lights (each a lamp), debris, trees and
        /// buildings outside the walls for a skyline.</summary>
        public static readonly NoirProp[] Decor =
        {
            new("Street_Light", 12, 0.6, 0, 1, false), new("Street_Light", 38, 0.6, 0, 1, false),
            new("Street_Light", 12, 39.4, 2, 1, false), new("Street_Light", 38, 39.4, 2, 1, false),
            new("Street_Light", 0.6, 20, 1, 1, false), new("Street_Light", 49.4, 20, 3, 1, false),
            new("Pallet", 10.5, 4.5, 0, 1, false), new("Pallet_Broken", 39, 35.5, 1, 1, false),
            new("Debris_Papers", 22, 10, 0, 1, false), new("Debris_Papers_2", 30, 29, 1, 1, false),
            new("Debris_Papers_3", 12, 30, 0, 1, false), new("Debris_Pile", 44, 4, 0, 1, false),
            new("Traffic_Cone", 21, 5.5, 0, 1, false), new("Traffic_Cone", 29, 34.5, 0, 1, false),
            new("Wood_Planks", 27.5, 18, 1, 1, false),
            new("Structure", -9, 8, 1, 1, false), new("Structure_3", -9, 30, 1, 1, false),
            new("Structure_2", 59, 10, 3, 1, false), new("Structure", 59, 31, 3, 1, false),
            new("Shipping_Container_Structure", 12, 47, 2, 1, false), new("Structure_2", 37, 48, 2, 1, false),
            new("Structure_3", 20, -9, 0, 1, false), new("Water_Tank", 40, -6, 0, 1.3, false),
            new("Tree_2", 4, -4, 0, 1, false), new("Tree_4", 30, -5, 0, 1, false), new("Tree", 54, -3, 0, 1, false),
            new("Tree_3", -4, 44, 0, 1, false), new("Tree_2", 26, 45, 0, 1, false), new("Tree", 54, 44, 0, 1, false),
        };

        public static readonly double[][] Solids = BuildSolids();

        /// <summary>The box a prop occupies, from the kit size, its scale and its turn.</summary>
        public static double[] BoxOf(NoirProp p)
        {
            var (w, h, d) = Sizes.TryGetValue(p.Model, out var s) ? s : (1.0, 1.0, 1.0);
            var k = Kit * p.Scale;
            var (hx, hz) = p.Turns % 2 == 0 ? (w * k / 2, d * k / 2) : (d * k / 2, w * k / 2);
            return new[] { p.X - hx, p.Z - hz, p.X + hx, p.Z + hz, Math.Round(h * k, 2) };
        }

        public static double[] BarrelBox((double X, double Z) b)
        {
            var (w, h, _) = Sizes["Exploding_Barrel"];
            var r = w * Kit / 2;
            return new[] { b.X - r, b.Z - r, b.X + r, b.Z + r, h * Kit };
        }

        private static double[][] BuildSolids()
        {
            var list = new List<double[]>(Walls);
            foreach (var p in Props) if (p.Solid) list.Add(BoxOf(p));
            return list.ToArray();
        }

        /// <summary>Everything the client needs to build the scene, sent once on join.</summary>
        public static object Describe() => new
        {
            w = Width,
            d = Depth,
            kit = Kit,
            walls = Walls,
            props = Props.Select(p => new { m = p.Model, x = p.X, z = p.Z, r = p.Turns, s = p.Scale, b = BoxOf(p) }).ToArray(),
            decor = Decor.Select(p => new { m = p.Model, x = p.X, z = p.Z, r = p.Turns, s = p.Scale }).ToArray(),
            barrels = Barrels.Select(b => new[] { b.X, b.Z }).ToArray(),
            pickups = Pickups.Select(p => new { k = p.Kind, x = p.X, z = p.Z }).ToArray(),
            solids = Solids,
        };
    }
}
