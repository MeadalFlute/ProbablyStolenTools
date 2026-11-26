#if MONO
using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolenTools
{
    internal static class NativeWindowGestures
    {
        private sealed class PinnedWindow
        {
            internal PixelWindow Window;
            internal GameObject Panel;
            internal TextMeshProUGUI Title;
            internal TextOverflowModes Overflow;
            internal string RawTitle, DecoratedTitle;
            internal RectTransform Rect => Window != null ? Window.rectTransform : Panel != null ? Panel.transform as RectTransform : null;
            internal bool Visible => Window != null ? Window.IsVisible() : Panel != null && Panel.activeInHierarchy;
        }
        // UI-only state: never add callbacks, tags or decorated titles to the captured game graph.
        private static readonly List<PinnedWindow> Pinned = new();
        private static string _error;
        private static bool _pinPressAllowed;
        private static int _pinPointerId;
        internal static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool Available => !Rewind.Restoring && !Rewind.Settling
            && !CustomerCheckpoint.Restoring && !SaveManager.IsDecoding;
        private static bool PinBusy => WindowGestureRules.PinInteractionBusy(Input.GetMouseButton(0),
            ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem,
            ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging,
            UIDragHandler.isDraggingAnyUI);

        internal static void PointerPressed(PointerEventData pointer) => Guard(() =>
        {
            if (pointer == null) return;
            if (pointer.button == PointerEventData.InputButton.Left) _pinPressAllowed = false;
            if (pointer.button != PointerEventData.InputButton.Middle) return;
            _pinPointerId = pointer.pointerId;
            _pinPressAllowed = Available && !CheckpointPanel.SuppressHotkeys && Shift && !PinBusy && !pointer.dragging;
        });

        internal static bool ConsumePinClick(PointerEventData pointer)
        {
            try
            {
                if (pointer == null || pointer.button != PointerEventData.InputButton.Middle) return false;
                bool allowed = _pinPressAllowed && _pinPointerId == pointer.pointerId;
                _pinPressAllowed = false;
                return allowed && Available && !CheckpointPanel.SuppressHotkeys
                    && WindowGestureRules.PinClick(Shift, true, pointer.clickCount, PinBusy || pointer.dragging, true);
            }
            catch (Exception e) { _pinPressAllowed = false; Report(e); return false; }
        }

        internal static void Opened(PixelWindow window, bool added)
        {
            Front(window);
        }

        internal static void Clicked(TreeNodeRender node, PointerEventData pointer) => Guard(() =>
        {
            if (!Available || Plugin.CfgWindowPin?.Value != true || pointer == null) return;
            var window = node?.FindParentWindow();
            if (window == null || !window.canMove || !window.IsVisible()) return;
            // Only native title/background surfaces, never item, control or resize nodes.
            bool surface = node.parent == window.handler && node.element == null
                && (node.text != null || node.eventHandler?.mouseBeginDragFunc?.Count > 0);
            if (!surface) return;
            var entry = Find(window);
            entry ??= new PinnedWindow { Window = window };
            if (!WindowGestureRules.TogglePin(Pinned, entry)) { Restore(entry); Raise(window); return; }
            Decorate(entry);
            Raise(window);
        });

        internal static void Front(PixelWindow window) => Guard(() => Raise(window));

        private static void Raise(PixelWindow focused)
        {
            if (!Available || Plugin.CfgWindowPin?.Value != true || focused?.rectTransform == null) return;
            Raise(focused.rectTransform);
        }

        private static void Raise(RectTransform focused)
        {
            if (!Available || Plugin.CfgWindowPin?.Value != true || focused == null) return;
            foreach (var entry in Pinned)
            {
                // Preserve native modal layers, docking and the game's focus/visibility order.
                var rect = entry.Rect;
                if (entry.Visible && rect != null && rect.parent == focused.parent)
                    rect.SetAsLastSibling();
            }
        }

        internal static void Validated(PixelWindow window) => Guard(() =>
        {
            var entry = Find(window);
            if (entry != null) Decorate(entry);
        });

        private static PinnedWindow Find(PixelWindow window) => Pinned.Find(p => ReferenceEquals(p.Window, window));

        private static void Decorate(PinnedWindow entry)
        {
            if (entry.Panel != null) { DecorateTablet(entry); return; }
            var window = entry.Window;
            if (!window.hasTitle || window.rectTransform == null) return;
            if (entry.Title == null)
            {
                for (int i = 0; i < window.rectTransform.childCount; i++)
                {
                    var title = window.rectTransform.GetChild(i).GetComponent<TextMeshProUGUI>();
                    if (title == null) continue;
                    entry.Title = title;
                    entry.Overflow = title.overflowMode;
                    break;
                }
            }
            if (entry.Title == null) return;
            string color = ColorUtility.ToHtmlStringRGB(RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.DarkOrange));
            string text = "<color=#" + color + ">" + WindowGestureRules.PinLabel(InspectionLocale.Code)
                + "</color> " + (window.titleString ?? "");
            if (entry.Title.text != text) entry.Title.text = text;
            entry.Title.overflowMode = TextOverflowModes.Ellipsis;
            var size = entry.Title.rectTransform.sizeDelta;
            float available = Math.Max(0, window.widthPixels - 36);
            size.x = Math.Min(available, Math.Max(size.x, entry.Title.GetPreferredValues(text).x));
            entry.Title.rectTransform.sizeDelta = size;
        }

        internal static void Closed(PixelWindow window) => Guard(() =>
        {
            var entry = Find(window);
            if (entry == null) return;
            Pinned.Remove(entry);
            Restore(entry);
        });

        private static void Restore(PinnedWindow entry)
        {
            if (entry.Title != null)
            {
                entry.Title.overflowMode = entry.Overflow;
                if (entry.Window != null)
                {
                    entry.Title.text = entry.Window.titleString ?? "";
                    entry.Window.Validate();
                }
                else if (entry.Title.text == entry.DecoratedTitle) entry.Title.text = entry.RawTitle ?? "";
            }
        }

        private static void DecorateTablet(PinnedWindow entry)
        {
            if (entry.Title == null) return;
            if (entry.Title.text != entry.DecoratedTitle) entry.RawTitle = entry.Title.text;
            string color = ColorUtility.ToHtmlStringRGB(RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.DarkOrange));
            entry.DecoratedTitle = "<color=#" + color + ">" + WindowGestureRules.PinLabel(InspectionLocale.Code)
                + "</color> " + (entry.RawTitle ?? "");
            if (entry.Title.text != entry.DecoratedTitle) entry.Title.text = entry.DecoratedTitle;
            entry.Title.overflowMode = TextOverflowModes.Ellipsis;
        }

        internal static void TabletClicked(GameObject panel, PointerEventData pointer) => Guard(() =>
        {
            if (!ConsumePinClick(pointer) || Plugin.CfgWindowPin?.Value != true || panel == null || !panel.activeInHierarchy) return;
            var frame = panel.GetComponent<NegotiationTabletFrame>();
            if (frame?.clientName == null) return;
            var entry = Pinned.Find(p => p.Panel == panel);
            entry ??= new PinnedWindow { Panel = panel, Title = frame.clientName, Overflow = frame.clientName.overflowMode };
            if (WindowGestureRules.TogglePin(Pinned, entry)) DecorateTablet(entry);
            else Restore(entry);
            Raise(entry.Rect);
        });

        internal static void TabletFocused(UIDragHandler drag) => Guard(() =>
        {
            if (drag != null) Raise(drag.transform as RectTransform);
        });

        internal static void TabletRefreshed(NegociationUIManager manager) => Guard(() =>
        {
            if (manager == null || Plugin.CfgWindowPin?.Value != true) return;
            var entry = Pinned.Find(p => p.Panel != null && p.Panel == manager.uiObject);
            if (entry != null) DecorateTablet(entry);
        });

        internal static void Tick() => Guard(() =>
        {
            if (Plugin.CfgWindowPin?.Value == true)
            {
                var panel = NegociationUIManager.Instance?.uiObject;
                if (panel != null && panel.GetComponent<NegotiationPinInput>() == null)
                    panel.AddComponent<NegotiationPinInput>();
            }
            if (_pinPressAllowed && (!Available || PinBusy || !Shift || CheckpointPanel.SuppressHotkeys))
                _pinPressAllowed = false;
            for (int i = Pinned.Count - 1; i >= 0; i--)
            {
                var entry = Pinned[i];
                if (Plugin.CfgWindowPin?.Value == true && entry.Rect != null && entry.Visible)
                {
                    if (entry.Panel != null) DecorateTablet(entry);
                    Raise(entry.Rect);
                    continue;
                }
                Pinned.RemoveAt(i);
                Restore(entry);
            }
        });

        internal static void Drag(TreeNodeRender node, Vector3 before, Vector2 size, PointerEventData pointer)
        {
            Guard(() =>
            {
                if (!Available || Plugin.CfgWindowSnap?.Value != true || !Shift
                    || pointer?.button != PointerEventData.InputButton.Left) return;
                var window = node?.FindParentWindow();
                var rect = window?.rectTransform;
                if (rect == null || !window.canMove || !window.IsVisible() || rect.position == before
                    || rect.rect.size != size || !(rect.parent is RectTransform parent)
                    || WindowsHandler.current == null) return;
                var others = new List<PanelBounds>();
                foreach (var other in WindowsHandler.current.visibleWindows)
                    if (other != window && other.canMove && other.IsVisible() && other.rectTransform != null
                        && other.rectTransform.parent == parent)
                        others.Add(NativeUi.Bounds(other.rectTransform, parent));
                var current = NativeUi.Bounds(rect, parent);
                var next = WindowSnap.Align(current, others, parent.rect.width, parent.rect.height);
                rect.position += parent.TransformVector(new Vector3(next.X - current.X, current.Y - next.Y, 0));
            });
        }

        internal static void Disable()
        {
            _pinPressAllowed = false;
            while (Pinned.Count > 0)
            {
                var entry = Pinned[Pinned.Count - 1];
                Pinned.RemoveAt(Pinned.Count - 1);
                Restore(entry);
            }
        }

        private static void Guard(Action action) { try { action(); } catch (Exception e) { Report(e); } }
        private static void Report(Exception e)
        {
            string error = e.GetType().Name + ": " + e.Message;
            if (_error != error) EventLog.Write("[WindowGestures] " + error);
            _error = error;
        }
    }

    internal sealed class NegotiationPinInput : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public void OnPointerDown(PointerEventData eventData) => NativeWindowGestures.PointerPressed(eventData);
        public void OnPointerClick(PointerEventData eventData) => NativeWindowGestures.TabletClicked(gameObject, eventData);
    }

    [HarmonyPatch(typeof(NegociationUIManager), "RefreshClientHeader")]
    internal static class NativeTabletHeaderPatch
    {
        [HarmonyPostfix] private static void Postfix(NegociationUIManager __instance) => NativeWindowGestures.TabletRefreshed(__instance);
    }

    [HarmonyPatch(typeof(UIDragHandler), nameof(UIDragHandler.MoveFront))]
    internal static class NativePanelFrontPatch
    {
        [HarmonyPostfix] private static void Postfix(UIDragHandler __instance) => NativeWindowGestures.TabletFocused(__instance);
    }

    [HarmonyPatch(typeof(WindowsHandler), nameof(WindowsHandler.TryAddWindow))]
    internal static class NativeWindowOpenedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PixelWindow window, bool __result) => NativeWindowGestures.Opened(window, __result);
    }
    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.ToFront))]
    internal static class NativeWindowFrontPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PixelWindow __instance) => NativeWindowGestures.Front(__instance);
    }
    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Validate))]
    internal static class NativeWindowTitlePatch
    {
        [HarmonyPostfix]
        private static void Postfix(PixelWindow __instance) => NativeWindowGestures.Validated(__instance);
    }
    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Hide))]
    internal static class NativeWindowClosedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PixelWindow __instance)
        {
            NativeWindowGestures.Closed(__instance);
            PinnedCompositionTooltip.Closed(__instance);
        }
    }
    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnPointerClick))]
    internal static class NativeWindowPinClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TreeNodeRender __instance, PointerEventData eventData)
        {
            if (!NativeWindowGestures.ConsumePinClick(eventData)) return;
            NativeWindowGestures.Clicked(__instance, eventData);
            PinnedCompositionTooltip.Clicked(__instance, eventData);
        }
    }
    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnPointerDown))]
    internal static class NativeWindowPinPressPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PointerEventData eventData) => NativeWindowGestures.PointerPressed(eventData);
    }
    [HarmonyPatch(typeof(TreeNodeRender), nameof(TreeNodeRender.OnDrag))]
    internal static class NativeWindowDragPatch
    {
        [HarmonyPrefix]
        private static void Prefix(TreeNodeRender __instance, out (Vector3 position, Vector2 size) __state)
        {
            var rect = __instance.FindParentWindow()?.rectTransform;
            __state = rect == null ? default : (rect.position, rect.rect.size);
        }
        [HarmonyPostfix]
        private static void Postfix(TreeNodeRender __instance, PointerEventData eventData,
            (Vector3 position, Vector2 size) __state) =>
            NativeWindowGestures.Drag(__instance, __state.position, __state.size, eventData);
    }
}
#endif
