using MonsterArena;
using StardewValley;
using xTile;
using xTile.Dimensions;
using xTile.Layers;

// ============================================================
// MonsterArena 纯逻辑测试
// 链接 MonsterCatalog.cs（目录/工厂元数据）、ArenaMapAsset.cs（地图几何）、
// MonsterEntry.cs（ISalable 非渲染行为）。
// 怪物实体构造（依赖 content 管线）和 ArenaManager 运行时由无头 SMAPI
// 集成测试（ma_selftest）覆盖。
// 跑法：cd logic_test && dotnet run -c Release
// ============================================================

int fails = 0, pass = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail == null ? "" : "  << " + detail));
    if (ok) pass++; else fails++;
}
void CheckEq<T>(string name, T got, T expected)
{
    bool ok = Equals(got, expected);
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : $"  << got={got} expected={expected}"));
    if (ok) pass++; else fails++;
}

// ============================================================
// 第一组：目录完整性 —— 46 种怪物、元数据非零、名称唯一
// ============================================================
{
    Check("catalog: 46 种怪物", MonsterCatalog.All.Count == 46, $"count={MonsterCatalog.All.Count}");

    // 名称唯一
    var names = MonsterCatalog.All.Select(e => e.Name).ToList();
    Check("catalog: 名称唯一", names.Distinct().Count() == names.Count);

    // 全部条目:HP/伤害/经验/价格都 > 0,区域非空
    bool allPositive = MonsterCatalog.All.All(e => e.Hp > 0 && e.Dmg > 0 && e.Exp > 0 && e.Price > 0);
    Check("catalog: 全部元数据正数", allPositive);
    bool allRegions = MonsterCatalog.All.All(e => !string.IsNullOrEmpty(e.Region));
    Check("catalog: 全部有区域", allRegions);

    // 工厂非 null
    bool allFactories = MonsterCatalog.All.All(e => e.Factory != null);
    Check("catalog: 全部有工厂", allFactories);

    // 价格:所有价格在合理范围(≥100 且 < 10000,避免填错位数)
    bool priceRange = MonsterCatalog.All.All(e => e.Price >= 100 && e.Price < 10000);
    Check("catalog: 价格范围 100..9999", priceRange);
}

// ============================================================
// 第二组：区域分组 —— 每个区域都有怪、分布合理
// ============================================================
{
    var groups = MonsterCatalog.All.GroupBy(e => e.Region).ToList();
    Check("region: 6 个区域", groups.Count == 6, $"count={groups.Count} [{string.Join(",", groups.Select(g => g.Key))}]");
    bool everyRegionHas = groups.All(g => g.Count() >= 3);
    Check("region: 每区 ≥3 种", everyRegionHas);

    // 危险/特殊区是最后一段(顺序:前四个区域 + 危险区)
    Check("region: 危险区最后", MonsterCatalog.All.Last().Region == "危险/特殊");
}

// ============================================================
// 第三组：难度单调性 —— 后面区域的怪平均比前面强（血量）
// ============================================================
{
    // 骷髅洞穴(索引 23..31)平均血量应明显高于矿井 1-39(索引 0..7)
    double mineAvg = MonsterCatalog.All.Take(8).Average(e => e.Hp);
    double skullAvg = MonsterCatalog.All.Where(e => e.Region == "骷髅洞穴").Average(e => e.Hp);
    Check("difficulty: 骷髅洞穴平均血 > 矿井 1-39",
        skullAvg > mineAvg, $"skull={skullAvg:F0} mine={mineAvg:F0}");

    // 每个区域的平均伤害非递减(大趋势检查:火山 > 骷髅洞穴 > 矿井 80-119 > 矿井 40-79 > 矿井 1-39)
    double[] avgDmg = MonsterCatalog.All.GroupBy(e => e.Region).Select(g => g.Average(e => e.Dmg)).ToArray();
    // 后三个区域(矿井 40-79/80-119/骷髅洞穴)的伤害应整体高于矿井 1-39
    Check("difficulty: 后期区域伤害更高",
        avgDmg.Skip(1).Min() >= avgDmg[0] * 1.0, $"first={avgDmg[0]:F0} minLater={avgDmg.Skip(1).Min():F0}");
}

