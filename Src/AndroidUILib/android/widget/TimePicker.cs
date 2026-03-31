using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A widget for selecting the time of day (hours and minutes).
    /// Maps to UWP TimePicker control.
    /// </summary>
    public class TimePicker : FrameLayout
    {
        private Windows.UI.Xaml.Controls.TimePicker timePicker = new Windows.UI.Xaml.Controls.TimePicker();

        public TimePicker(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            timePicker.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = timePicker;
        }

        public int getHour()
        {
            return timePicker.Time.Hours;
        }

        public int getMinute()
        {
            return timePicker.Time.Minutes;
        }

        public void setHour(int hour)
        {
            timePicker.Time = new TimeSpan(hour, timePicker.Time.Minutes, 0);
        }

        public void setMinute(int minute)
        {
            timePicker.Time = new TimeSpan(timePicker.Time.Hours, minute, 0);
        }

        public void setIs24HourView(bool is24)
        {
            timePicker.ClockIdentifier = is24 ? "24HourClock" : "12HourClock";
        }
    }
}
