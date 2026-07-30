using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace MonsterArena;

/// <summary>Provides the arena map via SMAPI's content pipeline.
/// A closed room using the vanilla townInterior tile sheet (same as the Adventure Guild),
/// walls on north/west/east, floor filling the whole room. Monsters cluster at the north wall.</summary>
public class ArenaMapAsset
{
    // big enough to fill the viewport so there's no black void around it
    public const int W = 20, H = 13;

    // tile indices in Maps/townInterior (32x68 sheet, 16px tiles), copied from Maps/AdventureGuild
    private const int Floor = 330;      // plain wood floor
    private const int WallTopL = 9, WallTopM = 10, WallTopR = 11; // north wall top edge
    private const int WallL = 64, WallR = 68;                     // west / east wall columns
    private const int WallMid = 60;      // wall body filler (used under the top edge)

    public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ArenaManager.ArenaMapAsset))
            e.LoadFrom(this.BuildMap, AssetLoadPriority.Exclusive);
    }

    private Map BuildMap()
    {
        var map = new Map();
        map.Id = ArenaManager.ArenaLocationName;
        map.Description = "Monster Arena";
        map.Properties["Music"] = new PropertyValue("MarlonsTheme");

        var interior = new TileSheet("1", map, "Maps/townInterior", new Size(32, 68), new Size(16, 16));
        map.AddTileSheet(interior);

        var back = new Layer("Back", map, new Size(W, H), new Size(16, 16));
        var buildings = new Layer("Buildings", map, new Size(W, H), new Size(16, 16));
        var front = new Layer("Front", map, new Size(W, H), new Size(16, 16));
        var paths = new Layer("Paths", map, new Size(W, H), new Size(16, 16));
        var alwaysFront = new Layer("AlwaysFront", map, new Size(W, H), new Size(16, 16));
        map.AddLayer(back);
        map.AddLayer(buildings);
        map.AddLayer(front);
        map.AddLayer(paths);
        map.AddLayer(alwaysFront);

        // floor fills the entire room
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                back.Tiles[x, y] = new StaticTile(back, interior, BlendMode.Alpha, Floor);

        // north wall: top edge row (y=0) + body row (y=1)
        buildings.Tiles[0, 0] = new StaticTile(buildings, interior, BlendMode.Alpha, WallTopL);
        for (int x = 1; x < W - 1; x++)
            buildings.Tiles[x, 0] = new StaticTile(buildings, interior, BlendMode.Alpha, WallTopM);
        buildings.Tiles[W - 1, 0] = new StaticTile(buildings, interior, BlendMode.Alpha, WallTopR);
        for (int x = 0; x < W; x++)
            buildings.Tiles[x, 1] = new StaticTile(buildings, interior, BlendMode.Alpha, WallMid);

        // west & east walls (leave the south open for the player to stand in)
        for (int y = 2; y < H; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, interior, BlendMode.Alpha, WallL);
            buildings.Tiles[W - 1, y] = new StaticTile(buildings, interior, BlendMode.Alpha, WallR);
        }

        return map;
    }
}
