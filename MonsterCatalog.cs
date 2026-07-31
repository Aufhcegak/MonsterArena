using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley.Monsters;

namespace MonsterArena;

/// <summary>The full monster catalog with real vanilla stats and computed prices,
/// grouped by region and ordered by difficulty. Stats come from Data/Monsters (game 1.6.15).</summary>
public static class MonsterCatalog
{
    public class Entry
    {
        public string Region;
        public string Name;
        public int Hp, Dmg, Exp, Price;
        public Func<Vector2, Monster> Factory;
        public Entry(string region, string name, int hp, int dmg, int exp, int price, Func<Vector2, Monster> factory)
        { Region = region; Name = name; Hp = hp; Dmg = dmg; Exp = exp; Price = price; Factory = factory; }
    }

    public static readonly List<Entry> All = new()
    {
        // ── 矿井 1-39 ──
        new("矿井 1-39", "Green Slime", 24, 5, 3, 400, p => new GreenSlime(p)),
        new("矿井 1-39", "Rock Crab", 30, 5, 4, 600, p => new RockCrab(p)),
        new("矿井 1-39", "Duggy", 40, 6, 10, 1250, p => new Duggy(p)),
        new("矿井 1-39", "Grub", 20, 4, 2, 700, p => new Grub(p)),
        new("矿井 1-39", "Fly", 22, 6, 10, 750, p => new Fly(p)),
        new("矿井 1-39", "Bug", 1, 8, 1, 700, p => new Bug(p, 0)),
        new("矿井 1-39", "Bat", 24, 6, 3, 850, p => new Bat(p, 1)),
        new("矿井 1-39", "Stone Golem", 45, 5, 5, 950, p => new RockGolem(p)),
        // ── 矿井 40-79 ──
        new("矿井 40-79", "Frost Jelly", 106, 7, 6, 750, p => new GreenSlime(p, 60)),
        new("矿井 40-79", "Frost Bat", 36, 7, 7, 900, p => new Bat(p, 45)),
        new("矿井 40-79", "Dust Spirit", 40, 6, 2, 700, p => new DustSpirit(p, true)),
        new("矿井 40-79", "Ghost", 96, 10, 15, 2250, p => new Ghost(p)),
        new("矿井 40-79", "Skeleton", 140, 10, 8, 950, p => new Skeleton(p)),
        new("矿井 40-79", "Skeleton Mage", 60, 5, 8, 750, p => new Skeleton(p, true)),
        new("矿井 40-79", "Metal Head", 40, 15, 6, 1250, p => new MetalHead("Metal Head", p)),
        new("矿井 40-79", "Spiker", 5, 15, 1, 950, p => new Spiker(p, 0)),
        new("矿井 40-79", "Shadow Brute", 160, 18, 15, 1150, p => new ShadowBrute(p)),
        new("矿井 40-79", "Shadow Shaman", 80, 17, 15, 950, p => new ShadowShaman(p)),
        // ── 矿井 80-119 ──
        new("矿井 80-119", "Sludge", 205, 16, 10, 1300, p => new GreenSlime(p, 100)),
        new("矿井 80-119", "Lava Bat", 80, 15, 15, 1050, p => new Bat(p, 85)),
        new("矿井 80-119", "Lava Crab", 120, 15, 12, 1600, p => new RockCrab(p, "Lava Crab")),
        new("矿井 80-119", "Squid Kid", 1, 18, 15, 2500, p => new SquidKid(p)),
        new("矿井 80-119", "Shadow Guy", 125, 20, 15, 1100, p => new ShadowGuy(p)),
        new("矿井 80-119", "Blue Squid", 80, 18, 15, 1350, p => new BlueSquid(p)),
        // ── 骷髅洞穴 ──
        new("骷髅洞穴", "Serpent", 150, 23, 20, 1200, p => new Serpent(p)),
        new("骷髅洞穴", "Royal Serpent", 150, 23, 20, 1200, p => new Serpent(p, "Royal Serpent")),
        new("骷髅洞穴", "Mummy", 260, 30, 20, 1800, p => new Mummy(p)),
        new("骷髅洞穴", "Carbon Ghost", 190, 25, 20, 1150, p => new Ghost(p, "Carbon Ghost")),
        new("骷髅洞穴", "Iridium Bat", 300, 30, 22, 3550, p => new Bat(p, -789)),
        new("骷髅洞穴", "Iridium Crab", 240, 15, 20, 3000, p => new RockCrab(p, "Iridium Crab")),
        new("骷髅洞穴", "Putrid Ghost", 500, 25, 25, 1900, p => new Ghost(p, "Putrid Ghost")),
        new("骷髅洞穴", "Shadow Sniper", 300, 18, 20, 1550, p => new ShadowShaman(p)),
        new("骷髅洞穴", "Spider", 200, 15, 15, 1300, p => new Fly(p, true)),
        // ── 火山地牢 ──
        new("火山地牢", "Lava Lurk", 220, 15, 12, 1350, p => new LavaLurk(p)),
        new("火山地牢", "Hot Head", 250, 18, 16, 1900, p => new HotHead(p)),
        new("火山地牢", "Magma Sprite", 220, 15, 15, 1300, p => new Bat(p, -555)),
        new("火山地牢", "Magma Duggy", 380, 16, 18, 2050, p => new Duggy(p, true)),
        new("火山地牢", "Magma Sparker", 310, 15, 17, 1500, p => new Bat(p, -556)),
        new("火山地牢", "False Magma Cap", 290, 15, 14, 1600, p => new RockCrab(p, "False Magma Cap")),
        new("火山地牢", "Dwarvish Sentry", 300, 18, 15, 3200, p => new DwarvishSentry(p)),
        // ── 危险/特殊 ──
        new("危险/特殊", "Big Slime", 60, 5, 7, 850, p => new BigSlime(p, 1)),
        new("危险/特殊", "Tiger Slime", 415, 23, 20, 1800, p => new GreenSlime(p, 999)),
        new("危险/特殊", "Pepper Rex", 300, 15, 7, 1400, p => new DinoMonster(p)),
        new("危险/特殊", "Skeleton Warrior", 300, 12, 15, 1800, p => new Skeleton(p, false)),
        new("危险/特殊", "Wilderness Golem", 30, 5, 5, 900, p => new RockGolem(p, 2)),
        new("危险/特殊", "Iridium Golem", 30, 5, 5, 900, p => new RockGolem(p, 6)),
    };
}