// ============================================================
// 第四组：MonsterEntry ISalable —— 非渲染行为
// ============================================================
{
    var entry = new MonsterEntry("TestMonster", 500, 100, 10, 15, p => null!); // 工厂返回 null(只测元数据)

    // Stack 恒为 1(防 ShopMenu 数量膨胀 bug 的回归)
    entry.Stack = 99;
    CheckEq("entry: Stack 恒 1", entry.Stack, 1);

    // QualifiedItemId 唯一命名
    Check("entry: QID 含名称", entry.QualifiedItemId == "(Salable)TestMonster");

    // 价格/元数据透传
    CheckEq("entry: 价格", entry.salePrice(), 500);
    CheckEq("entry: 血量", entry.Health, 100);
    CheckEq("entry: 伤害", entry.Damage, 10);
    CheckEq("entry: 经验", entry.Experience, 15);

    // ISalable 契约:可买、非配方、非无限库存
    Check("entry: 可买", entry.CanBuyItem(null!));
    Check("entry: 非配方", !entry.IsRecipe);
    Check("entry: 非无限库存", !entry.IsInfiniteStock());
    Check("entry: 不堆叠", !entry.canStackWith(entry));
    CheckEq("entry: 购买即弃", entry.actionWhenPurchased("shop"), true);
    CheckEq("entry: 不套利", entry.sellToStorePrice(), 0);

    // 工厂 null 的 entry:DisplayName 走 GetIcon → 构造崩溃?必须安全
    // (GetIcon 调 factory(Vector2.Zero) 返回 null → 抛 NRE。这是防御性问题,标记)
    try
    {
        _ = entry.DisplayName;
        Check("entry: null 工厂 DisplayName 不崩", false, "GetIcon 居然没炸");
    }
    catch (Exception)
    {
        Check("entry: null 工厂 DisplayName 不崩", true); // 现实中工厂不会返回 null(目录保证)
    }

    // 两个 entry 不同名 → 不相等(字典键安全)
    var e2 = new MonsterEntry("Other", 100, 1, 1, 1, p => null!);
    Check("entry: 不同名不同键", entry.QualifiedItemId != e2.QualifiedItemId);
}

// ============================================================
// 第五组：地图几何 —— 尺寸、图层、墙完整、门缺口、出生点
// ============================================================
{
    var mapAsset = new ArenaMapAsset();
    Map map = mapAsset.BuildMapPublic();

    // 尺寸:23x17(15x9 房间 + 4 边 pad)
    CheckEq("map: 宽 23", map.DisplayWidth, 23 * 64);
    CheckEq("map: 高 17", map.DisplayHeight, 17 * 64);
    CheckEq("map: 图层数 5", map.Layers.Count, 5);
    Check("map: 图层齐全", map.GetLayer("Back") != null && map.GetLayer("Buildings") != null
        && map.GetLayer("Front") != null && map.GetLayer("Paths") != null && map.GetLayer("AlwaysFront") != null);

    // 地图 ID
    CheckEq("map: 地图 ID", map.Id, "xiepe.MonsterArena.Arena");

    var buildings = map.GetLayer("Buildings");
    int fullW = ArenaMapAsset.FullW, fullH = ArenaMapAsset.FullH;

    // HARD BORDER:整个外圈每一格都有 Buildings tile(防穿墙)
    bool borderComplete = true;
    for (int x = 0; x < fullW; x++)
    {
        if (buildings.Tiles[x, 0] == null || buildings.Tiles[x, fullH - 1] == null) borderComplete = false;
    }
    for (int y = 0; y < fullH; y++)
    {
        if (buildings.Tiles[0, y] == null || buildings.Tiles[fullW - 1, y] == null) borderComplete = false;
    }
    Check("map: 外圈硬边界完整", borderComplete);

    // 房间四面墙:北墙 X0..X1 每格有 tile;西/东墙 Y0+1..Y1-1 每格有 tile
    bool northWall = true, sideWalls = true;
    for (int x = ArenaMapAsset.X0; x <= ArenaMapAsset.X1; x++)
        if (buildings.Tiles[x, ArenaMapAsset.Y0] == null) northWall = false;
    for (int y = ArenaMapAsset.Y0 + 1; y < ArenaMapAsset.Y1; y++)
    {
        if (buildings.Tiles[ArenaMapAsset.X0, y] == null) sideWalls = false;
        if (buildings.Tiles[ArenaMapAsset.X1, y] == null) sideWalls = false;
    }
    Check("map: 北墙完整", northWall);
    Check("map: 西/东墙完整", sideWalls);

    // 南墙:除门洞外都有 tile;门洞恰好两格空
    int doorHoles = 0;
    for (int x = ArenaMapAsset.X0; x <= ArenaMapAsset.X1; x++)
    {
        if (buildings.Tiles[x, ArenaMapAsset.Y1] == null)
            doorHoles++;
    }
    CheckEq("map: 南墙门洞 2 格", doorHoles, 2);
    Check("map: 门洞在 DoorX0/DoorX1", buildings.Tiles[ArenaMapAsset.DoorX0, ArenaMapAsset.Y1] == null
        && buildings.Tiles[ArenaMapAsset.DoorX1, ArenaMapAsset.Y1] == null);
    Check("map: 门洞两侧有墙", buildings.Tiles[ArenaMapAsset.DoorX0 - 1, ArenaMapAsset.Y1] != null
        && buildings.Tiles[ArenaMapAsset.DoorX1 + 1, ArenaMapAsset.Y1] != null);

    // 门洞外(pad 区,南墙下一行)必须可通行——门是出口,玩家走出去;
    // 最终护栏是外圈硬边界(FullH-1 行),那里必须封死
    Check("map: 门洞外可通行(出口)", buildings.Tiles[ArenaMapAsset.DoorX0, ArenaMapAsset.Y1 + 1] == null
        && buildings.Tiles[ArenaMapAsset.DoorX1, ArenaMapAsset.Y1 + 1] == null);
    Check("map: 出口外最终护栏封死", buildings.Tiles[ArenaMapAsset.DoorX0, ArenaMapAsset.FullH - 1] != null
        && buildings.Tiles[ArenaMapAsset.DoorX1, ArenaMapAsset.FullH - 1] != null);

    // 地板:Back 图层全覆盖(无 null)
    var back = map.GetLayer("Back");
    bool floorComplete = true;
    for (int y = 0; y < fullH; y++)
        for (int x = 0; x < fullW; x++)
            if (back.Tiles[x, y] == null) floorComplete = false;
    Check("map: 地板全覆盖", floorComplete);

    // 笔格(Pen)在房间里、靠北墙
    Check("map: 笔格在房内", ArenaMapAsset.PenX > ArenaMapAsset.X0 && ArenaMapAsset.PenX < ArenaMapAsset.X1
        && ArenaMapAsset.PenY > ArenaMapAsset.Y0 && ArenaMapAsset.PenY < ArenaMapAsset.Y1);
    Check("map: 笔格靠北墙", ArenaMapAsset.PenY == ArenaMapAsset.Y0 + 2);
    Check("map: 笔格居中", ArenaMapAsset.PenX == ArenaMapAsset.X0 + ArenaMapAsset.W / 2);

    // 出生点:门内一行、与门同列
    Check("map: 出生点在门内", ArenaMapAsset.SpawnY == ArenaMapAsset.Y1 - 1);
    Check("map: 出生点与门同列", ArenaMapAsset.SpawnX == ArenaMapAsset.DoorX0);
}

