// Stands in for BepInEx's logger so the real source compiles without BepInEx or the mod profile
namespace BepInEx.Logging
{
    internal sealed class ManualLogSource
    {
        public System.Collections.Generic.List<string> Lines { get; } = new();

        public void LogInfo(object o) => Lines.Add("INFO " + o);

        public void LogError(object o) => Lines.Add("ERROR " + o);

        public void LogWarning(object o) => Lines.Add("WARN " + o);
    }
}
