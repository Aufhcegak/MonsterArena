using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace MonsterArena;

/// <summary>Provides the arena map via SMAPI's content pipeline.
/// Layout: a small room, walls on north/west/east, open at the south (player enters from there).
/// Monsters are clustered in the north-center so the player can hit them all from the opening.</summary>
public class ArenaMapAsset
{
    public const int W = 9, H = 7;

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

        var walls = new TileSheet("walls_and_floors", map, "Maps/walls_and_floors", new Size(32, 64), new Size(16, 16));
        map.AddTileSheet(walls);

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

        // floor everywhere
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                back.Tiles[x, y] = new StaticTile(back, walls, BlendMode.Alpha, 0);

        // three-sided pen: north row (y=0), west col (x=0), east col (x=W-1)
        for (int x = 0; x < W; x++)
            buildings.Tiles[x, 0] = new StaticTile(buildings, walls, BlendMode.Alpha, 1);
        for (int y = 0; y < H; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, walls, BlendMode.Alpha, 1);
            buildings.Tiles[W - 1, y] = new StaticTile(buildings, walls, BlendMode.Alpha, 1);
        }

        return map;
    }
}
