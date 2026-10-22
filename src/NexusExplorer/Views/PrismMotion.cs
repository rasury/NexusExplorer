using System.Windows;
using System.Windows.Media.Animation;

namespace NexusExplorer.Views;

internal static class PrismMotion
{
    // The same damped spring drives layout and decoration, including interrupted transitions.
    internal static DoubleAnimationUsingKeyFrames Spring(double from, double to, bool bounded = false)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        const double damping = .72, frequency = 18, duration = .7;
        var damped = frequency * Math.Sqrt(1 - damping * damping);
        for (var i = 0; i <= 60; i++)
        {
            var t = duration * i / 60;
            var progress = 1 - Math.Exp(-damping * frequency * t) * (Math.Cos(damped * t) + damping * frequency / damped * Math.Sin(damped * t));
            var value = i == 60 ? to : from + (to - from) * progress;
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(bounded ? Math.Max(0, value) : value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        return animation;
    }
}
