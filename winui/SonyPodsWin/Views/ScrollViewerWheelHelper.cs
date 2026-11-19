using System;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SonyPodsWin.Views;

/// <summary>
/// Fixes mouse-wheel scrolling across all Page ScrollViewers in WinUI 3:
/// 1. Ensures non-null Transparent backgrounds on the ScrollViewer and its root content panel
///    so pointer hit-testing never falls through empty padding or card spacing.
/// 2. Intercepts PointerWheelChanged (including events marked Handled by child controls such as
///    Slider or ComboBox) and drives ScrollViewer.ChangeView deterministically.
/// 3. Suppresses unintended BringIntoView auto-scrolling during background UI state syncs.
/// </summary>
internal static class ScrollViewerWheelHelper
{
    private const double StepPerWheelNotch = 84.0;

    public static void Attach(ScrollViewer scrollViewer, Func<bool>? isSyncingUi = null)
    {
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.Background ??= new SolidColorBrush(Colors.Transparent);
        if (scrollViewer.Content is Panel panel && panel.Background is null)
        {
            panel.Background = new SolidColorBrush(Colors.Transparent);
        }

        if (isSyncingUi is not null)
        {
            scrollViewer.BringIntoViewRequested += (_, e) =>
            {
                if (isSyncingUi())
                {
                    e.Handled = true;
                }
            };
        }

        ulong lastHandledTimestamp = 0;

        void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            PointerPoint pt = e.GetCurrentPoint(scrollViewer);
            if (pt.Timestamp == lastHandledTimestamp)
            {
                e.Handled = true;
                return;
            }

            PointerPointProperties props = pt.Properties;
            if (props.IsHorizontalMouseWheel)
            {
                return;
            }

            int delta = props.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            lastHandledTimestamp = pt.Timestamp;
            double targetOffset = Math.Clamp(
                scrollViewer.VerticalOffset - (delta / 120.0) * StepPerWheelNotch,
                0.0,
                scrollViewer.ScrollableHeight);
            scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            e.Handled = true;
        }

        var handler = new PointerEventHandler(OnPointerWheelChanged);
        if (scrollViewer.Content is UIElement contentElement)
        {
            contentElement.AddHandler(UIElement.PointerWheelChangedEvent, handler, handledEventsToo: true);
        }

        scrollViewer.AddHandler(UIElement.PointerWheelChangedEvent, handler, handledEventsToo: true);
    }
}
