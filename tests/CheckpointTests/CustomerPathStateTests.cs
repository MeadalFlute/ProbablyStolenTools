using ProbablyStolenTools;
using DG.Tweening;

internal static class CustomerPathStateTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("customer path restores captured cursor and direction without replaying native callbacks", () =>
        {
            var tween = new Tween { elapsed = 4, timeScale = 0.45f, isBackwards = true };
            int events = 0;
            TweenCallback callback = () => events++;
            TweenCallback<int> waypoint = _ => events++;
            tween.onStart = tween.onPlay = tween.onPause = tween.onRewind = callback;
            tween.onUpdate = tween.onStepComplete = tween.onComplete = tween.onKill = callback;
            tween.onWaypointChange = waypoint;
            var state = new CustomerPathState(new DOTweenPath { tween = tween });
            tween.elapsed = 0; tween.timeScale = 1; tween.isBackwards = false; tween.playing = true;
            state.Restore();
            check(tween.elapsed == 4 && tween.timeScale == 0.45f && tween.isBackwards && !tween.playing);
            check(events == 0 && tween.Seeks == 1);
            check(new[] { tween.onStart, tween.onPlay, tween.onPause, tween.onRewind, tween.onUpdate,
                tween.onStepComplete, tween.onComplete, tween.onKill }.All(c => ReferenceEquals(c, callback)));
            check(ReferenceEquals(tween.onWaypointChange, waypoint));
            tween.onRewind();
            check(events == 1);
        });

        test("customer path callback suppression unwinds on seek error without a hidden retry", () =>
        {
            var tween = new Tween { elapsed = 4 };
            int events = 0;
            TweenCallback callback = () => events++;
            tween.onComplete = tween.onKill = callback;
            var state = new CustomerPathState(new DOTweenPath { tween = tween });
            tween.ThrowOnSeek = true;
            try { state.Restore(); throw new Exception("Expected seek failure"); }
            catch (InvalidOperationException e) { check(e.Message == "modeled seek failure"); }
            check(events == 0 && tween.Seeks == 1 && ReferenceEquals(tween.onComplete, callback)
                && ReferenceEquals(tween.onKill, callback));
        });

        test("customer path preflight rejects replaced or killed tweens before any seek", () =>
        {
            var tween = new Tween { elapsed = 4 };
            var path = new DOTweenPath { tween = tween };
            var state = new CustomerPathState(path);
            path.tween = new Tween();
            Reject(state.Validate); Reject(state.Restore);
            check(tween.Seeks == 0 && path.tween.Seeks == 0);
            path.tween = tween;
            tween.active = false;
            Reject(state.Validate);
            check(tween.Seeks == 0);
        });

        test("customer path capture refuses moving or invalid paths and permits absent optional paths", () =>
        {
            Reject(() => new CustomerPathState(new DOTweenPath { tween = new Tween { playing = true } }));
            Reject(() => new CustomerPathState(new DOTweenPath { tween = new Tween { active = false } }));
            new CustomerPathState(null).Restore();
            var empty = new DOTweenPath();
            var state = new CustomerPathState(empty);
            state.Restore();
            empty.tween = new Tween();
            Reject(state.Validate);
        });

        test("customer two-node path roundtrips preserve later native leave callbacks", () =>
        {
            var tween = new Tween { elapsed = 4 };
            var right = new Tween();
            var mainPath = new DOTweenPath { tween = tween };
            var rightPath = new DOTweenPath { tween = right };
            var atCounter = new CustomerPathState(mainPath);
            var beforeRightLeave = new CustomerPathState(rightPath);
            tween.elapsed = 0; right.elapsed = 3;
            var left = new CustomerPathState(mainPath);
            var rightLeft = new CustomerPathState(rightPath);
            int departures = 0;
            tween.onRewind = () => departures++;
            right.onComplete = () => departures++;
            for (int i = 0; i < 6; i++)
            {
                atCounter.Restore(); beforeRightLeave.Restore();
                check(tween.elapsed == 4 && right.elapsed == 0);
                left.Restore(); rightLeft.Restore();
                check(tween.elapsed == 0 && right.elapsed == 3 && departures == 0);
            }
            atCounter.Restore();
            tween.Goto(0, false);
            check(departures == 1);
        });

        test("failed customer path preflight leaves another retained node usable", () =>
        {
            var first = new Tween { elapsed = 4 };
            var second = new Tween { elapsed = 2 };
            var path = new DOTweenPath { tween = first };
            var earlier = new CustomerPathState(path);
            path.tween = second;
            var later = new CustomerPathState(path);
            second.elapsed = 0;
            Reject(earlier.Restore);
            check(first.Seeks == 0 && second.Seeks == 0);
            later.Restore();
            check(second.elapsed == 2 && second.Seeks == 1);
        });
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected invalid customer path");
    }
}

// Tests exercise production CustomerPathState against a callback-dispatching DOTween model.
// This does not load Unity or certify actual rendering.
namespace DG.Tweening
{
    public delegate void TweenCallback();
    public delegate void TweenCallback<T>(T value);
    public sealed class DOTweenPath { public Tween tween; }
    public sealed class Tween
    {
        public float elapsed, timeScale = 1;
        public bool isBackwards, playing, active = true, ThrowOnSeek;
        public int Seeks;
        public TweenCallback onStart, onPlay, onPause, onRewind, onUpdate, onStepComplete, onComplete, onKill;
        public TweenCallback<int> onWaypointChange;
        public bool IsActive() => active;
        public bool IsPlaying() => playing;
        public float Elapsed() => elapsed;
        public void Goto(float value, bool andPlay)
        {
            Seeks++;
            onPause?.Invoke(); onUpdate?.Invoke();
            onStepComplete?.Invoke(); onWaypointChange?.Invoke(0);
            if (value == 0) onRewind?.Invoke();
            if (value > 0) onComplete?.Invoke();
            if (ThrowOnSeek) throw new InvalidOperationException("modeled seek failure");
            elapsed = value; playing = andPlay;
        }
    }
}
