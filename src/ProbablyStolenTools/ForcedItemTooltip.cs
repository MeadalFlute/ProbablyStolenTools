#if MONO
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class ForcedItemTooltip
    {
        private static bool _shown;
        private static GameItemElement _target;
        private static RichText _content;
        private static float _refreshAt;

        internal static void Tick()
        {
            try
            {
                var store = Plugin.Store();
                bool ready = ItemDetailsInput.Held && store != null && !store.cinematicMode
                    && MainMenuUIController.Instance == null && !Rewind.Restoring && !Rewind.Settling
                    && !CustomerCheckpoint.Restoring && !SaveManager.IsDecoding && !NativeLoadDiagnostics.Active
                    && !CurrentConverterHelper.isNightCycle && !UIDragHandler.isDraggingAnyUI
                    && OverlayHandler.current != null && UnityEngine.EventSystems.EventSystem.current != null;
                if (!ready) { Clear(); return; }
                var drag = ItemMouseDragHandler.current;
                var source = drag?.IsDraggingItem == true ? drag.GetCurrentItem() as GameItemElement : null;
                var item = RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition, source?.fakeBackground);
                if (item == null || ReferenceEquals(item, source)
                    || ItemMultiSelectHandler.current?.IsGroupDraggingItem(item) == true || !item.HasTooltip())
                { Clear(); return; }
                if (!ReferenceEquals(_target, item) || _content == null || Time.unscaledTime >= _refreshAt)
                {
                    _target = item;
                    _content = item.GetTooltip();
                    _refreshAt = Time.unscaledTime + .1f;
                }
                // Render only. Native drag, transfer and tool callbacks keep their original inputs.
                OverlayHandler.current.ShowTooltip(Input.mousePosition,
                    ItemTooltipHandler.current != null ? ItemTooltipHandler.current.tooltipTranslation : new Vector2(32, 0),
                    _content, 100);
                _shown = true;
            }
            catch (System.Exception e) { Clear(); DetailedCompositionTooltip.Warn(e); }
        }

        internal static void Clear()
        {
            _target = null;
            _content = null;
            if (!_shown) return;
            _shown = false;
            OverlayHandler.current?.HideTooltip();
        }
    }
}
#endif
