using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    // Business fields restore OPEN/CLOSED; native Unity objects need their own view state.
    internal sealed class ShopPhaseVisual
    {
        private readonly StoreShutterButton _shutter = StoreShutterButton.Instance;
        private readonly List<ObjectView> _objects = new();
        private readonly Image _mask;
        private readonly Color _maskColor;
        private readonly bool _maskEnabled;

        internal ShopPhaseVisual()
        {
            if (_shutter == null) throw new InvalidOperationException("Shop shutter missing.");
            Add(_shutter.shutterGameObject);
            Add(_shutter.blockerGameObject);
            _mask = _shutter.blockerMaskObject;
            if (_mask != null)
            {
                Add(_mask.gameObject);
                _maskColor = _mask.color;
                _maskEnabled = _mask.enabled;
            }
            Add(EmporiumEntry.Instance.storeInteractables);
            Add(EmporiumEntry.Instance.storePhysical);
            Add(StoreUIManager.Instance.closeShutterConfirmationPanel);
            Validate();
        }

        private void Add(GameObject go)
        {
            if (go != null) _objects.Add(new ObjectView(go));
        }

        internal static bool IsSettled()
        {
            var shutter = StoreShutterButton.Instance;
            if (shutter == null || shutter.shutterGameObject == null || shutter.blockerGameObject == null) return false;
            bool closed = GameFields.Get<bool>(shutter, "isClosed");
            var position = closed ? shutter.startPosition : shutter.endPosition;
            var blocker = closed ? shutter.blockerStartPosition : shutter.blockerEndPosition;
            return (shutter.shutterGameObject.transform.position - position).sqrMagnitude < 0.00001f
                && (shutter.blockerGameObject.transform.localPosition - blocker).sqrMagnitude < 0.00001f;
        }

        internal void Validate()
        {
            if (_shutter == null || StoreShutterButton.Instance == null
                || Id(StoreShutterButton.Instance) != Id(_shutter))
                throw new InvalidOperationException("Shop view no longer exists.");
            foreach (var view in _objects) view.Validate();
        }

        internal void Restore()
        {
            Validate();
            foreach (var view in _objects) view.Restore();
            if (_mask != null) { _mask.color = _maskColor; _mask.enabled = _maskEnabled; }
        }

        private sealed class ObjectView
        {
            private readonly GameObject _go;
            private readonly Transform _parent;
            private readonly Vector3 _position;
            private readonly bool _active;
            internal ObjectView(GameObject go)
            {
                _go = go; _parent = go.transform.parent;
                _position = go.transform.localPosition; _active = go.activeSelf;
            }
            internal void Validate()
            {
                if (_go == null || _go.transform.parent != _parent)
                    throw new InvalidOperationException("Shop view was destroyed or reparented.");
            }
            internal void Restore()
            {
                _go.transform.localPosition = _position;
                _go.SetActive(_active);
            }
        }
    }
}
