using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace PaPHideoutServer;

/// <summary>
/// A server mod's say in what the machine does to a weapon. Register one with <see cref="PaPServerApi.Register"/>;
/// every callback is optional and runs inside PaP's own item event, so whatever it changes through the
/// <see cref="PaPWeaponContext"/> reaches the client in the same reply (and, in the hideout's weapons mode, the gun
/// in the player's hands).
/// </summary>
public sealed class PaPWeaponHandler
{
    /// <summary>For the log.</summary>
    public string Name { get; init; } = "unnamed";

    /// <summary>Which weapons this handler is for; null means every weapon. Check the template, or ids your mod keeps.</summary>
    public Func<PaPWeaponContext, bool>? Matches { get; init; }

    /// <summary>Before the player pays: return a message to refuse the upgrade (shown to the player), or null to allow it.</summary>
    public Func<PaPWeaponContext, string?>? CanUpgrade { get; init; }

    /// <summary>The weapon has just been paid for and infused: add parts, top up ammo, and so on.</summary>
    public Action<PaPWeaponContext>? Upgraded { get; init; }

    /// <summary>Its 115 powers were just taken off: undo what <see cref="Upgraded"/> did, if it should go.</summary>
    public Action<PaPWeaponContext>? Stripped { get; init; }
}

/// <summary>
/// The weapon in the machine, and the tools to change it so the client stays in step. Change items only through
/// these methods, or call <see cref="MarkChanged"/> after editing one directly: the client is only told about the
/// items marked here.
/// </summary>
public sealed class PaPWeaponContext
{
    internal PaPWeaponContext(MongoId sessionId, PmcData pmc, Item weapon, bool cosmetic)
    {
        SessionId = sessionId;
        Pmc = pmc;
        Weapon = weapon;
        Cosmetic = cosmetic;
    }

    public MongoId SessionId { get; }
    public PmcData Pmc { get; }

    /// <summary>The weapon (the profile's own: in weapons mode the client holds a copy, which PaP refreshes).</summary>
    public Item Weapon { get; }

    /// <summary>The player chose looks and sound only (no weapon bonuses). Upgrades only.</summary>
    public bool Cosmetic { get; }

    /// <summary>Every item of the profile's inventory.</summary>
    public List<Item> Items => Pmc.Inventory?.Items ?? [];

    // Top-level items (a weapon, or the backpack or case it sits in) whose contents changed
    internal readonly HashSet<string> ChangedRoots = [];

    /// <summary>The weapon and everything on or in it.</summary>
    public List<Item> WeaponItems() => Subtree(Weapon);

    /// <summary>What sits in a slot of the weapon (or of <paramref name="parent"/>, one of its parts), or null.</summary>
    public Item? InSlot(string slotId, Item? parent = null)
    {
        string parentId = (parent ?? Weapon).Id.ToString();
        return Items.FirstOrDefault(i => i.ParentId == parentId && i.SlotId == slotId);
    }

    /// <summary>
    /// Puts a new item of <paramref name="template"/> into a slot of the weapon (or of one of its parts), replacing
    /// whatever was there. Returns it, so more can be attached to it in turn.
    /// </summary>
    public Item Attach(MongoId template, string slotId, Item? parent = null, Upd? upd = null)
    {
        Item onto = parent ?? Weapon;
        if (InSlot(slotId, onto) is { } old)
            Remove(old);
        var item = new Item { Id = new MongoId(), Template = template, ParentId = onto.Id.ToString(), SlotId = slotId, Upd = upd };
        Items.Add(item);
        MarkChanged(onto);
        return item;
    }

    /// <summary>Removes an item and everything inside it.</summary>
    public void Remove(Item item)
    {
        MarkChanged(item); // the root first, while the item still links up to it
        foreach (Item gone in Subtree(item))
            Items.Remove(gone);
    }

    /// <summary>Sets a stack's count (ammo in a magazine, say).</summary>
    public void SetStack(Item item, double count)
    {
        item.Upd ??= new Upd();
        item.Upd.StackObjectsCount = count;
        MarkChanged(item);
    }

    /// <summary>Tells the client about an item edited directly (and everything it sits in).</summary>
    public void MarkChanged(Item item)
    {
        if (RootOf(item) is { } root)
            ChangedRoots.Add(root.Id.ToString());
    }

