using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Monsters;

namespace MonsterArena;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;
    internal ArenaManager Arena = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Arena = new ArenaManager(helper, this.Monitor);

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction), new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.BeforePerformAction))
        );

        helper.Events.Content.AssetRequested += new ArenaMapAsset().OnAssetRequested;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Player.Warped += this.OnWarped;
        helper.ConsoleCommands.Add("ma_arena", "Open the monster arena shop (debug).", (_, __) => this.OpenShop());
        helper.ConsoleCommands.Add("ma_test", "Queue 2 test monsters and enter the arena (debug).", (_, __) =>
        {
            this.Arena.QueuePurchase(MonsterCatalog.All[0], 2);
            this.Arena.QueuePurchase(MonsterCatalog.All[11], 1);
            this.Arena.BeginSession();
        });
    }

    /// <summary>Expose a tiny API so an automated self-test mod can drive the arena.</summary>
    public override object? GetApi(IModInfo mod) => new MonsterArenaApi(this.Arena);

    public class MonsterArenaApi
    {
        private readonly ArenaManager arena;
        public MonsterArenaApi(ArenaManager arena) => this.arena = arena;
        public void QueueFirst(int count) => this.arena.QueuePurchase(MonsterCatalog.All[0], count);
        public void QueueIndex(int index, int count) => this.arena.QueuePurchase(MonsterCatalog.All[index], count);
        public void Begin() => this.arena.BeginSession();
        public int Remaining() => this.arena.RemainingMonsters();
        public bool Active => this.arena.SessionActive;
    }

    // --- Marlon dialogue injection ---
    // The counter tile in front of Marlon has the tile action "AdventureShop", which routes to
    // GameLocation.adventureShop() (opens the shop, or a Shop/Recovery/Leave question if you have
    // lost items). We intercept that tile action so we always show our own menu instead.
    private static bool BeforePerformAction(string[] action, Farmer who, GameLocation __instance)
    {
        try
        {
            if (!Context.IsWorldReady || action == null || action.Length == 0)
                return true;
            // only hijack Marlon's counter tile, nothing else
            if (!string.Equals(action[0], "AdventureShop", StringComparison.OrdinalIgnoreCase))
                return true;
            if (__instance == null || !who.IsLocalPlayer)
                return true;
            Instance.OfferArenaDialogue(__instance);
            return false; // skip vanilla adventureShop()
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Arena dialogue hook failed: {ex}", LogLevel.Error);
            return true;
        }
    }

    private void OfferArenaDialogue(GameLocation location)
    {
        var responses = new List<Response>
        {
            new Response("Arena_Browse", "挑选怪物（可一次选多只）。"),
            new Response("Shop", "买武器装备。"),
            new Response("Arena_Enter", "直接开打（已选好的怪）。"),
            new Response("Arena_No", "今天不了。")
        };
        // include item-recovery only when the player actually has items to recover
        if (Game1.player.itemsLostLastDeath.Count > 0)
            responses.Insert(2, new Response("Recovery", "找回丢失的物品。"));

        location.createQuestionDialogue(
            "想练练手？我驯了一批怪，关在围栏里，跑不掉也伤不了你。砍死照样掉东西、长经验，打完从南门出去。来几只？",
            responses.ToArray(),
            new GameLocation.afterQuestionBehavior(this.OnArenaAnswer),
            Game1.getCharacterFromName("Marlon")
        );
    }

    private void OnArenaAnswer(Farmer who, string whichAnswer)
    {
        switch (whichAnswer)
        {
            case "Arena_Browse":
                this.OpenShop();
                break;
            case "Shop":
                Game1.player.forceCanMove();
                Utility.TryOpenShopMenu("AdventureShop", "Marlon");
                break;
            case "Recovery":
                Game1.player.forceCanMove();
                Utility.TryOpenShopMenu("AdventureGuildRecovery", "Marlon");
                break;
            case "Arena_Enter":
                if (this.Arena.HasPending)
                    this.Arena.BeginSession();
                else
                    Game1.drawObjectDialogue("你还没选怪物。先点“挑选怪物”加几只，关掉清单后再来。");
                break;
            default:
                break;
        }
    }

    // --- shop menu ---
    private void OpenShop()
    {
        var stock = new Dictionary<ISalable, ItemStockInformation>();
        foreach (var e in MonsterCatalog.All)
        {
            var entry = new MonsterEntry(e.Name, e.Price, e.Hp, e.Dmg, e.Exp, e.Factory);
            // stock 999 so the row stays after buying; price from catalog
            stock[entry] = new ItemStockInformation(e.Price, 999);
        }
        var menu = new ShopMenu("xiepe.MonsterArena.Shop", stock, 0, "Marlon", this.OnPurchased, null, true);
        Game1.activeClickableMenu = menu;
    }

    private bool OnPurchased(ISalable salable, Farmer who, int count, ItemStockInformation stockInfo)
    {
        if (salable is MonsterEntry entry)
        {
            var cat = MonsterCatalog.All.FirstOrDefault(c => c.Name == entry.MonsterName);
            if (cat != null)
                this.Arena.QueuePurchase(cat, count);
        }
        return false; // keep the shop open so you can pick several before submitting
    }

    // --- Adventure Guild open time: 2 PM -> 9 AM ---
    // The mountain door tile action is "LockedDoorWarp 6 19 AdventureGuild 1400 2600".
    // Rewrite the map asset so it opens at 9 AM instead, to match the arena being available early.
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Maps/Mountain"))
            return;
        e.Edit(asset =>
        {
            var map = asset.AsMap().Data;
            var buildings = map.GetLayer("Buildings");
            var tile = buildings.Tiles[76, 8];
            if (tile != null && tile.Properties.TryGetValue("Action", out var action))
            {
                string current = action.ToString();
                // only swap the open-time token, leave every other character untouched
                if (current.StartsWith("LockedDoorWarp 6 19 AdventureGuild 1400"))
                {
                    tile.Properties["Action"] = new xTile.ObjectModel.PropertyValue("LockedDoorWarp 6 19 AdventureGuild 900" + current.Substring("LockedDoorWarp 6 19 AdventureGuild 1400".Length));
                    this.Monitor.Log("冒险者公会开门时间已改为早上 9:00。", LogLevel.Info);
                }
            }
        }, AssetEditPriority.Default);
    }

    // --- session flow ---
    private bool wasShopOpen;
    private bool wasAtExit;

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        // when the pick-list closes, show one summary of everything you queued
        bool shopOpen = Game1.activeClickableMenu is ShopMenu sm && sm.ShopId == "xiepe.MonsterArena.Shop";
        if (this.wasShopOpen && !shopOpen && this.Arena.HasPending && !this.Arena.SessionActive)
        {
            var lines = this.Arena.PendingSummary()
                .Select(kv => $"{kv.Key.Factory(Microsoft.Xna.Framework.Vector2.Zero).displayName} x{kv.Value}")
                .ToList();
            Game1.drawObjectDialogue($"已选好 {this.Arena.Pending.Count} 只怪物：{string.Join("、", lines)}。跟马龙说“直接开打”就能进围栏了。");
        }
        this.wasShopOpen = shopOpen;

        if (!this.Arena.SessionActive)
            return;

        // exit: stepping onto the south door. Edge-triggered, and not while a menu is up.
        bool atExit = this.Arena.IsPlayerAtExit();
        if (atExit && !this.wasAtExit && Game1.activeClickableMenu == null)
        {
            this.wasAtExit = true;
            int alive = this.Arena.RemainingMonsters();
            if (alive <= 0)
            {
                // cleared everything: no refund, just walk out
                this.Arena.LeaveArena(0);
                return;
            }
            // still monsters left: confirm + offer a partial refund
            int refund = this.Arena.ComputeRefund();
            Game1.currentLocation.createQuestionDialogue(
                $"还有 {alive} 只怪没打完。现在走的话我收回来，退你 {refund} 金。走吗？",
                new[] { new Response("Arena_LeaveYes", "走，退钱收工。"), new Response("Arena_LeaveNo", "不走，接着砍。") },
                new GameLocation.afterQuestionBehavior(this.OnExitAnswer)
            );
            return;
        }
        this.wasAtExit = atExit;

        // keep monsters pinned to the pen (knockback can't push them through Marlon's wall)
        if (Game1.currentLocation?.Name == ArenaManager.ArenaLocationName && e.IsMultipleOf(4))
            this.Arena.RepinMonsters();
    }

    private void OnExitAnswer(Farmer who, string whichAnswer)
    {
        if (whichAnswer == "Arena_LeaveYes")
        {
            this.Arena.LeaveArena(this.Arena.ComputeRefund());
        }
        // "Arena_LeaveNo": do nothing; wasAtExit resets once they step off the door tile
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        // safety: if the player somehow leaves the arena without the door, clean up (no refund)
        if (e.OldLocation?.Name == ArenaManager.ArenaLocationName && this.Arena.SessionActive)
            this.Arena.LeaveArena(0);
    }
}
