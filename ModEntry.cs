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
    private IModHelper Helper = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Arena = new ArenaManager(helper, this.Monitor);
        this.Helper = helper;
        ArenaManager.SessionEndBroadcast = this.BroadcastSessionEnd;

        var harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction), new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.BeforePerformAction))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.monsterDrop), new[] { typeof(Monster), typeof(int), typeof(int), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.BeforeMonsterDrop))
        );

        helper.Events.Content.AssetRequested += new ArenaMapAsset().OnAssetRequested;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Player.Warped += this.OnWarped;
        // 联机消息:访客购买/请求进竞技场 → 主机
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.ConsoleCommands.Add("ma_arena", "Open the monster arena shop (debug).", (_, __) => this.OpenShop());
        helper.ConsoleCommands.Add("ma_test", "Queue 2 test monsters and enter the arena (debug).", (_, __) =>
        {
            this.Arena.QueuePurchase(MonsterCatalog.All[0], 2);
            this.Arena.QueuePurchase(MonsterCatalog.All[11], 1);
            this.Arena.BeginSession();
        });
        helper.ConsoleCommands.Add("ma_selftest", "Construct every catalog monster and hit-test it (debug).", (_, __) => this.RunSelfTest());
        helper.ConsoleCommands.Add("ma_spawnall", "Queue every catalog monster and enter the arena (debug).", (_, __) =>
        {
            for (int i = 0; i < MonsterCatalog.All.Count; i++)
                this.Arena.QueuePurchase(MonsterCatalog.All[i], 1);
            this.Arena.BeginSession();
            this.Monitor.Log($"[ma_spawnall] 已进竞技场: 全部 {MonsterCatalog.All.Count} 种怪物各 1 只。", LogLevel.Info);
        });

        // automated test hook: if autotest.txt sits next to the DLL, run the self-test
        // automatically right after a save loads (used for headless regression runs)
        this.autotestPending = System.IO.File.Exists(System.IO.Path.Combine(helper.DirectoryPath, "autotest.txt"));
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

    /// <summary>联机消息类型与载荷(访客 → 主机)。</summary>
    private const string MsgBuy = "ma_buy";
    private const string MsgEnter = "ma_enter";
    private const string MsgEnterAck = "ma_enter_ack";
    private const string MsgSessionEnd = "ma_session_end";

    /// <summary>主机:广播竞技场会话结束(访客清 SessionActive,防"再点开打进空场")。</summary>
    public void BroadcastSessionEnd()
    {
        if (!Game1.IsMasterGame) return;
        Helper.Multiplayer.SendMessage(new object(), MsgSessionEnd, new[] { ModManifest.UniqueID });
    }

    private class BuyPayload
    {
        public string MonsterName = "";
        public int Count;
    }

    /// <summary>主机收到访客消息:买怪(加入共享池) / 请求进竞技场(刷怪)。</summary>
    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != ModManifest.UniqueID)
            return;
        if (!Context.IsWorldReady || !Game1.IsMasterGame)
            return;
        try
        {
            switch (e.Type)
            {
                case MsgBuy:
                {
                    var payload = e.ReadAs<BuyPayload>();
                    var cat = MonsterCatalog.All.FirstOrDefault(c => c.Name == payload.MonsterName);
                    if (cat != null && payload.Count > 0)
                    {
                        this.Arena.QueuePurchase(cat, payload.Count);
                        this.Monitor.Log($"[ma] 联机: 玩家 {e.FromPlayerID} 购买 {payload.Count} 只 {cat.Name}(已入共享池)。", LogLevel.Info);
                    }
                    break;
                }
                case MsgEnter:
                    // 访客请求进竞技场:主机建场+刷怪(若未开),回 ack 让【发起者】进去。
                    // ⚠️ 不能调 BeginSession(那会 warp 主机)——开打者是谁,谁进。
                    if (Game1.IsMasterGame)
                    {
                        this.Arena.HostOpenArena();
                        this.Helper.Multiplayer.SendMessage(new object(), MsgEnterAck, new[] { ModManifest.UniqueID }, new[] { e.FromPlayerID });
                    }
                    break;
                case MsgEnterAck:
                    // 访客收到 ack:主机已建场刷怪(或会话已开)→ 访客自己 warp 进竞技场。
                    if (!Game1.IsMasterGame)
                        this.Arena.BeginSessionRemote();
                    break;
                case MsgSessionEnd:
                    // 主机结束会话:访客清 SessionActive;若在竞技场里则先退出(回原地点)再清。
                    if (!Game1.IsMasterGame)
                    {
                        bool inArena = Game1.currentLocation?.Name == ArenaManager.ArenaLocationName;
                        if (inArena)
                            this.Arena.LeaveArena(0);   // 先退(LeaveArena 内部依赖 SessionActive)
                        this.Arena.ClearSession();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[ma] 联机消息处理失败: {ex}", LogLevel.Error);
        }
    }

    /// <summary>竞技场地点必须常驻主机(访客 warp 到主机 RequireLocation 失败 = 黑屏)。
    /// 读档后若竞技场不存在则重建(玩家第一次进时也已由 BeginSession 保证)。</summary>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (!Game1.IsMasterGame)
            return;
        try
        {
            var existing = Game1.getLocationFromName(ArenaManager.ArenaLocationName);
            if (existing == null)
            {
                var arena = new GameLocation(ArenaManager.ArenaMapAsset, ArenaManager.ArenaLocationName);
                arena.map.LoadTileSheets(Game1.mapDisplayDevice);
                Game1.locations.Add(arena);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[ma] 读档重建竞技场失败: {ex}", LogLevel.Error);
        }
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Arena.Pending.Clear();
        this.Arena.ClearSession();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        // 每天清空未使用的购买池(防跨天堆积)
        this.Arena.Pending.Clear();
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
                if (!Game1.IsMasterGame)
                {
                    // 访客:请主机准备竞技场(未开则建场刷怪;已开则直接放行),然后【访客自己】warp 进。
                    this.Helper.Multiplayer.SendMessage(new object(), MsgEnter, new[] { ModManifest.UniqueID });
                    Game1.drawObjectDialogue("已通知房主准备竞技场，怪物马上进场。");
                    return;
                }
                if (this.Arena.HasPending || this.Arena.SessionActive)
                {
                    // 主机自己进:未开会话先开场上怪;已开会话(可能是访客开的)直接进。
                    if (!this.Arena.SessionActive)
                        this.Arena.HostOpenArena();
                    this.Arena.BeginSession();
                }
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
            {
                if (Game1.IsMasterGame)
                    this.Arena.QueuePurchase(cat, count);
                else
                {
                    // 访客购买 → 发给主机进共享池(购买已扣访客的钱,池子里标记买家)。
                    // 注意:ShopMenu 在访客端会真实扣钱(原版多人同步 Money),这里只同步"买了什么"。
                    this.Helper.Multiplayer.SendMessage(new BuyPayload { MonsterName = cat.Name, Count = count }, MsgBuy, new[] { ModManifest.UniqueID });
                    this.Monitor.Log($"[ma] 联机: 访客购买 {count} 只 {cat.Name},已请求主机加入共享池。", LogLevel.Info);
                }
            }
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

    // --- drop rate halving ---
    // Arena monsters drop at half the vanilla rate. The original roll happens at spawn time
    // (parseMonsterInfo) so it can't be re-rolled here; instead we remove each queued drop with
    // 50% probability, which halves every drop line's effective rate without touching the
    // guaranteed-100% lines' drop category (they just drop half as often).
    private static bool BeforeMonsterDrop(GameLocation __instance, Monster monster)
    {
        try
        {
            if (__instance.Name != ArenaManager.ArenaLocationName || monster == null)
                return true;
            if (monster.objectsToDrop != null)
            {
                for (int i = monster.objectsToDrop.Count - 1; i >= 0; i--)
                {
                    if (Game1.random.NextDouble() < 0.5)
                        monster.objectsToDrop.RemoveAt(i);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Arena drop halving hook failed: {ex}", LogLevel.Error);
            return true;
        }
    }

    // --- session flow ---
    private bool wasShopOpen;
    private bool wasAtExit;
    private bool autotestPending;
    private bool autotestSpawnPending;

    /// <summary>Autotest part 3: in a live save, queue every catalog monster, warp into the
    /// arena, and verify all of them spawned alive at the pen.</summary>
    private void RunArenaIntegrationTest()
    {
        try
        {
            for (int i = 0; i < MonsterCatalog.All.Count; i++)
                this.Arena.QueuePurchase(MonsterCatalog.All[i], 1);
            this.Arena.BeginSession();

            var arena = Game1.getLocationFromName(ArenaManager.ArenaLocationName);
            int alive = this.Arena.RemainingMonsters();
            var names = arena?.characters
                .OfType<StardewValley.Monsters.Monster>()
                .Where(m => m.Health > 0)
                .Select(m => m.Name)
                .ToList();

            this.Monitor.Log($"[ma_selftest] 实机集成测试: 排队 {MonsterCatalog.All.Count} 只, 竞技场存活 {alive} 只. {string.Join("、", names ?? new List<string>())}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[ma_selftest] 实机集成测试失败: {ex}", LogLevel.Error);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        // automated test hook: run the self-test once as soon as the game is up.
        // Works on the title screen — monster construction only needs the content
        // pipeline, and takeDamage only needs a location + a farmer reference.
        if (this.autotestPending && Context.IsGameLaunched && !Context.IsWorldReady)
        {
            this.autotestPending = false;
            this.RunSelfTest();
            this.autotestSpawnPending = true; // arm the arena spawn for the next save load
            return;
        }

        // integration test part 2: once the player is free in a real save, queue ALL
        // monsters and warp in so the full arena flow can be exercised live
        if (this.autotestSpawnPending && Context.IsWorldReady && Context.IsPlayerFree)
        {
            this.autotestSpawnPending = false;
            this.RunArenaIntegrationTest();
        }

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
        // 联机:只主机做(怪物实体只在主机;访客镜像会被主机同步覆盖,重复操作无意义)
        if (Game1.currentLocation?.Name == ArenaManager.ArenaLocationName && Game1.IsMasterGame && e.IsMultipleOf(4))
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

    /// <summary>Construct every catalog monster and verify it (a) spawns with a visible
    /// texture, (b) takes damage when hit, and (c) dies when hit enough. Prints a per-monster
    /// report. Used for regression-testing the monster factories.</summary>
    private void RunSelfTest()
    {
        int pass = 0, fail = 0;
        var failures = new List<string>();

        // one shared location so sounds/drops have somewhere to go; never registered in
        // Game1.locations, just a scratch object
        var scratch = new GameLocation("Maps\\Town", "ma_selftest_scratch");
        var who = Game1.player; // non-null even on the title screen

        foreach (var entry in MonsterCatalog.All)
        {
            string result;
            try
            {
                result = this.TestOneMonster(entry, scratch, who);
            }
            catch (Exception ex)
            {
                result = $"构造/受击异常: {ex.GetType().Name}: {ex.Message}";
            }

            bool ok = result == "OK";
            if (ok) pass++; else fail++;
            if (!ok) failures.Add($"{entry.Name}: {result}");
            this.Monitor.Log($"[ma_selftest] {(ok ? "PASS" : "FAIL")} {entry.Name} — {result}", ok ? LogLevel.Info : LogLevel.Error);
        }

        this.Monitor.Log($"[ma_selftest] 完成: {pass} 通过, {fail} 失败. {string.Join("; ", failures)}", fail == 0 ? LogLevel.Info : LogLevel.Warn);

        // part 2: spawn all 46 monsters into the REAL arena through the production code path
        try
        {
            int alive = this.Arena.TestSpawnAllInArena();
            var arena = Game1.getLocationFromName(ArenaManager.ArenaLocationName);
            var bad = arena?.characters
                .OfType<StardewValley.Monsters.Monster>()
                .Where(m => m.Health > 0 && m.Sprite?.Texture == null)
                .Select(m => m.Name)
                .ToList();
            this.Monitor.Log($"[ma_selftest] 竞技场实机刷怪: 已刷 {MonsterCatalog.All.Count} 种, 存活 {alive} 只. 无贴图: {(bad != null && bad.Count > 0 ? string.Join("、", bad) : "无")}", alive == MonsterCatalog.All.Count && (bad == null || bad.Count == 0) ? LogLevel.Info : LogLevel.Warn);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"[ma_selftest] 竞技场实机刷怪失败: {ex}", LogLevel.Error);
        }
    }

    private string TestOneMonster(MonsterCatalog.Entry entry, GameLocation scratch, Farmer who)
    {
        // 1. construct
        var m = entry.Factory(Vector2.Zero);
        if (m == null) return "工厂返回 null";
        if (m.Health <= 0) return $"血量异常 ({m.Health})";

        // 2. visible sprite?
        if (m.Sprite?.Texture == null) return "无贴图 (Sprite.Texture 为 null)";
        if (m.Sprite.SourceRect.Width <= 0 || m.Sprite.SourceRect.Height <= 0) return "精灵尺寸异常";

        // 3. put it in a location so takeDamage works (needs currentLocation for sounds)
        scratch.characters.Clear();
        scratch.characters.Add(m);
        m.currentLocation = scratch;
        m.Position = new Vector2(64f, 64f);

        // 4. freeze it exactly like the arena does (ArenaManager.Freeze), then hit it:
        //    3 hits of maxHealth each — the monster must take damage every hit and die by
        //    the third. Damage 99999 bypasses resilience/armor checks.
        m.stunTime.Value = int.MaxValue;
        m.DamageToFarmer = 0;
        m.focusedOnFarmers = false;

        for (int i = 0; i < 3; i++)
        {
            int dealt = m.takeDamage(99999, 0, 0, false, 1.0, who);
            if (dealt <= 0)
                return $"第{i + 1}次受击无效 (takeDamage 返回 {dealt}) — 被格挡/免疫";
            if (m.Health <= 0)
            {
                if (m.ShouldMonsterBeRemoved())
                    return "OK";
                return $"血量归零但 ShouldMonsterBeRemoved()=false — 死亡后不被移除";
            }
        }

        return "3次重击后仍未死亡 (HP残留: " + m.Health + ")";
    }
}
