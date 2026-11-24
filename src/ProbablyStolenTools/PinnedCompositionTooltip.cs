#if MONO
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    internal static class PinnedCompositionTooltip
    {
        private sealed class Entry
        {
            internal GameItemElement Item;
            internal PixelWindow Owner;
            internal readonly List<PixelWindow> Containers = new();
            internal RichTextElement View;
            internal string LastText;
            internal float RefreshAt;
        }
        // Only mod-owned UI references. Never attach a PixelElement to the game's window graph.
        private static readonly List<Entry> Entries = new();
        private static string _error;
        private static bool Ready => InspectionLocale.AnnotationsReady && Plugin.Store() != null
            && MainMenuUIController.Instance == null && !Rewind.Restoring && !Rewind.Settling
            && !CustomerCheckpoint.Restoring && !SaveManager.IsDecoding && !NativeLoadDiagnostics.Active
            && !CurrentConverterHelper.isNightCycle;

        internal static void Clicked(TreeNodeRender node, PointerEventData pointer)
        {
            try
            {
                if (!Ready || pointer == null || CheckpointPanel.SuppressHotkeys) return;
                var item = node?.FindParentElement<GameItemElement>();
                if (item == null) return;
                var existing = Entries.Find(e => ReferenceEquals(e.Item, item));
                if (existing != null) { Remove(existing); return; }
                var owner = item.handler?.FindParentWindow();
                if (owner?.handler == null || !owner.IsVisible()) return;
                var entry = new Entry { Item = item, Owner = owner };
                var content = Content(entry);
                if (content == null) return;
                Entries.Add(entry);
                try
                {
                    entry.View = new RichTextElement(hasRaycast: false);
                    // Transform-only parenting avoids writing native window/element relationships.
                    entry.View.rectTransform.SetParent(owner.rectTransform, false);
                    foreach (var graphic in entry.View.handler.GetComponentsInChildren<Graphic>(true))
                        graphic.raycastTarget = false;
                    Dependencies(entry);
                    Refresh(entry, content);
                    Place(entry);
                }
                catch { Remove(entry); throw; }
            }
            catch (Exception e) { Warn(e); }
        }

        private static RichText Content(Entry entry)
            => DetailedCompositionTooltip.BuildDetails(entry.Item, entry.View?.GetText()?.text);

        private static void Dependencies(Entry entry)
        {
            entry.Containers.Clear();
            entry.Containers.Add(entry.Owner);
            var seen = new HashSet<GameItem>();
            var parent = entry.Item.parentInventory?.GetParentItem();
            while (parent != null && seen.Add(parent))
            {
                var window = parent.contentWindow;
                if (window != null && window.IsVisible() && !entry.Containers.Contains(window))
                    entry.Containers.Add(window);
                parent = parent.parentInventory?.GetParentItem();
            }
        }

        private static void Refresh(Entry entry, RichText content)
        {
            entry.RefreshAt = Time.unscaledTime + 0.25f;
            if (content.richTextForeground == entry.LastText) return;
            entry.View.SetRichText(content);
            entry.LastText = content.richTextForeground;
        }

        private static void Place(Entry entry)
        {
            // Fixed inventory roots are not viewport-sized; floating container roots can be.
            // Clamp in the owning canvas, without moving the view out of its window layer.
            var canvas = entry.Owner.handler.GetComponentInParent<Canvas>(true);
            if (canvas == null) canvas = RenderHandler.current.windowUICanvas;
            var area = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (area == null || area.rect.width <= 0 || area.rect.height <= 0) return;
            var item = NativeUi.Bounds(entry.Item.rectTransform, area);
            var current = NativeUi.Bounds(entry.View.rectTransform, area);
            var bounds = PinnedTooltipLayout.Place(item, current.Width, current.Height,
                area.rect.width, area.rect.height);
            entry.View.rectTransform.position += area.TransformVector(new Vector3(
                bounds.X - current.X, current.Y - bounds.Y, 0));
        }

        internal static void Tick()
        {
            try
            {
                if (Entries.Count == 0) return;
                if (!Ready) { Disable(); return; }
                for (int i = Entries.Count - 1; i >= 0; i--)
                {
                    var entry = Entries[i];
                    try
                    {
                        if (entry.Item.handler == null || entry.Owner.handler == null || entry.View?.handler == null
                            || entry.Containers.Exists(w => w.handler == null || !w.IsVisible()))
                        { Remove(entry); continue; }
                        bool visible = entry.Item.handler.gameObject.activeInHierarchy;
                        entry.View.handler.gameObject.SetActive(visible);
                        if (!visible) continue;
                        var owner = entry.Item.handler.FindParentWindow();
                        if (owner != null && !ReferenceEquals(owner, entry.Owner))
                        {
                            entry.Owner = owner;
                            entry.View.rectTransform.SetParent(owner.rectTransform, false);
                            Dependencies(entry);
                        }
                        if (Time.unscaledTime >= entry.RefreshAt)
                        {
                            var content = Content(entry);
                            if (content == null) { Remove(entry); continue; }
                            Refresh(entry, content);
                        }
                        Place(entry);
                    }
                    catch (Exception e) { Remove(entry); Warn(e); }
                }
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void Closed(PixelWindow window)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
                if (Entries[i].Containers.Contains(window)) Remove(Entries[i]);
        }

        private static void Remove(Entry entry)
        {
            Entries.Remove(entry);
            try
            {
                if (entry.View?.handler != null) UnityEngine.Object.Destroy(entry.View.handler.gameObject);
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void Disable()
        {
            while (Entries.Count > 0) Remove(Entries[Entries.Count - 1]);
        }

        private static void Warn(Exception e)
        {
            string error = e.GetType().Name + ": " + e.Message;
            if (_error != error) EventLog.Write("[PinnedComposition] " + error);
            _error = error;
        }
    }
}
#endif
