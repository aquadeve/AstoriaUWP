using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A widget for selecting a date (year, month, day).
    /// Maps to UWP CalendarDatePicker control.
    /// </summary>
    public class DatePicker : FrameLayout
    {
        private CalendarDatePicker datePicker = new CalendarDatePicker();

        public DatePicker(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            datePicker.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = datePicker;
        }

        public int getYear()
        {
            return datePicker.Date.HasValue ? datePicker.Date.Value.Year : DateTime.Now.Year;
        }

        public int getMonth()
        {
            return datePicker.Date.HasValue ? datePicker.Date.Value.Month - 1 : DateTime.Now.Month - 1;
        }

        public int getDayOfMonth()
        {
            return datePicker.Date.HasValue ? datePicker.Date.Value.Day : DateTime.Now.Day;
        }

        public void init(int year, int month, int day, object listener)
        {
            datePicker.Date = new DateTimeOffset(new DateTime(year, month + 1, day));
        }
    }
}
