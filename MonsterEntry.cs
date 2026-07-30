using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Monsters;

namespace MonsterArena;

/// <summary>One sellable row in the arena shop menu: draws the real monster sprite + name + price.
/// Implements ISalable so the vanilla ShopMenu can render it without custom drawing code.</summary>
public class MonsterEntry : ISalable
{
    private readonly Func<Vector2, Monster> factory;
    private Monster? icon;

    public string MonsterName { get; }
    public int Price { get; }
    public int Health { get; }
    public int Damage { get; }
    public int Experience { get; }

    public MonsterEntry(string monsterName, int price, int health, int damage, int experience, Func<Vector2, Monster> factory)
    {
        this.MonsterName = monsterName;
        this.Price = price;
        this.Health = health;
        this.Damage = damage;
        this.Experience = experience;
        this.factory = factory;
    }

    /// <summary>Create the actual live monster for the arena at a position.</summary>
    public Monster CreateMonster(Vector2 pixelPosition) => this.factory(pixelPosition);

    private Monster GetIcon()
    {
        if (this.icon == null)
        {
            this.icon = this.factory(Vector2.Zero);
            try { this.icon.Sprite.CurrentFrame = 0; } catch { }
        }
        return this.icon;
    }

    // --- ISalable ---
    public string TypeDefinitionId => "(Salable)";
    public string GetItemTypeId() => "(Salable)";
    public string QualifiedItemId => "(Salable)" + this.MonsterName;
    public string DisplayName => this.GetIcon().displayName ?? this.MonsterName;
    public string Name => this.MonsterName;
    public bool IsRecipe { get; set; }

    // ShopMenu multiplies the buy count by item.Stack and writes it back, so letting Stack grow
    // makes the quantity balloon (99 -> 625 -> 15625). Pin it to 1 so one click = one monster.
    private int stack = 1;
    public int Stack { get => 1; set => this.stack = 1; }
    public int Quality { get; set; }

    public bool ShouldDrawIcon() => true;

    public void drawInMenu(SpriteBatch spriteBatch, Vector2 location, float scaleSize, float transparency, float layerDepth, StackDrawType drawStackNumber, Color color, bool drawShadow)
    {
        Monster m = this.GetIcon();
        var sprite = m.Sprite;
        var tex = sprite.Texture;
        if (tex == null)
            return;
        var src = sprite.SourceRect;
        // fit the monster frame into the 64x64 icon cell, centered
        float scale = 4f * scaleSize;
        float w = src.Width * scale, h = src.Height * scale;
        float dx = location.X + (64f * scaleSize - w) / 2f;
        float dy = location.Y + (64f * scaleSize - h) / 2f;
        spriteBatch.Draw(tex, new Vector2(dx, dy), src, Color.White * transparency, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth);
    }

    public string getDescription()
    {
        return $"{this.DisplayName}\n生命 {this.Health}   伤害 {this.Damage}   战斗经验 {this.Experience}";
    }

    public int maximumStackSize() => 999;
    public int addToStack(Item stack) => 0;
    public int sellToStorePrice(long specificPlayerID = -1) => 0;
    public int salePrice(bool ignoreProfitMargins = false) => this.Price;
    public bool appliesProfitMargins() => false;
    public bool actionWhenPurchased(string shopId) => true; // discard: we don't give an item
    public bool canStackWith(ISalable other) => false;
    public bool CanBuyItem(Farmer farmer) => true;
    public bool IsInfiniteStock() => false;
    public ISalable GetSalableInstance() => this;
    public void FixStackSize() { }
    public void FixQuality() { }
}
