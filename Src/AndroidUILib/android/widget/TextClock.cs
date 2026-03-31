using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A clock display that can show the current time in 12-hour or 24-hour format.
    /// </summary>
    public class TextClock : TextView
    {
        private string format12Hour;
        private string format24Hour;

        public TextClock(Context c, AttributeSet a) : base(c, a) { }

        public void setFormat12Hour(string format) { format12Hour = format; }

        public void setFormat24Hour(string format) { format24Hour = format; }

        public string getFormat12Hour() { return format12Hour; }

        public string getFormat24Hour() { return format24Hour; }
    }
}