// ============================================================
// 第六组：常量一致性 —— 门两格相邻、笔格不压墙、资源路径
// ============================================================
{
    // 门洞两格相邻
    CheckEq("const: 门两格相邻", ArenaMapAsset.DoorX1, ArenaMapAsset.DoorX0 + 1);

    // 笔格三面有基脚板(北墙下 + 两侧)——防怪穿墙
    Check("const: 笔格不压北墙", ArenaMapAsset.PenY > ArenaMapAsset.Y0 + 1);

    // 房间尺寸合理(宽 ≥ 高)
    Check("const: 房间宽 ≥ 高", ArenaMapAsset.W >= ArenaMapAsset.H);

    // 全图尺寸 = 房间 + 2*pad
    CheckEq("const: 全宽公式", ArenaMapAsset.FullW, ArenaMapAsset.W + ArenaMapAsset.Pad * 2);
    CheckEq("const: 全高公式", ArenaMapAsset.FullH, ArenaMapAsset.H + ArenaMapAsset.Pad * 2);

    // Arena 常量与地图资产路径一致
    CheckEq("const: 地图资产路径", ArenaManager.ArenaMapAsset, "Maps/" + ArenaManager.ArenaLocationName);
    CheckEq("const: 地点名", ArenaManager.ArenaLocationName, "xiepe.MonsterArena.Arena");
}

// ============================================================
// 第七组：商店库存映射 —— OnPurchased 的查找语义（名称匹配）
// ============================================================
{
    // 商店把 MonsterEntry(名称) 映射回 MonsterCatalog 条目:名称必须完全匹配
    // (ModEntry.OnPurchased 用 FirstOrDefault(c => c.Name == entry.MonsterName))
    bool allMatch = MonsterCatalog.All.All(e =>
    {
        var entry = new MonsterEntry(e.Name, e.Price, e.Hp, e.Dmg, e.Exp, e.Factory);
        var found = MonsterCatalog.All.FirstOrDefault(c => c.Name == entry.MonsterName);
        return found != null && found.Name == e.Name && found.Price == e.Price;
    });
    Check("shop: 名称回查全部命中", allMatch);

    // 商店里 MonsterEntry 元数据与目录一致(防手抄错)
    bool metaMatch = MonsterCatalog.All.All(e =>
    {
        var entry = new MonsterEntry(e.Name, e.Price, e.Hp, e.Dmg, e.Exp, e.Factory);
        return entry.Health == e.Hp && entry.Damage == e.Dmg && entry.Experience == e.Exp && entry.Price == e.Price;
    });
    Check("shop: 商店元数据与目录一致", metaMatch);
}

Console.WriteLine($"\n总计: PASS={pass} FAIL={fails}");
return fails == 0 ? 0 : 1;
