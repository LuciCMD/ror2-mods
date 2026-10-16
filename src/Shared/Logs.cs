using System;
using BepInEx.Logging;

namespace JustBeBetter.Shared
{
    // Every Info line goes through here, so the Verbose Logging switch reaches all of them
    // Warnings and errors skip it, they always matter
    internal static class Logs
    {
        private static Func<bool> verbose = () => false;

        // Read on every line, so the switch takes effect straight away
        internal static bool VerboseOn => verbose();

        internal static void Follow(Func<bool> on)
        {
            verbose = on ?? throw new ArgumentNullException(nameof(on));
        }

        // For the few lines a player's log should keep, such as where saves live or why a feature can't work
        internal static void Info(this ManualLogSource log, string message)
        {
            log.LogInfo(message);
        }

        // Written as Info, so BepInEx's own log levels don't hide it once asked for
        // The message is built either way, so work done inside it, a save area's capture say, still runs
        internal static void Verbose(this ManualLogSource log, string message)
        {
            if (VerboseOn)
            {
                log.LogInfo(message);
            }
        }
    }
}