    private List<Item> Subtree(Item top)
    {
        var all = new List<Item> { top };
        var ids = new HashSet<string> { top.Id.ToString() };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (Item item in Items)
            {
                if (item.ParentId != null && ids.Contains(item.ParentId) && ids.Add(item.Id.ToString()))
                {
                    all.Add(item);
                    grew = true;
                }
            }
        }
        return all;
    }

    /// <summary>
    /// The item's top-level ancestor: the one sitting straight in the stash, the equipment or another of the
    /// inventory's own containers. The client can only add new items there, so that whole item is re-sent.
    /// </summary>
    private Item? RootOf(Item item)
    {
        HashSet<string> containers = TopContainers();
        Item current = item;
        for (int depth = 0; depth < 32; depth++)
        {
            if (current.ParentId == null || containers.Contains(current.ParentId))
                return current;
            Item? parent = Items.FirstOrDefault(i => i.Id.ToString() == current.ParentId);
            if (parent == null)
                return current;
            current = parent;
        }
        return null;
    }

    private HashSet<string> TopContainers()
    {
        BotBaseInventory? inventory = Pmc.Inventory;
        var ids = new HashSet<string>();
        if (inventory == null)
            return ids;
        foreach (MongoId? id in new[] { inventory.Stash, inventory.Equipment, inventory.QuestRaidItems, inventory.QuestStashItems, inventory.SortingTable })
        {
            if (id.HasValue)
                ids.Add(id.Value.ToString());
        }
        foreach (MongoId id in inventory.HideoutAreaStashes?.Values ?? Enumerable.Empty<MongoId>())
            ids.Add(id.ToString());
        return ids;
    }

    /// <summary>
    /// Every changed top-level item goes into the reply as deleted and then new again, whole, with the same ids:
    /// the client applies deletions first, and new items only straight into a top-level container (a new part on a
    /// gun would fail there). True if anything was sent.
    /// </summary>
    internal bool Report(ItemEventRouterResponse output)
    {
        if (ChangedRoots.Count == 0)
            return false;
        ItemChanges? changes = output.ProfileChanges?.TryGetValue(SessionId, out ProfileChange? change) == true ? change?.Items : null;
        if (changes == null)
            return false;
        changes.NewItems ??= [];
        changes.DeletedItems ??= [];
        foreach (string rootId in ChangedRoots)
        {
            Item? root = Items.FirstOrDefault(i => i.Id.ToString() == rootId);
            changes.DeletedItems.Add(new DeletedItem { Id = new MongoId(rootId) });
            if (root != null)
                changes.NewItems.AddRange(Subtree(root));
        }
        return true;
    }
}

/// <summary>
/// For other server mods: how the machine treats particular weapons. A wonder weapon tops up its ammo, a mystery
/// box gun gets better parts, a gun turns into its upgraded form. Register from your mod's OnLoad (any order).
/// </summary>
public static class PaPServerApi
{
    private static readonly List<PaPWeaponHandler> Handlers = [];

    /// <summary>Adds a handler; every matching handler runs, in the order they were registered.</summary>
    public static void Register(PaPWeaponHandler handler)
    {
        lock (Handlers)
            Handlers.Add(handler);
    }

    /// <summary>Adds a handler for one weapon template (on top of its own <see cref="PaPWeaponHandler.Matches"/>, if any).</summary>
    public static void Register(string templateId, PaPWeaponHandler handler)
    {
        Func<PaPWeaponContext, bool>? also = handler.Matches;
        Register(new PaPWeaponHandler
        {
            Name = handler.Name,
            Matches = c => c.Weapon.Template.ToString() == templateId && (also == null || also(c)),
            CanUpgrade = handler.CanUpgrade,
            Upgraded = handler.Upgraded,
            Stripped = handler.Stripped,
        });
    }

    private static List<PaPWeaponHandler> For(PaPWeaponContext context)
    {
        lock (Handlers)
            return Handlers.Where(h => h.Matches == null || h.Matches(context)).ToList();
    }

    /// <summary>The first refusal any handler gives, or null.</summary>
    internal static string? CanUpgrade(PaPWeaponContext context, Action<string> logError)
    {
        foreach (PaPWeaponHandler handler in For(context))
        {
            try
            {
                if (handler.CanUpgrade?.Invoke(context) is { Length: > 0 } refusal)
                    return refusal;
            }
            catch (Exception e)
            {
                logError($"[PaP] Weapon handler '{handler.Name}' failed its upgrade check (allowed anyway): {e.Message}");
            }
        }
        return null;
    }

    /// <summary>Runs each handler's step; one failing doesn't stop the others or the upgrade.</summary>
    internal static void Run(PaPWeaponContext context, Func<PaPWeaponHandler, Action<PaPWeaponContext>?> step, string what, Action<string> logError)
    {
        foreach (PaPWeaponHandler handler in For(context))
        {
            try
            {
                step(handler)?.Invoke(context);
            }
            catch (Exception e)
            {
                logError($"[PaP] Weapon handler '{handler.Name}' failed on {what}: {e.Message}");
            }
        }
    }
}
