using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace MonsterArena;

/// <summary>Provides the arena map via SMAPI's content pipeline.
/// Every tile index is copied from the vanilla FarmHouse1 room (verified from Maps/FarmHouse1.xnb),
/// so the walls/floor render exactly like a real room. The room is small and fully visible at once:
/// the player spawns by the SOUTH door (walk into it = exit), the monster pen is centred just
/// under the north wall. A floor margin surrounds the room so the camera never shows void.</summary>
public class ArenaMapAsset
{
    // the enclosed room interior
    public const int W = 13, H = 8;
    // floor margin around the room so the camera never shows void when it pans
    public const int Pad = 5;
    public const int FullW = W + Pad * 2, FullH = H + Pad * 2;   // 23 x 18

    // room origin inside the padded map
    public const int X0 = Pad, Y0 = Pad;
    public const int X1 = Pad + W - 1, Y1 = Pad + H - 1;

    // monster pen: centred column, just under the north wall
    public const int PenX = X0 + W / 2;   // 11
    public const int PenY = Y0 + 1;       // 6

    // south exit door: 2-tile gap in the south wall, centred
    public const int DoorX0 = X0 + W / 2;      // 11
    public const int DoorX1 = DoorX0 + 1;      // 12
    public const int DoorY = Y1;               // south wall row (12)

    // player spawn: just inside the door
    public const int SpawnX = X0 + W / 2;      // 11
    public const int SpawnY = Y1 - 1;          // 11

    // townInterior sheet ("indoor" in FarmHouse1) — wall body tiles
    private const int WallTopL = 1, WallTopM = 2, WallTopR = 3;  // north wall top edge
    private const int WallL = 64, WallR = 68;                    // west / east wall columns
    private const int WallBaseL = 160, WallBaseR = 130;          // wall bottom-corner trim

    // walls_and_floors sheet — wood floor (336/337) + baseboard (32) that seals the wall bottom
    private const int FloorA = 336, FloorB = 337;
    private const int FloorA2 = 352, FloorB2 = 353;              // staggered row for a nicer seam
    private const int Baseboard = 32;

    public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ArenaManager.ArenaMapAsset))
            e.LoadFrom(this.BuildMap, AssetLoadPriority.Exclusive);
    }

    public Map BuildMapPublic() => this.BuildMap();

    private Map BuildMap()
    {
        var map = new Map();
        map.Id = "xiepe.MonsterArena.Arena";
        map.Description = "Monster Arena";
        map.Properties["Music"] = new PropertyValue("MarlonsTheme");

        var interior = new TileSheet("1", map, "Maps/townInterior", new Size(32, 68), new Size(16, 16));
        var floors = new TileSheet("2", map, "Maps/walls_and_floors", new Size(16, 35), new Size(16, 16));
        map.AddTileSheet(interior);
        map.AddTileSheet(floors);

        var layerSize = new Size(FullW, FullH);
        var back = new Layer("Back", map, layerSize, new Size(16, 16));
        var buildings = new Layer("Buildings", map, layerSize, new Size(16, 16));
        var front = new Layer("Front", map, layerSize, new Size(16, 16));
        var paths = new Layer("Paths", map, layerSize, new Size(16, 16));
        var alwaysFront = new Layer("AlwaysFront", map, layerSize, new Size(16, 16));
        map.AddLayer(back);
        map.AddLayer(buildings);
        map.AddLayer(front);
        map.AddLayer(paths);
        map.AddLayer(alwaysFront);

        // --- floor fills the whole padded area so the camera never shows void ---
        for (int y = 0; y < FullH; y++)
        {
            // alternate the two floor variants; every 4th row uses the staggered set like FarmHouse1
            bool stagger = (y % 4) == 3;
            int a = stagger ? FloorA2 : FloorA;
            int b = stagger ? FloorB2 : FloorB;
            for (int x = 0; x < FullW; x++)
                back.Tiles[x, y] = new StaticTile(back, floors, BlendMode.Alpha, ((x + y) % 2 == 0) ? a : b);
        }

        // --- north wall (y=Y0): top edge ---
        for (int x = X0; x <= X1; x++)
        {
            int t = (x == X0) ? WallTopL : (x == X1) ? WallTopR : WallTopM;
            buildings.Tiles[x, Y0] = new StaticTile(buildings, interior, BlendMode.Alpha, t);
        }

        // --- west & east walls (y=Y0+1..Y1-1) ---
        for (int y = Y0 + 1; y < Y1; y++)
        {
            buildings.Tiles[X0, y] = new StaticTile(buildings, interior, BlendMode.Alpha, WallL);
            buildings.Tiles[X1, y] = new StaticTile(buildings, interior, BlendMode.Alpha, WallR);
        }

        // --- south wall (y=Y1) with a 2-tile exit gap in the middle ---
        for (int x = X0; x <= X1; x++)
        {
            if (x == DoorX0 || x == DoorX1)
                continue; // the exit opening
            int t = (x == X0) ? WallBaseL : (x == X1) ? WallBaseR : WallTopM;
            buildings.Tiles[x, Y1] = new StaticTile(buildings, interior, BlendMode.Alpha, t);
        }

        // --- baseboard (walls_and_floors 32) seals the wall bottom so monsters can't slip through ---
        // along the north wall and down the west/east walls
        for (int bx = X0 + 1; bx < X1; bx++)
            buildings.Tiles[bx, Y0 + 1] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
        for (int by = Y0 + 2; by < Y1; by++)
        {
            buildings.Tiles[X0 + 1, by] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
            buildings.Tiles[X1 - 1, by] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
        }

        // --- HARD BORDER: isTilePassable() treats OUT-OF-BOUNDS tiles as passable (null tile),
        // so the player could otherwise walk off the small map into the void ("穿墙"). Fill the
        // entire outer ring of the Buildings layer with a blocking tile so you can never leave.
        for (int x = 0; x < FullW; x++)
        {
            buildings.Tiles[x, 0] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
            buildings.Tiles[x, FullH - 1] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
        }
        for (int y = 0; y < FullH; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
            buildings.Tiles[FullW - 1, y] = new StaticTile(buildings, floors, BlendMode.Alpha, Baseboard);
        }

        return map;
    }
}
