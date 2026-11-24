using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class ItemDetailsInput
    {
        internal static bool Matches(KeyCode binding, bool primaryHeld, bool otherShiftHeld)
            => binding != KeyCode.None && (primaryHeld
                || (binding == KeyCode.LeftShift || binding == KeyCode.RightShift) && otherShiftHeld);

        internal static bool Held
        {
            get
            {
                if (CheckpointPanel.SuppressHotkeys) return false;
                var binding = Plugin.CfgKeyDetails?.Value ?? KeyCode.LeftShift;
                if (binding == KeyCode.None) return false;
                bool other = binding == KeyCode.LeftShift ? Input.GetKey(KeyCode.RightShift)
                    : binding == KeyCode.RightShift && Input.GetKey(KeyCode.LeftShift);
                return Matches(binding, Input.GetKey(binding), other);
            }
        }
    }
}
