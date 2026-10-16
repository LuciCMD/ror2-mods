namespace JustBeBetter
{
    // One part of the mod, enabled at launch when its config section's Enabled is on
    internal interface IFeature
    {
        void Enable();

        void Disable();
    }
}
