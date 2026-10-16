using System.Globalization;
using System.Text;

namespace JustBeBetter.Shared
{
    // A cooldown line, the same for skills and equipment: what it is now, with the base and the reduction when
    // items change it
    internal static class CooldownText
    {
        private const float Same = 0.005f;

        internal static void Append(StringBuilder text, float baseCooldown, float now)
        {
            text.Append("\nCooldown: <style=cIsUtility>").Append(Seconds(now)).Append("</style>");

            // A penalty such as Seeker's failed Meditate can push it past the base, which isn't a reduction
            if (now > baseCooldown + Same)
            {
                text.Append(" <style=cStack>(base ").Append(Seconds(baseCooldown)).Append(")</style>");
            }
            else if (baseCooldown - now > Same)
            {
                float reduction = (1f - (now / baseCooldown)) * 100f;
                text.Append(" <style=cStack>(base ").Append(Seconds(baseCooldown)).Append(", ")
                    .Append(reduction.ToString("0.#", CultureInfo.InvariantCulture)).Append("% reduction)</style>");
            }
        }

        private static string Seconds(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        }
    }
}
