using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AstroDesk.App.Controls;

/// <summary>
/// Adds predictable one-finger drag scrolling and inertia to WPF scroll viewers.
/// The drag threshold preserves normal taps on controls inside the viewer.
/// </summary>
public static class TouchScrollBehavior
{
    private const double MinimumInertiaVelocity = 70;
    private const double StopInertiaVelocity = 14;
    private const double MaximumInertiaVelocity = 4200;
    private const double InertiaFriction = 5.2;

    private static readonly ConditionalWeakTable<ScrollViewer, TouchScrollState> States = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TouchScrollBehavior),
        new PropertyMetadata(false, HandleIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void HandleIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            scrollViewer.Loaded += HandleLoaded;
            scrollViewer.Unloaded += HandleUnloaded;
            scrollViewer.PreviewTouchDown += HandlePreviewTouchDown;
            scrollViewer.PreviewTouchMove += HandlePreviewTouchMove;
            scrollViewer.PreviewTouchUp += HandlePreviewTouchUp;
            scrollViewer.LostTouchCapture += HandleLostTouchCapture;
            return;
        }

        scrollViewer.Loaded -= HandleLoaded;
        scrollViewer.Unloaded -= HandleUnloaded;
        scrollViewer.PreviewTouchDown -= HandlePreviewTouchDown;
        scrollViewer.PreviewTouchMove -= HandlePreviewTouchMove;
        scrollViewer.PreviewTouchUp -= HandlePreviewTouchUp;
        scrollViewer.LostTouchCapture -= HandleLostTouchCapture;

        if (States.TryGetValue(scrollViewer, out TouchScrollState? state))
        {
            state.StopInertia();
            state.ResetGesture();
            States.Remove(scrollViewer);
        }
    }

    private static void HandleLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        // Prevent the native recognizer from applying the same gesture twice.
        // This behavior selects an axis from the viewer's real scrollable extent.
        scrollViewer.PanningMode = PanningMode.None;
        scrollViewer.IsManipulationEnabled = false;
        _ = States.GetValue(scrollViewer, static viewer => new TouchScrollState(viewer));
    }

    private static void HandleUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ScrollViewer scrollViewer &&
            States.TryGetValue(scrollViewer, out TouchScrollState? state))
        {
            state.StopInertia();
            state.ResetGesture();
        }
    }

    private static void HandlePreviewTouchDown(object? sender, TouchEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        TouchScrollState state = States.GetValue(
            scrollViewer,
            static viewer => new TouchScrollState(viewer));

        if (state.ActiveDevice is not null)
        {
            return;
        }

        state.StopInertia();
        state.ActiveDevice = args.TouchDevice;
        state.StartPoint = args.GetTouchPoint(scrollViewer).Position;
        state.StartHorizontalOffset = scrollViewer.HorizontalOffset;
        state.StartVerticalOffset = scrollViewer.VerticalOffset;
        state.LastTimestamp = Stopwatch.GetTimestamp();
        state.Velocity = 0;
        state.Axis = ScrollAxis.None;
        state.IsDragging = false;
    }

    private static void HandlePreviewTouchMove(object? sender, TouchEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer ||
            !States.TryGetValue(scrollViewer, out TouchScrollState? state) ||
            state.ActiveDevice != args.TouchDevice)
        {
            return;
        }

        Point position = args.GetTouchPoint(scrollViewer).Position;
        Vector totalDelta = position - state.StartPoint;

        if (!state.IsDragging)
        {
            bool canScrollVertically = scrollViewer.ScrollableHeight > 0.5;
            bool canScrollHorizontally = scrollViewer.ScrollableWidth > 0.5;
            bool passedVerticalThreshold = Math.Abs(totalDelta.Y) >= SystemParameters.MinimumVerticalDragDistance;
            bool passedHorizontalThreshold = Math.Abs(totalDelta.X) >= SystemParameters.MinimumHorizontalDragDistance;

            if ((!canScrollVertically || !passedVerticalThreshold) &&
                (!canScrollHorizontally || !passedHorizontalThreshold))
            {
                return;
            }

            state.Axis = SelectAxis(
                canScrollVertically,
                canScrollHorizontally,
                totalDelta);

            if (state.Axis == ScrollAxis.None)
            {
                return;
            }

            state.IsDragging = true;
            _ = scrollViewer.CaptureTouch(args.TouchDevice);
        }

        double previousOffset = state.Axis == ScrollAxis.Vertical
            ? scrollViewer.VerticalOffset
            : scrollViewer.HorizontalOffset;
        double targetOffset = state.Axis == ScrollAxis.Vertical
            ? state.StartVerticalOffset - totalDelta.Y
            : state.StartHorizontalOffset - totalDelta.X;
        double maximumOffset = state.Axis == ScrollAxis.Vertical
            ? scrollViewer.ScrollableHeight
            : scrollViewer.ScrollableWidth;

        targetOffset = Math.Clamp(targetOffset, 0, maximumOffset);
        ScrollToOffset(scrollViewer, state.Axis, targetOffset);

        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(state.LastTimestamp, now).TotalSeconds;
        if (elapsedSeconds > 0.001)
        {
            double instantaneousVelocity = (targetOffset - previousOffset) / elapsedSeconds;
            instantaneousVelocity = Math.Clamp(
                instantaneousVelocity,
                -MaximumInertiaVelocity,
                MaximumInertiaVelocity);
            state.Velocity = (state.Velocity * 0.58) + (instantaneousVelocity * 0.42);
        }

        state.LastTimestamp = now;
        args.Handled = true;
    }

    private static void HandlePreviewTouchUp(object? sender, TouchEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer ||
            !States.TryGetValue(scrollViewer, out TouchScrollState? state) ||
            state.ActiveDevice != args.TouchDevice)
        {
            return;
        }

        bool wasDragging = state.IsDragging;
        ScrollAxis axis = state.Axis;
        double velocity = state.Velocity;
        state.ActiveDevice = null;
        state.IsDragging = false;
        state.Axis = ScrollAxis.None;

        if (!wasDragging)
        {
            return;
        }

        scrollViewer.ReleaseTouchCapture(args.TouchDevice);
        args.Handled = true;

        if (Math.Abs(velocity) >= MinimumInertiaVelocity)
        {
            state.StartInertia(axis, velocity);
        }
    }

    private static void HandleLostTouchCapture(object? sender, TouchEventArgs args)
    {
        if (sender is ScrollViewer scrollViewer &&
            States.TryGetValue(scrollViewer, out TouchScrollState? state) &&
            state.ActiveDevice == args.TouchDevice &&
            !scrollViewer.AreAnyTouchesCaptured)
        {
            state.ResetGesture();
        }
    }

    private static ScrollAxis SelectAxis(
        bool canScrollVertically,
        bool canScrollHorizontally,
        Vector delta)
    {
        if (canScrollVertically && canScrollHorizontally)
        {
            return Math.Abs(delta.Y) >= Math.Abs(delta.X)
                ? ScrollAxis.Vertical
                : ScrollAxis.Horizontal;
        }

        if (canScrollVertically)
        {
            return ScrollAxis.Vertical;
        }

        return canScrollHorizontally
            ? ScrollAxis.Horizontal
            : ScrollAxis.None;
    }

    private static void ScrollToOffset(ScrollViewer scrollViewer, ScrollAxis axis, double offset)
    {
        if (axis == ScrollAxis.Vertical)
        {
            scrollViewer.ScrollToVerticalOffset(offset);
        }
        else if (axis == ScrollAxis.Horizontal)
        {
            scrollViewer.ScrollToHorizontalOffset(offset);
        }
    }

    private enum ScrollAxis
    {
        None,
        Vertical,
        Horizontal,
    }

    private sealed class TouchScrollState
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly DispatcherTimer _inertiaTimer;
        private long _inertiaTimestamp;

        public TouchScrollState(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _inertiaTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _inertiaTimer.Tick += HandleInertiaTick;
        }

        public TouchDevice? ActiveDevice { get; set; }

        public Point StartPoint { get; set; }

        public double StartHorizontalOffset { get; set; }

        public double StartVerticalOffset { get; set; }

        public long LastTimestamp { get; set; }

        public double Velocity { get; set; }

        public ScrollAxis Axis { get; set; }

        public bool IsDragging { get; set; }

        public void StartInertia(ScrollAxis axis, double velocity)
        {
            Axis = axis;
            Velocity = Math.Clamp(velocity, -MaximumInertiaVelocity, MaximumInertiaVelocity);
            _inertiaTimestamp = Stopwatch.GetTimestamp();
            _inertiaTimer.Start();
        }

        public void StopInertia()
        {
            _inertiaTimer.Stop();
            Velocity = 0;
        }

        public void ResetGesture()
        {
            ActiveDevice = null;
            IsDragging = false;
            Axis = ScrollAxis.None;
        }

        private void HandleInertiaTick(object? sender, EventArgs args)
        {
            if (!_scrollViewer.IsLoaded ||
                Axis == ScrollAxis.None ||
                Math.Abs(Velocity) < StopInertiaVelocity)
            {
                StopInertia();
                Axis = ScrollAxis.None;
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double elapsedSeconds = Math.Min(
                Stopwatch.GetElapsedTime(_inertiaTimestamp, now).TotalSeconds,
                0.05);
            _inertiaTimestamp = now;

            double currentOffset = Axis == ScrollAxis.Vertical
                ? _scrollViewer.VerticalOffset
                : _scrollViewer.HorizontalOffset;
            double maximumOffset = Axis == ScrollAxis.Vertical
                ? _scrollViewer.ScrollableHeight
                : _scrollViewer.ScrollableWidth;
            double targetOffset = Math.Clamp(
                currentOffset + (Velocity * elapsedSeconds),
                0,
                maximumOffset);

            ScrollToOffset(_scrollViewer, Axis, targetOffset);

            if (Math.Abs(targetOffset - currentOffset) < 0.01)
            {
                StopInertia();
                Axis = ScrollAxis.None;
                return;
            }

            Velocity *= Math.Exp(-InertiaFriction * elapsedSeconds);
        }
    }
}
