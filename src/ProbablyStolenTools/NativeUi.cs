using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    // Use the game's public mod UI prefabs, independently of Inspector's DLL.
    internal static class NativeUi
    {
        internal static CustomUIBuilder Create(string id, string title, float width, float height,
            bool draggable, out CustomUIWindow window)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) { window = null; return null; }
            var previous = manager.GetWindow(id);
            if (previous != null && previous.IsAlive) manager.CloseWindow(id);
            var builder = manager.CreateWindow(id, title, "overlay", null)
                .SetDraggable(draggable).SetCloseOnEscape(false).SetSize(width, height);
            window = manager.GetWindow(id);
            if (window == null || !window.IsAlive || window.ContentRoot == null || window.frame == null)
                throw new InvalidOperationException("Native window prefab is not ready: " + id);
            if (window.frame.closeButton != null) window.frame.closeButton.gameObject.SetActive(false);
            if (window.frame.dragHandle != null) window.frame.dragHandle.ignoreClamp = true;
            return builder;
        }

        internal static void FixedSize(CustomUIWindow window, float width, float height)
        {
            var fitter = window.Rect.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            var content = window.ContentRoot.GetComponent<LayoutElement>()
                ?? window.ContentRoot.gameObject.AddComponent<LayoutElement>();
            content.minHeight = 0;
            content.flexibleHeight = 1;
            window.SetSize(width, height);
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.Rect);
        }

        internal static PanelBounds Bounds(RectTransform rect, RectTransform parent)
        {
            var topLeft = parent.InverseTransformPoint(rect.TransformPoint(
                new Vector3(rect.rect.xMin, rect.rect.yMax, 0)));
            var bottomRight = parent.InverseTransformPoint(rect.TransformPoint(
                new Vector3(rect.rect.xMax, rect.rect.yMin, 0)));
            return new PanelBounds(topLeft.x - parent.rect.xMin, parent.rect.yMax - topLeft.y,
                bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
        }

        internal static void Place(CustomUIWindow window, RectTransform parent, PanelBounds bounds)
        {
            var current = Bounds(window.Rect, parent);
            // UIDragHandler may reparent a dragged window into the letterbox dock.
            window.Rect.position += parent.TransformVector(
                new Vector3(bounds.X - current.X, current.Y - bounds.Y, 0));
        }

        internal static bool Topmost(RectTransform target)
        {
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (system == null || target == null) return false;
            var pointer = new UnityEngine.EventSystems.PointerEventData(system) { position = Input.mousePosition };
            var hits = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            system.RaycastAll(pointer, hits);
            return hits.Count > 0 && (hits[0].gameObject.transform == target
                || hits[0].gameObject.transform.IsChildOf(target));
        }
    }
}
