using System.Windows;
using System.Windows.Media.Animation;

namespace NexusExplorer.Views;

internal readonly record struct MotionSample(double Value, double Velocity);
internal sealed record MotionSegment(double From, double To, double Velocity, MotionProfile Profile,
    double Minimum = double.NegativeInfinity, double Maximum = double.PositiveInfinity)
{
    internal MotionSample Sample(double seconds)
    {
        if (seconds >= Profile.Seconds) return new(To, 0);
        var t = Math.Max(0, seconds);
        double value, velocity;
        if (Profile.Curve == MotionCurve.Spring)
        {
            var a = Profile.Damping * Profile.Frequency;
            var w = Profile.Frequency * Math.Sqrt(1 - Profile.Damping * Profile.Damping);
            var x = From - To;
            var b = (Velocity + a * x) / w;
            var c = Math.Cos(w * t); var s = Math.Sin(w * t); var decay = Math.Exp(-a * t);
            value = To + decay * (x * c + b * s);
            velocity = decay * ((-a * x + w * b) * c + (-a * b - w * x) * s);
        }
        else if (Profile.Curve == MotionCurve.Emphasized)
        {
            // Spend 40% of time crossing the first 20% of the visible area,
            // rush through the middle 60%, then settle across the last 20%.
            // Quadratic edges and a cubic middle have continuous velocity.
            var u = t / Profile.Seconds;
            double progress, speed;
            if (u < .4)
            {
                progress = 1.25 * u * u;
                speed = 2.5 * u;
            }
            else if (u > .6)
            {
                progress = 1 - 1.25 * (1 - u) * (1 - u);
                speed = 2.5 * (1 - u);
            }
            else
            {
                var middle = (u - .4) / .2;
                progress = .2 + .2 * middle + 1.2 * middle * middle - .8 * middle * middle * middle;
                speed = 1 + 12 * middle * (1 - middle);
            }
            value = From + (To - From) * progress;
            velocity = (To - From) * speed / Profile.Seconds;
        }
        else
        {
            // Cubic Hermite: preserve incoming velocity and arrive with zero velocity.
            var u = t / Profile.Seconds; var u2 = u * u; var u3 = u2 * u;
            value = (2*u3-3*u2+1)*From + (u3-2*u2+u)*Profile.Seconds*Velocity + (-2*u3+3*u2)*To;
            velocity = ((6*u2-6*u)*From + (3*u2-4*u+1)*Profile.Seconds*Velocity + (-6*u2+6*u)*To) / Profile.Seconds;
        }
        var bounded = Math.Clamp(value, Minimum, Maximum);
        return new(bounded, bounded == value ? velocity : 0);
    }
}

// WPF supplies the clock. Analytical evaluation avoids allocating 61/75 keyframes per gesture.
internal sealed class MotionAnimation : DoubleAnimationBase
{
    internal static readonly DependencyProperty SegmentProperty = DependencyProperty.Register(nameof(Segment), typeof(MotionSegment), typeof(MotionAnimation));
    internal MotionSegment Segment { get => (MotionSegment)GetValue(SegmentProperty); set => SetValue(SegmentProperty, value); }
    protected override Freezable CreateInstanceCore() => new MotionAnimation();
    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock clock)
        => Segment.Sample(clock.CurrentTime?.TotalSeconds ?? 0).Value;
}
