using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;

namespace MonsterArena;

/// <summary>Owns the arena GameLocation: builds it, warps the player in, spawns purchased
/// monsters clustered in the pen, refunds the un-killed share if the player leaves early,
/// and warps the player back out.</summary>
public class ArenaManager
{
    public const string ArenaLocationName = "xiepe.MonsterArena.Arena";
    public const string ArenaMapAsset = "Maps/" + ArenaLocationName;

    /// <summary>Fraction of the surviving monsters' price refunded when the player leaves early.</summary>
    public const double RefundRatio = 0.6;

    private readonly IModHelper helper;
    private readonly IMonitor monitor;

    /// <summary>The monsters the player has bought but not yet been warped in to fight.</summary>
    public readonly List<MonsterCatalog.Entry> Pending = new();
    /// <summary>The monsters spawned for the current session (alive or already slain).</summary>
    private readonly List<MonsterCatalog.Entry> sessionBought = new();
    private string returnLocation = "AdventureGuild";
    private Vector2 returnTile = new Vector2(4, 7);

    public bool SessionActive { get; private set; }

    public ArenaManager(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    // map-constant shortcuts
    private static int PenX => MonsterArena.ArenaMapAsset.PenX;
    private static int PenY => MonsterArena.ArenaMapAsset.PenY;
    private static int SpawnX => MonsterArena.ArenaMapAsset.SpawnX;
    private static int SpawnY => MonsterArena.ArenaMapAsset.SpawnY;

    /// <summary>True when the local player is standing on the south exit door tiles.</summary>
    public bool IsPlayerAtExit()
    {
        if (Game1.currentLocation?.Name != ArenaLocationName)
            return false;
        var t = Game1.player.Tile;
        int tx = (int)t.X, ty = (int)t.Y;
        int dx0 = MonsterArena.ArenaMapAsset.DoorX0, dx1 = MonsterArena.ArenaMapAsset.DoorX1;
        int dy = MonsterArena.ArenaMapAsset.DoorY;
        return ty >= dy && (tx == dx0 || tx == dx1);
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
        Game1.warpFarmer(ArenaLocationName, SpawnX, SpawnY, 0); // by the south door, facing the pen

        this.sessionBought.Clear();
        this.sessionBought.AddRange(this.Pending);
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
            if (m.Tile.X < PenX - 2 || m.Tile.X > PenX + 2 || m.Tile.Y > PenY + 2)
            {
                float jx = (i % 3 - 1) * 4f;
                m.Position = new Vector2(PenX * 64f + jx, PenY * 64f);
                m.xVelocity = 0; m.yVelocity = 0;
            }
            i++;
        }
    }

    /// <summary>Count of live monsters left in the pen.</summary>
    public int RemainingMonsters()
    {
        if (!this.SessionActive)
            return 0;
        var arena = Game1.getLocationFromName(ArenaLocationName);
        if (arena == null)
            return 0;
        return arena.characters.Count(c => c is Monster m && m.Health > 0);
    }

    /// <summary>Refund for leaving early: RefundRatio of the still-alive monsters' total price.</summary>
    public int ComputeRefund()
    {
        int alive = this.RemainingMonsters();
        if (alive <= 0)
            return 0;
        // refund by matching surviving count against the cheapest entries first is unfair; instead
        // refund RefundRatio of the average price of what was bought this session, per survivor.
        if (this.sessionBought.Count == 0)
            return 0;
        double avg = this.sessionBought.Average(e => e.Price);
        return (int)(avg * alive * RefundRatio);
    }

    /// <summary>Leave the arena. If refundGold > 0, give the player that gold (leaving early).</summary>
    public void LeaveArena(int refundGold)
    {
        if (!this.SessionActive)
            return;
        this.SessionActive = false;

        var arena = Game1.getLocationFromName(ArenaLocationName);
        if (arena != null)
            arena.characters.RemoveWhere(c => c is Monster);
        this.sessionBought.Clear();

        if (Game1.activeClickableMenu != null)
            Game1.exitActiveMenu();
        if (refundGold > 0)
        {
            Game1.player.Money += refundGold;
            Game1.playSound("purchase");
        }
        Game1.warpFarmer(this.returnLocation, (int)this.returnTile.X, (int)this.returnTile.Y, 2);
        this.monitor.Log($"Arena session ended (refund {refundGold}g).", LogLevel.Info);
    }
}
