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

    // pen tile: clustered against the north wall, horizontally centred (offset by the floor pad)
    public const int PenX = 6 + (MonsterArena.ArenaMapAsset.W - 1) / 2;  // room-centre column
    public const int PenY = 6 + 2;
    // player spawn: south of the pen, centred
    public const int SpawnX = 6 + (MonsterArena.ArenaMapAsset.W - 1) / 2;
    public const int SpawnY = 6 + MonsterArena.ArenaMapAsset.H - 3;

    public ArenaManager(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    /// <summary>True when the local player is standing on a north exit tile (should warp out).</summary>
    public bool IsPlayerAtExit()
    {
        if (Game1.currentLocation?.Name != ArenaLocationName)
            return false;
        var t = Game1.player.Tile;
        int ty = (int)t.Y;
        int tx = (int)t.X;
        int door0 = 6 + MonsterArena.ArenaMapAsset.DoorX0;
        int door1 = 6 + MonsterArena.ArenaMapAsset.DoorX1;
        return ty <= 6 + 1 && (tx == door0 || tx == door1);
    }

    /// <summary>Warp the player out through the exit and clean up.</summary>
    public void ExitThroughDoor()
    {
        this.EndSession();
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
        Game1.warpFarmer(ArenaLocationName, SpawnX, SpawnY, 0); // stand south of the pen, facing it

        var toSpawn = this.Pending.ToList();
        this.Pending.Clear();
        this.SpawnMonsters(arena, toSpawn);
        this.monitor.Log($"Arena session started with {toSpawn.Count} monsters.", LogLevel.Info);
    }

    private GameLocation GetOrCreateArena()
    {
        // always rebuild so map/code changes take effect within a session
        var old = Game1.getLocationFromName(ArenaLocationName);
        if (old != null)
            Game1.locations.Remove(old);
        var arena = new GameLocation(ArenaMapAsset, ArenaLocationName);
        arena.map.LoadTileSheets(Game1.mapDisplayDevice);
        Game1.locations.Add(arena);
        return arena;
    }

    private void SpawnMonsters(GameLocation arena, List<MonsterCatalog.Entry> entries)
    {
        // pack every monster onto ONE tile against the north wall. They take knockback but are
        // pinned by Marlon's special wall on three sides, so you can keep juggling them forever.
        int i = 0;
        foreach (var entry in entries)
        {
            // a couple pixels of jitter only, so they truly pile onto one spot
            float jx = (i % 3 - 1) * 4f;
            float jy = (i % 2) * 4f;
            Vector2 pixel = new Vector2(PenX * 64f + jx, PenY * 64f + jy);
            Monster m = entry.Factory(pixel);
            this.Freeze(m);
            m.currentLocation = arena;
            arena.characters.Add(m);
            i++;
        }
    }

    /// <summary>Monster takes knockback (so hits feel good) but never moves on its own, never
    /// attacks, and never leaves the pen — Marlon's wall stops even ghosts/serpents.</summary>
    private void Freeze(Monster m)
    {
        m.stunTime.Value = int.MaxValue;   // no self-movement / attacks
        m.DamageToFarmer = 0;              // contact deals no damage
        m.focusedOnFarmers = false;
        // NOTE: Slipperiness left at the monster's natural value so hits still knock it back;
        // the surrounding walls cancel the slide so it stays piled on the pen tile.
    }

    /// <summary>Re-pin any monster that a hit knocked off the pen tile (safety net for the wall).</summary>
    public void RepinMonsters()
    {
        var arena = Game1.getLocationFromName(ArenaLocationName);
        if (arena == null)
            return;
        int i = 0;
        foreach (var c in arena.characters)
        {
            if (c is not Monster m || m.Health <= 0)
                continue;
            m.stunTime.Value = int.MaxValue;
            m.DamageToFarmer = 0;
            // if a knockback slid it away from the pen, snap it back onto the pile
            if (m.Tile.X < PenX - 2 || m.Tile.X > PenX + 2 || m.Tile.Y > PenY + 2)
            {
                float jx = (i % 3 - 1) * 4f;
                m.Position = new Vector2(PenX * 64f + jx, PenY * 64f);
                m.xVelocity = 0; m.yVelocity = 0;
            }
            i++;
        }
    }

    /// <summary>Send the player back and clean up the arena for next time.
    /// Drops (debris) are collected by the player before they walk out, so we only need to
    /// remove any leftover live monsters; dead-monster loot has already spawned as debris.</summary>
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
