using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
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
        new("矿井 1-39", "Rock Crab", 30, 5, 4, 600, p => MakeRockCrab(p, "Rock Crab")),
        new("矿井 1-39", "Duggy", 40, 6, 10, 1250, p => MakeDuggy(p, false)),
        new("矿井 1-39", "Grub", 20, 4, 2, 700, p => MakeGrub(p)),
        new("矿井 1-39", "Fly", 22, 6, 10, 750, p => new Fly(p)),
        new("矿井 1-39", "Bug", 1, 8, 1, 700, p => new Bug(p, 0)),
        new("矿井 1-39", "Bat", 24, 6, 3, 850, p => new Bat(p, 1)),
        new("矿井 1-39", "Stone Golem", 45, 5, 5, 950, p => MakeStoneGolem(p)),
        // ── 矿井 40-79 ──
        new("矿井 40-79", "Frost Jelly", 106, 7, 6, 750, p => new GreenSlime(p, 60)),
        new("矿井 40-79", "Frost Bat", 36, 7, 7, 900, p => new Bat(p, 45)),
        new("矿井 40-79", "Dust Spirit", 40, 6, 2, 700, p => new DustSpirit(p, true)),
        new("矿井 40-79", "Ghost", 96, 10, 15, 2250, p => new Ghost(p)),
        new("矿井 40-79", "Skeleton", 140, 10, 8, 950, p => new Skeleton(p)),
        new("矿井 40-79", "Skeleton Mage", 60, 5, 8, 750, p => new Skeleton(p, true)),
        new("矿井 40-79", "Metal Head", 40, 15, 6, 1250, p => new MetalHead("Metal Head", p)),
        new("矿井 40-79", "Spiker", 5, 15, 1, 950, p => MakeSpiker(p)),
        new("矿井 40-79", "Shadow Brute", 160, 18, 15, 1150, p => new ShadowBrute(p)),
        new("矿井 40-79", "Shadow Shaman", 80, 17, 15, 950, p => new ShadowShaman(p)),
        // ── 矿井 80-119 ──
        new("矿井 80-119", "Sludge", 205, 16, 10, 1300, p => new GreenSlime(p, 100)),
        new("矿井 80-119", "Lava Bat", 80, 15, 15, 1050, p => new Bat(p, 85)),
        new("矿井 80-119", "Lava Crab", 120, 15, 12, 1600, p => MakeRockCrab(p, "Lava Crab")),
        new("矿井 80-119", "Squid Kid", 1, 18, 15, 2500, p => new SquidKid(p)),
        new("矿井 80-119", "Shadow Guy", 125, 20, 15, 1100, p => MakeShadowGuy(p)),
        new("矿井 80-119", "Blue Squid", 80, 18, 15, 1350, p => new BlueSquid(p)),
        // ── 骷髅洞穴 ──
        new("骷髅洞穴", "Serpent", 150, 23, 20, 1200, p => new Serpent(p)),
        new("骷髅洞穴", "Royal Serpent", 150, 23, 20, 1200, p => new Serpent(p, "Royal Serpent")),
        new("骷髅洞穴", "Mummy", 260, 30, 20, 1800, p => MakeMummy(p)),
        new("骷髅洞穴", "Carbon Ghost", 190, 25, 20, 1150, p => new Ghost(p, "Carbon Ghost")),
        new("骷髅洞穴", "Iridium Bat", 300, 30, 22, 3550, p => new Bat(p, 171)),
        new("骷髅洞穴", "Iridium Crab", 240, 15, 20, 3000, p => MakeRockCrab(p, "Iridium Crab")),
        new("骷髅洞穴", "Putrid Ghost", 500, 25, 25, 1900, p => new Ghost(p, "Putrid Ghost")),
        new("骷髅洞穴", "Shadow Sniper", 300, 18, 20, 1550, p => new Shooter(p, "Shadow Sniper")),
        new("骷髅洞穴", "Spider", 200, 15, 15, 1300, p => new Leaper(p)),
        // ── 火山地牢 ──
        new("火山地牢", "Lava Lurk", 220, 15, 12, 1350, p => MakeLavaLurk(p)),
        new("火山地牢", "Hot Head", 250, 18, 16, 1900, p => MakeHotHead(p)),
        new("火山地牢", "Magma Sprite", 220, 15, 15, 1300, p => new Bat(p, -555)),
        new("火山地牢", "Magma Duggy", 380, 16, 18, 2050, p => MakeDuggy(p, true)),
        new("火山地牢", "Magma Sparker", 310, 15, 17, 1500, p => new Bat(p, -556)),
        new("火山地牢", "False Magma Cap", 290, 15, 14, 1600, p => MakeRockCrab(p, "False Magma Cap")),
        new("火山地牢", "Dwarvish Sentry", 300, 18, 15, 3200, p => new DwarvishSentry(p)),
        // ── 危险/特殊 ──
        new("危险/特殊", "Big Slime", 60, 5, 7, 850, p => MakeBigSlime(p)),
        new("危险/特殊", "Tiger Slime", 415, 23, 20, 1800, p => MakeTigerSlime(p)),
        new("危险/特殊", "Pepper Rex", 300, 15, 7, 1400, p => new DinoMonster(p)),
        new("危险/特殊", "Skeleton Warrior", 300, 12, 15, 1800, p => MakeSkeletonWarrior(p)),
        new("危险/特殊", "Wilderness Golem", 30, 5, 5, 900, p => MakeRockGolem(p, false)),
        new("危险/特殊", "Iridium Golem", 30, 5, 5, 900, p => MakeRockGolem(p, true)),
    };

    // ── Arena-safe factories ──
    // Several vanilla constructors produce monsters that can't be fought in the arena
    // (spawn hidden / immune / scripted-away), so they're rewrapped here in the way the
    // game itself does when spawning them in the wild. All verified against the game 1.6.15
    // decompiled source + Data/Monsters on 2026-07-31.

    /// <summary>Duggy starts IsInvisible=true (it pops out only via its update AI, which the
    /// arena freezes). Pop it out immediately so it can be seen and hit.</summary>
    public static Monster MakeDuggy(Vector2 p, bool magma)
    {
        var m = magma ? new Duggy(p, true) : new Duggy(p);
        m.IsInvisible = false;
        return m;
    }

    /// <summary>Wilderness/Iridium golem: the vanilla difficultyMod ctor only becomes
    /// "Iridium Golem" at mod &gt;= 9 on the wilderness farm, so force the name + data +
    /// texture explicitly.</summary>
    public static Monster MakeRockGolem(Vector2 p, bool iridium)
    {
        var g = new RockGolem(p, 2); // difficultyMod 2 → "Wilderness Golem" (mod < 9)
        if (iridium)
        {
            g.Name = "Iridium Golem";
            g.reloadSprite(); // Characters\Monsters\Iridium Golem exists
            g.MaxHealth = 500;
            g.Health = 500;
            g.DamageToFarmer = 32;
            g.ExperienceGained = 25;
        }
        g.Sprite.currentFrame = 0; // wake pose: vanilla spawns them as a stone pile (frame 16)
        return g;
    }

    /// <summary>Stone Golem (mine): plain ctor, woken from the sleeping pose so it isn't a
    /// pile of rocks.</summary>
    public static Monster MakeStoneGolem(Vector2 p)
    {
        var s = new RockGolem(p);
        s.Sprite.currentFrame = 0;
        return s;
    }

    /// <summary>Tiger Slime is created via makeTigerSlime(), not the mineLevel ctor (which
    /// would give a purple Sludge at level 999).</summary>
    public static Monster MakeTigerSlime(Vector2 p)
    {
        var m = new GreenSlime(p);
        m.makeTigerSlime();
        return m;
    }

    /// <summary>LavaLurk starts submerged (damage-immune) and only emerges on lava tiles.
    /// The arena is dry, so force it emerged with a killable AI.</summary>
    public static Monster MakeLavaLurk(Vector2 p)
    {
        var m = new LavaLurk(p);
        m.currentState.Value = LavaLurk.State.Emerged;
        m.stunTime.Value = 0;
        m.focusedOnFarmers = false;
        return m;
    }

    /// <summary>HotHead explodes into a bomb when killed (DropBomb), which would hurt the
    /// player in the arena — swap to a plain Monster so it dies clean.</summary>
    public static Monster MakeHotHead(Vector2 p)
    {
        var m = new Monster("Hot Head", p);
        m.Sprite.SpriteWidth = 16;
        m.Sprite.SpriteHeight = 16;
        m.Sprite.UpdateSourceRect();
        return m;
    }

    /// <summary>BigSlime splits into 2-5 unfrozen GreenSlimes on death (they'd swarm the
    /// arena). Rebuild as a plain Monster (32x32 frames) so it dies clean.</summary>
    public static Monster MakeBigSlime(Vector2 p)
    {
        var m = new Monster("Big Slime", p);
        m.Sprite.SpriteWidth = 32;
        m.Sprite.SpriteHeight = 32;
        m.Sprite.UpdateSourceRect();
        return m;
    }

    /// <summary>"Skeleton Warrior" has no entity class and no texture in 1.6 (vanilla only
    /// shows it in credits). Build a Skeleton (correct 16x32 body) and restat it to the
    /// catalog's advertised values.</summary>
    public static Monster MakeSkeletonWarrior(Vector2 p)
    {
        var m = new Skeleton(p, false);
        m.Name = "Skeleton Warrior";
        m.MaxHealth = 300;
        m.Health = 300;
        m.DamageToFarmer = 12;
        m.ExperienceGained = 15;
        return m;
    }

    /// <summary>Crabs block all damage while Sprite.currentFrame % 4 == 0 (frame 0 = shell
    /// stance). The arena freezes animation, so the frame never leaves 0 and they are
    /// immortal. Pin the frame to 1 (not divisible by 4) so the block check never fires.</summary>
    public static Monster MakeRockCrab(Vector2 p, string name)
    {
        var m = new RockCrab(p, name);
        m.Sprite.currentFrame = 1;
        m.Sprite.UpdateSourceRect();
        return m;
    }

    /// <summary>Spiker is damage-immune by design (vanilla kills it by crushing rocks).
    /// Rebuild it as a plain Monster with the Spiker data row so it can be hit, keeping
    /// the spiky 16x16 look.</summary>
    public static Monster MakeSpiker(Vector2 p)
    {
        var m = new Monster("Spiker", p);
        m.Sprite.SpriteWidth = 16;
        m.Sprite.SpriteHeight = 16;
        m.Sprite.UpdateSourceRect();
        m.Slipperiness = 3;
        return m;
    }

    /// <summary>Mummy revives 10s after a non-bomb kill (vanilla anti-frustration mechanic)
    /// — with swords you could literally never finish it. Rebuild as a plain Monster so it
    /// dies for good; the 16x32 frame layout keeps the Mummy texture rendering right.</summary>
    public static Monster MakeMummy(Vector2 p)
    {
        var m = new Monster("Mummy", p);
        m.Sprite.SpriteHeight = 32;
        m.Sprite.UpdateSourceRect();
        m.Slipperiness = 2;
        return m;
    }

    /// <summary>ShadowGuy's base ctor loads "Characters\Monsters\Shadow Guy" — that texture
    /// doesn't exist in the game content (only "Shadow Girl" ships, which is what vanilla
    /// reloadSprite picks for even X positions). Load the existing texture explicitly.</summary>
    public static Monster MakeShadowGuy(Vector2 p)
    {
        var m = new ShadowGuy(p);
        m.Sprite = new AnimatedSprite("Characters\\Monsters\\Shadow Girl");
        return m;
    }

    /// <summary>Grub starts pupating below half health and hatches into an UNFROZEN Fly
    /// after 4.5s (its update skips the stun check). Rebuild as a plain Monster so it
    /// just stands there and dies.</summary>
    public static Monster MakeGrub(Vector2 p)
    {
        var m = new Monster("Grub", p);
        m.Sprite.SpriteHeight = 24;
        m.Sprite.UpdateSourceRect();
        return m;
    }
}
