using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;

namespace NexusExplorer.Views;

// One replacement clock per target/property. Retargeting samples both position and velocity.
internal static class PrismMotion
{
    private sealed record Running(MotionSegment Segment, AnimationClock Clock);
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Running>> Channels = new();

    internal static void Animate(DependencyObject target, DependencyProperty property, double to,
        MotionProfile profile, Action? completed = null,
        double minimum = double.NegativeInfinity, double maximum = double.PositiveInfinity)
    {
        target.Dispatcher.VerifyAccess();
        if (!double.IsFinite(to)) throw new ArgumentOutOfRangeException(nameof(to));
        var channels = Channels.GetOrCreateValue(target);
        var from = (double)target.GetValue(property);
        if (!double.IsFinite(from)) throw new InvalidOperationException("Set an initial finite value before animating " + property.Name);
        var velocity = channels.TryGetValue(property, out var previous)
            ? previous.Segment.Sample(previous.Clock.CurrentTime?.TotalSeconds ?? 0).Velocity : 0;
        if (previous is not null && previous.Segment.To == to && completed is null) return;
        var segment = new MotionSegment(from, Math.Clamp(to, minimum, maximum), velocity, profile, minimum, maximum);
        var animation = new MotionAnimation { Segment = segment, Duration = TimeSpan.FromSeconds(profile.Seconds) };
        animation.Freeze();
        var clock = animation.CreateClock();
        var running = new Running(segment, clock);
        channels[property] = running;
        clock.Completed += (_, _) =>
        {
            if (!channels.TryGetValue(property, out var active) || !ReferenceEquals(active, running)) return;
            channels.Remove(property);
            Apply(target, property, null);
            target.SetCurrentValue(property, segment.To); // Preserve bindings and release the clock.
            completed?.Invoke();
        };
        Apply(target, property, clock);
    }

    internal static void Stop(DependencyObject target, DependencyProperty property, bool finish = false)
    {
        if (!Channels.TryGetValue(target, out var channels) || !channels.Remove(property, out var running)) return;
        var value = finish ? running.Segment.To : (double)target.GetValue(property);
        Apply(target, property, null); target.SetCurrentValue(property, value);
    }
    internal static void StopAll(DependencyObject target, bool finish = false)
    {
        if (!Channels.TryGetValue(target, out var channels)) return;
        foreach (var property in channels.Keys.ToArray()) Stop(target, property, finish);
    }
    private static void Apply(DependencyObject target, DependencyProperty property, AnimationClock? clock)
    {
        switch (target)
        {
            case UIElement element: element.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace); break;
            case Animatable animatable: animatable.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace); break;
            default: throw new ArgumentException("Motion target must be UIElement or Animatable", nameof(target));
        }
    }
}
