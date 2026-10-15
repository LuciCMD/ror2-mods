using System.Linq;
using JustBeBetter.ImmuneBar;

// Immune Health Bar's rule for what counts as immune, and the boss bar's whole-group check
internal static partial class Program
{
    private static void RunImmuneBar()
    {
        Test("Immune bar: nothing set is not immune", ImmuneNothing);
        Test("Immune bar: each full immunity counts on its own", ImmuneEachCounts);
        Test("Immune bar: god mode is named first when several apply", ImmuneGodModeFirst);
        Test("Immune bar: a boss group is immune only while every living member is", ImmuneGroup);
        Test("Immune bar: an empty boss group is not immune", ImmuneEmptyGroup);
    }

    private static void ImmuneNothing()
    {
        Expect(ImmuneRules.Reason(new ImmuneFlags(false, false, false, false, false, false)) == null, "no flags gave a reason");
    }

    private static void ImmuneEachCounts()
    {
        (ImmuneFlags Flags, string Reason)[] cases =
        {
            (new ImmuneFlags(true, false, false, false, false, false), "god mode"),
            (new ImmuneFlags(false, true, false, false, false, false), "Immune buff"),
            (new ImmuneFlags(false, false, true, false, false, false), "hidden invincibility"),
            (new ImmuneFlags(false, false, false, true, false, false), "reject all damage"),
            (new ImmuneFlags(false, false, false, false, true, false), "Sojourn vehicle"),
            (new ImmuneFlags(false, false, false, false, false, true), "throne shield"),
        };
        foreach ((ImmuneFlags flags, string reason) in cases)
        {
            Equal(reason, ImmuneRules.Reason(flags) ?? "null");
        }
    }

    private static void ImmuneGodModeFirst()
    {
        Equal("god mode", ImmuneRules.Reason(new ImmuneFlags(true, true, true, true, true, true)) ?? "null");
    }

    private static void ImmuneGroup()
    {
        Expect(ImmuneRules.AllImmune(new[] { true }), "one immune boss");
        Expect(ImmuneRules.AllImmune(new[] { true, true }), "two immune bosses");
        Expect(!ImmuneRules.AllImmune(new[] { true, false }), "one of two immune counted as immune");
        Expect(!ImmuneRules.AllImmune(new[] { false }), "a hittable boss counted as immune");
    }

    private static void ImmuneEmptyGroup()
    {
        Expect(!ImmuneRules.AllImmune(Enumerable.Empty<bool>()), "no living members counted as immune");
    }
}
