using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A calendar widget for displaying and selecting dates.
    /// Maps to UWP CalendarView control.
    /// </summary>
    public class CalendarView : FrameLayout
    {
        private Windows.UI.Xaml.Controls.CalendarView calView = new Windows.UI.Xaml.Controls.CalendarView();

        public CalendarView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            calView.HorizontalAlignment = HorizontalAlignment.Stretch;
            calView.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = calView;
        }

        public long getDate()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void setDate(long date) { }
    }
}
