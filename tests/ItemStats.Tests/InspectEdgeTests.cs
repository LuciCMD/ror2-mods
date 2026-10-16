using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JustBeBetter.ItemStats;
using RoR2;
using RoR2.UI;

// Clicked items and equipment at their edges, after FillCatalog
// InspectDetails can't run outside Unity, so the reflection tests check every member it reads exists in the
// installed game, and IsGameDescription is mirrored as written and held against the game's own InspectInfo
// A change to InspectDetails has to be mirrored here
internal static partial class Program
{
    private static void RunInspectEdge()
    {
        Test("Inspect edge: every member InspectDetails reads exists in the installed game with the type it expects", InspectEdgeReflected);
        Test("Inspect edge: the game's InspectInfo for a plain item carries its description token", InspectEdgeGamePlain);
        Test("Inspect edge: the game's InspectInfo for a Hex is what IsGameDescription expects", InspectEdgeGameHex);
        Test("Inspect edge: a Hex marked WorldUnique isn't wrapped by the game, and still counts as its text", InspectEdgeWorldUniqueHex);
        Test("Inspect edge: disabled, undiscovered, empty or other items' text is left alone", InspectEdgeLeftAlone);
        Test("Inspect edge: a Hex's clicked text keeps the game's Hallowed Shrine hint and adds stats", InspectEdgeHexKeepsHint);
        Test("Inspect edge: a clicked item's text is the game's then the stats, as hover has them", InspectEdgeStatsAppended);
        Test("Inspect edge: a clicked item with no inventory behind it keeps the game's text", InspectEdgeNoInventory);
    }

    private const BindingFlags InspectPrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InspectAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void InspectEdgeReflected()
    {
        FieldInfo? description = typeof(InspectPanelController).GetField("InspectDescription", InspectPrivate);
        Expect(description != null && description.FieldType == typeof(LanguageTextMeshController), "InspectPanelController.InspectDescription");
        FieldInfo? inventory = typeof(ItemInventoryDisplay).GetField("inventory", InspectPrivate);
        Expect(inventory != null && inventory.FieldType == typeof(Inventory), "ItemInventoryDisplay.inventory");
        FieldInfo? target = typeof(EquipmentIcon).GetField("targetInventory", InspectPrivate);
        Expect(target != null && target.FieldType == typeof(Inventory), "EquipmentIcon.targetInventory");
        FieldInfo? data = typeof(EquipmentIcon).GetField("currentDisplayData", InspectPrivate);
        Expect(data != null, "EquipmentIcon.currentDisplayData");
        FieldInfo? def = data!.FieldType.GetField("equipmentDef", InspectAny);
        Expect(def != null && def.FieldType == typeof(EquipmentDef), "currentDisplayData.equipmentDef");
        Expect(typeof(ItemIcon).GetMethod("ItemClicked", Type.EmptyTypes) != null, "ItemIcon.ItemClicked()");
        Expect(typeof(EquipmentIcon).GetMethod("ItemClicked", Type.EmptyTypes) != null, "EquipmentIcon.ItemClicked()");
        Expect(typeof(ItemIcon).GetProperty("inspectPanel")?.PropertyType == typeof(InspectPanelController), "ItemIcon.inspectPanel");
        Expect(typeof(EquipmentIcon).GetProperty("inspectPanel")?.PropertyType == typeof(InspectPanelController), "EquipmentIcon.inspectPanel");
        Expect(typeof(LanguageTextMeshController).GetProperty("token")?.CanWrite == true, "LanguageTextMeshController.token");
    }

