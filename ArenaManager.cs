using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;

namespace MonsterArena;

/// <summary>Owns the arena GameLocation: builds it, warps the player in, spawns purchased
/// monsters clustered in the pen, and warps the player back out.</summary>
public class ArenaManager
{
    public const string ArenaLocationName = "xiepe.MonsterArena.Arena";
    public const string ArenaMapAsset = "Maps/" + ArenaLocationName;

    private readonly IModHelper helper;
    private readonly IMonitor monitor;

    /// <summary>The monsters the player has bought but not yet been warped in to fight.</summary>
    public readonly List<MonsterCatalog.Entry> Pending = new();
    private string returnLocation = "AdventureGuild";
    private Vector2 returnTile = new Vector2(4, 7);

    public bool SessionActive { get; private set; }

    public ArenaManager(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    public void QueuePurchase(MonsterCatalog.Entry entry, int count)
    {
        for (int i = 0; i < count; i++)
            this.Pending.Add(entry);
    }

    public bool HasPending => this.Pending.Count > 0;

    /// <summary>Group pending monsters by entry -> count, for the summary message.</summary>
    public IEnumerable<KeyValuePair<MonsterCatalog.Entry, int>> PendingSummary()
        => this.Pending.GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Warp the player into the arena and spawn all pending monsters clustered in the pen.</summary>
    public void BeginSession()
    {
        if (this.Pending.Count == 0)
            return;

        if (Game1.currentLocation != null)
        {
            this.returnLocation = Game1.currentLocation.Name;
            this.returnTile = Game1.player.Tile;
        }

        GameLocation arena = this.GetOrCreateArena();
        this.SessionActive = true;
        if (Game1.activeClickableMenu is StardewValley.Menus.DialogueBox)
            Game1.activeClickableMenu.exitThisMenu();
        Game1.warpFarmer(ArenaLocationName, 4, 5, 0); // stand in the south opening, facing the pen

        var toSpawn = this.Pending.ToList();
        this.Pending.Clear();
        this.SpawnMonsters(arena, toSpawn);
        this.monitor.Log($"Arena session started with {toSpawn.Count} monsters.", LogLevel.Info);
    }

    private GameLocation GetOrCreateArena()
    {
        var existing = Game1.getLocationFromName(ArenaLocationName);
        if (existing != null)
            return existing;
        var arena = new GameLocation(ArenaMapAsset, ArenaLocationName);
        arena.map.LoadTileSheets(Game1.mapDisplayDevice);
        Game1.locations.Add(arena);
        return arena;
    }

    private void SpawnMonsters(GameLocation arena, List<MonsterCatalog.Entry> entries)
    {
        // cluster all monsters against the north wall, packed in 2 tight rows so the player
        // standing in the south opening can reach and see every one of them
        int cols = 5;
        int startX = 2, startY = 1;
        int i = 0;
        foreach (var entry in entries)
        {
            int col = i % cols;
            int row = i / cols;
            // slight pixel jitter so they overlap into one satisfying clump instead of a rigid grid
            float jx = (i % 2 == 0) ? 0f : 24f;
            Vector2 pixel = new Vector2((startX + col) * 64f + jx, (startY + row) * 64f);
            Monster m = entry.Factory(pixel);
            this.Freeze(m);
            m.currentLocation = arena;
            arena.characters.Add(m);
            i++;
        }
    }

    /// <summary>Make a monster stand still, never attack, take no knockback, but still be killable.</summary>
    private void Freeze(Monster m)
    {
        m.stunTime.Value = int.MaxValue;
        m.DamageToFarmer = 0;
        m.Slipperiness = -1;
        m.focusedOnFarmers = false;
    }

    /// <summary>Send the player back and clean up the arena for next time.</summary>
    public void EndSession()
    {
        if (!this.SessionActive)
            return;
        this.SessionActive = false;

        var arena = Game1.getLocationFromName(ArenaLocationName);
        if (arena != null)
            arena.characters.RemoveWhere(c => c is Monster);

        // close any lingering menu before warping back
        if (Game1.activeClickableMenu != null)
            Game1.exitActiveMenu();
        Game1.warpFarmer(this.returnLocation, (int)this.returnTile.X, (int)this.returnTile.Y, 2);
        this.monitor.Log("Arena session ended.", LogLevel.Info);
    }

    public int RemainingMonsters()
    {
        if (!this.SessionActive)
            return 0;
        var arena = Game1.getLocationFromName(ArenaLocationName);
        if (arena == null)
            return 0;
        return arena.characters.Count(c => c is Monster m && m.Health > 0);
    }
}
