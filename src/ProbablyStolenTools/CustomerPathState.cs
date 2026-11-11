using System;
using DG.Tweening;

namespace ProbablyStolenTools
{
    // Only settled customer paths are captured. Seeking must not replay arrival/leave events.
    internal sealed class CustomerPathState
    {
        private readonly DOTweenPath _path;
        private readonly Tween _tween;
        private readonly bool _hasPath;
        private readonly float _elapsed, _timeScale;
        private readonly bool _backwards;

        internal CustomerPathState(DOTweenPath path)
        {
            _path = path;
            _hasPath = path != null;
            _tween = path == null ? null : path.tween;
            if (_tween == null) return;
            if (!_tween.IsActive() || _tween.IsPlaying())
                throw new InvalidOperationException("Customer path is not settled.");
            _elapsed = _tween.Elapsed();
            _timeScale = _tween.timeScale;
            _backwards = _tween.isBackwards;
        }

        internal void Validate()
        {
            if (_path == null)
            {
                if (_hasPath) throw new InvalidOperationException("Customer path was destroyed.");
                return;
            }
            if (!ReferenceEquals(_path.tween, _tween) || (_tween != null && !_tween.IsActive()))
                throw new InvalidOperationException("Customer path tween was replaced or killed.");
        }

        internal void Restore()
        {
            Validate();
            if (_tween == null) return;
            var play = _tween.onPlay;
            var pause = _tween.onPause;
            var rewind = _tween.onRewind;
            var update = _tween.onUpdate;
            var step = _tween.onStepComplete;
            var complete = _tween.onComplete;
            var kill = _tween.onKill;
            var waypoint = _tween.onWaypointChange;
            try
            {
                // DOTween.Goto still dispatches onUpdate/onRewind/onComplete/onPause.
                _tween.onPlay = _tween.onPause = _tween.onRewind = null;
                _tween.onUpdate = _tween.onStepComplete = _tween.onComplete = _tween.onKill = null;
                _tween.onWaypointChange = null;
                _tween.Goto(_elapsed, false);
                _tween.timeScale = _timeScale;
                _tween.isBackwards = _backwards;
            }
            finally
            {
                _tween.onPlay = play;
                _tween.onPause = pause;
                _tween.onRewind = rewind;
                _tween.onUpdate = update;
                _tween.onStepComplete = step;
                _tween.onComplete = complete;
                _tween.onKill = kill;
                _tween.onWaypointChange = waypoint;
            }
        }

        internal string Describe() => _tween == null ? "none"
            : $"active={_tween.IsActive()},playing={_tween.IsPlaying()},elapsed={_tween.Elapsed():0.###},target={_elapsed:0.###},backwards={_tween.isBackwards}";
    }
}
