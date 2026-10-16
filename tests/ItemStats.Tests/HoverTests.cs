using JustBeBetter.ItemStats;
using RoR2;

// Inventory hover text, an item as it's held, after FillCatalog
internal static partial class Program
{
    private static void RunHover()
    {
        Test("Hover over a held Tougher Times x6 shows its 47.37%, no arrow or heading", HoverHeldValue);
        Test("Hover with no holder, as on the run report, shows the description alone", HoverNoHolder);
        Test("Hover with full text off shows the one-liner and still the stats", HoverShortText);
        Test("Hover over a Hex item keeps the game's Hallowed Shrine hint", HoverHexKeepsSuffix);
        Test("Hover over equipment with no holder shows its base cooldown after its stats", HoverEquipmentCooldown);
        Test("Equipment with stats off or no cooldown gets no cooldown line", HoverEquipmentNoCooldown);
    }

    private static void HoverHeldValue()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribeHeld(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 6)), true, true);
        Contains("<style=cIsUtility>47.37%</style>", s);
        Lacks("→", s);
        Lacks("With this item", s);
        Contains(Description(RoR2Content.Items.Bear), s);
    }

    private static void HoverNoHolder()
    {
        Equal(Description(RoR2Content.Items.Bear), ItemStatText.DescribeHeld(RoR2Content.Items.Bear, null, true, true));
    }

    // With no language loaded here the full token counts as missing, so it falls back as the game would
    private static string Description(ItemDef item)
    {
        return Language.GetString(Language.IsTokenInvalid(item.descriptionToken) ? item.pickupToken : item.descriptionToken);
    }

    private static void HoverShortText()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribeHeld(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 6)), false, true);
        Contains(Language.GetString(RoR2Content.Items.Bear.pickupToken), s);
        Contains("47.37%", s);
    }

    // ItemIcon.SetItemIndex wraps a Hex's one-liner in HEXTIER_PICKUP_SUFFIX, which has to survive the swap
    private static void HoverHexKeepsSuffix()
    {
        var hex = (ItemDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ItemDef).GetField("deprecatedTier", flags)!.SetValue(hex, ItemTier.Hex);
        typeof(ItemDef).GetField("_itemIndex", flags)!.SetValue(hex, (ItemIndex)0);
        hex.tags = System.Array.Empty<ItemTag>();
        hex.nameToken = "ITEM_HEXCURSE_NAME";
        hex.pickupToken = "ITEM_HEXCURSE_PICKUP";
        hex.descriptionToken = "ITEM_HEXCURSE_DESC";
        string game = Language.GetStringFormatted("HEXTIER_PICKUP_SUFFIX", Language.GetString(hex.pickupToken));
        Equal(game, ItemStatText.DescribeHeld(hex, null, false, true, game));
    }

    private static EquipmentDef Launcher()
    {
        EquipmentDef e = RoR2Content.Equipment.CommandMissile;
        e.pickupToken ??= "CommandMissile_PICKUP";
        e.descriptionToken ??= "CommandMissile_DESC";
        e.cooldown = 45f;
        return e;
    }

    private static void HoverEquipmentCooldown()
    {
        string s = ItemStatText.DescribeHeld(Launcher(), null, true, true);
        Contains("\nCooldown: <style=cIsUtility>45 s</style>", s);
        Lacks("reduction", s);
        Lacks("\n\n\nCooldown", s);
    }

    private static void HoverEquipmentNoCooldown()
    {
        Lacks("Cooldown", ItemStatText.DescribeHeld(Launcher(), null, true, false));
        EquipmentDef e = Launcher();
        e.cooldown = 0f;
        Lacks("Cooldown", ItemStatText.DescribeHeld(e, null, true, true));
        e.cooldown = 45f;
    }
}
