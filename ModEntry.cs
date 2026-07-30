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
            "哦？想练练手？我驯了一批怪物，明码标价，关在后面的围栏里，你只管砍。要买装备、找回东西，还是来练练？",
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
            // stock 99 so the row stays after buying; price from catalog
            stock[entry] = new ItemStockInformation(e.Price, 99);
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

    // --- session flow ---
    private bool wasShopOpen;

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
        // auto-end when the arena is cleared
        if (Game1.currentLocation?.Name == ArenaManager.ArenaLocationName && this.Arena.RemainingMonsters() == 0)
        {
            Game1.drawObjectDialogue("砍完了！过瘾吧。马龙把你送了回来。");
            this.Arena.EndSession();
        }
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        // if the player leaves the arena early, clean up so the next run starts fresh
        if (e.OldLocation?.Name == ArenaManager.ArenaLocationName && this.Arena.SessionActive)
            this.Arena.EndSession();
    }
}