    // InspectDetails.IsGameDescription, as written
    private static bool IsGameDescriptionMirror(string? token, ItemDef item)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(item.descriptionToken))
        {
            return false;
        }

        if (token == item.descriptionToken)
        {
            return true;
        }

        return item.tier == ItemTier.Hex
            && token == Language.GetStringFormatted("HEXTIER_DESC_SUFFIX", Language.GetString(item.descriptionToken));
    }

    private static ItemDef HexItem(bool worldUnique)
    {
        var hex = (ItemDef)RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
        typeof(ItemDef).GetField("deprecatedTier", InspectPrivate)!.SetValue(hex, ItemTier.Hex);
        typeof(ItemDef).GetField("_itemIndex", InspectPrivate)!.SetValue(hex, (ItemIndex)0);
        hex.tags = worldUnique ? new[] { ItemTag.WorldUnique } : Array.Empty<ItemTag>();
        hex.nameToken = "ITEM_HEXCURSE_NAME";
        hex.pickupToken = "ITEM_HEXCURSE_PICKUP";
        hex.descriptionToken = "ITEM_HEXCURSE_DESC";
        return hex;
    }

    // The game's ItemDef to InspectInfo conversion, the token ItemIcon.ItemClicked hands the panel
    private static string? GameToken(ItemDef item)
    {
        InspectInfo info;
        try
        {
            info = item;
        }
        catch (Exception e)
        {
            throw new AssertFail("the game's InspectInfo conversion can't run outside the game: " + e.GetType().Name + " " + e.Message);
        }

        return info.DescriptionToken;
    }

    private static void InspectEdgeGamePlain()
    {
        ItemDef bear = RoR2Content.Items.Bear;
        bear.descriptionToken ??= "ITEM_BEAR_DESC";
        string? token = GameToken(bear);
        Equal(bear.descriptionToken, token ?? "(null)");
        Expect(IsGameDescriptionMirror(token, bear), "the game's own token wasn't taken as its text");
    }

    private static void InspectEdgeGameHex()
    {
        ItemDef hex = HexItem(false);
        string? token = GameToken(hex);
        Expect(token != hex.descriptionToken, "setup, the game should wrap a Hex's description");
        Expect(IsGameDescriptionMirror(token, hex), $"the game's Hex text [{token}] wasn't taken as its text, so it gets no stats");
    }

    private static void InspectEdgeWorldUniqueHex()
    {
        ItemDef hex = HexItem(true);
        string? token = GameToken(hex);
        Equal(hex.descriptionToken, token ?? "(null)");
        Expect(IsGameDescriptionMirror(token, hex), "a WorldUnique Hex's plain token wasn't taken as its text");
    }

    // InspectPanelController.Show swaps in these for a disabled or undiscovered item
    private static void InspectEdgeLeftAlone()
    {
        ItemDef bear = RoR2Content.Items.Bear;
        bear.descriptionToken ??= "ITEM_BEAR_DESC";
        string disabled = Language.GetStringFormatted(Language.GetString("INSPECT_ITEM_DISABLED"), Language.GetString(bear.descriptionToken));
        Expect(!IsGameDescriptionMirror(disabled, bear) || disabled == bear.descriptionToken, "a disabled item's text was replaced");
        Expect(!IsGameDescriptionMirror("UNIDENTIFIED_DESC", bear), "an undiscovered item's text was replaced");
        Expect(!IsGameDescriptionMirror(null, bear), "a null token was taken");
        Expect(!IsGameDescriptionMirror(string.Empty, bear), "an empty token was taken");
        Expect(!IsGameDescriptionMirror(RoR2Content.Items.Syringe.descriptionToken ?? "ITEM_SYRINGE_DESC", bear), "another item's text was taken");

        ItemDef empty = HexItem(false);
        empty.descriptionToken = string.Empty;
        Expect(!IsGameDescriptionMirror(string.Empty, empty), "an item with no description was taken");
        empty.descriptionToken = null!;
        Expect(!IsGameDescriptionMirror("X", empty), "an item with a null description was taken");
    }

    private static void InspectEdgeHexKeepsHint()
    {
        ItemDef hex = HexItem(false);
        string game = Language.GetString(GameToken(hex)!);
        string s = ItemStatText.DescribeHeld(hex, Holding((hex, 1)), false, true, game);
        Expect(s.StartsWith(game, StringComparison.Ordinal), $"the game's text [{game}] wasn't kept at the start of [{s}]");
    }

    private static void InspectEdgeStatsAppended()
    {
        Utils.CapChance = true;
        ItemDef bear = RoR2Content.Items.Bear;
        bear.descriptionToken ??= "ITEM_BEAR_DESC";
        string game = Language.GetString(bear.descriptionToken);
        string s = ItemStatText.DescribeHeld(bear, Holding((bear, 6)), false, true, game);
        Expect(s.StartsWith(game, StringComparison.Ordinal), $"the game's text wasn't kept: {s}");
        Contains("47.37%", s);
        Lacks("With this item", s);
    }

    private static void InspectEdgeNoInventory()
    {
        ItemDef bear = RoR2Content.Items.Bear;
        bear.descriptionToken ??= "ITEM_BEAR_DESC";
        string game = Language.GetString(bear.descriptionToken);
        Equal(game, ItemStatText.DescribeHeld(bear, null, false, true, game));
    }
}
