namespace NexusExplorer.Views;

internal enum MotionCurve { Spring, Smooth }
internal sealed record MotionProfile(double Seconds, MotionCurve Curve, double Damping = .72, double Frequency = 18);

// Values describe interaction roles, not particular controls. No per-call easing constants.
internal static class MotionProfiles
{
    internal static readonly MotionProfile Panel = new(.7, MotionCurve.Spring);
    internal static readonly MotionProfile Scope = new(.5, MotionCurve.Spring, .72, 24);
    internal static readonly MotionProfile NoticeEnter = new(.82, MotionCurve.Spring, .58);
    internal static readonly MotionProfile NoticeExit = new(.3, MotionCurve.Smooth);
    internal static readonly MotionProfile Fade = new(.7, MotionCurve.Smooth);
    internal static readonly MotionProfile Press = new(.14, MotionCurve.Smooth);
    internal static readonly TimeSpan NoticeHold = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan HoverPoll = TimeSpan.FromMilliseconds(40);
    internal static readonly TimeSpan InspectorEnter = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan InspectorLeave = TimeSpan.FromMilliseconds(180);
    internal const int VolumeLeaveMilliseconds = 250;
    internal const double PressDepth = 1.5;
}
