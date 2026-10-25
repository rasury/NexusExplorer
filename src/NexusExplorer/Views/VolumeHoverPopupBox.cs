using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Views;

// Keep the Material popup, allowing the pointer to cross its rounded edges and gap.
// This corridor observes the pointer without capturing or blocking other controls.
public sealed class VolumeHoverPopupBox : PopupBox
{
    internal const int CloseDelayMilliseconds = MotionProfiles.VolumeLeaveMilliseconds;
    private readonly DispatcherTimer _leaveTimer;
    private long? _outsideSince;

    public VolumeHoverPopupBox()
    {
        _leaveTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        { Interval = MotionProfiles.HoverPoll };
        _leaveTimer.Tick += (_, _) => ProcessHover(Mouse.GetPosition(this),
            Mouse.Captured is Thumb thumb && PopupContent is FrameworkElement content && content.IsAncestorOf(thumb),
            Environment.TickCount64);
        Unloaded += (_, _) => SetCurrentValue(IsPopupOpenProperty, false);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        if (PopupContent is PrismCard glass)
            glass.PrepareDialogBackdrop(Window.GetWindow(this)?.FindName("WallpaperScene") as FrameworkElement);
        _leaveTimer.Stop();
        _outsideSince = null;
        base.OnMouseEnter(e);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild(PopupPartName) is Popup { Child: Card card })
        {
            card.Background = Brushes.Transparent;
            card.Padding = new Thickness(0);
            card.ClipContent = false;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (PopupMode is not (PopupBoxPopupMode.MouseOver or PopupBoxPopupMode.MouseOverEager))
        { base.OnMouseLeave(e); return; }
        // The base implementation closes before the pointer can cross to the
        // separate popup HWND. Use a non-modal hover grace period instead.
        if (IsPopupOpen) _leaveTimer.Start();
    }

    protected override void OnClosed()
    {
        _leaveTimer.Stop();
        _outsideSince = null;
        base.OnClosed();
    }

    internal Rect GetHoverBounds()
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (GetTemplateChild(PopupPartName) is Popup { Child: FrameworkElement content }
            && content.IsVisible && PresentationSource.FromVisual(content) is not null)
        {
            var topLeft = PointFromScreen(content.PointToScreen(new Point()));
            var bottomRight = PointFromScreen(content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight)));
            bounds.Union(new Rect(topLeft, bottomRight));
        }
        // The rectangular union bridges the gap; 12dp either side permits a
        // diagonal approach without enlarging the visible speaker or slider.
        bounds.Inflate(12, 4);
        return bounds;
    }

    internal void ProcessHover(Point pointer, bool sliderCaptured, long timestamp)
    {
        if (!IsPopupOpen) { _leaveTimer.Stop(); return; }
        if (sliderCaptured || GetHoverBounds().Contains(pointer))
        { _outsideSince = null; return; }
        _outsideSince ??= timestamp;
        if (timestamp - _outsideSince >= CloseDelayMilliseconds)
            SetCurrentValue(IsPopupOpenProperty, false);
    }
}
