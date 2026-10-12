using RoR2;

namespace JustBeBetter.Safety
{
    internal static class PickerTypes
    {
        // Other prompts reuse NetworkUIPromptController
        // CraftingController derives from PickupPickerController
        internal static bool IsPicker(NetworkUIPromptController prompt)
        {
            return prompt.GetComponent<PickupPickerController>()
                || prompt.GetComponent<TieredPickupPickerController>()
                || prompt.GetComponent<DroneScrapperPickerController>();
        }
    }
}
