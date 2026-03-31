using AndroidInteropLib.android.content;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A toast is a view containing a quick little message for the user.
    /// This is a stub implementation — show() is a no-op.
    /// </summary>
    public class Toast
    {
        public const int LENGTH_SHORT = 0;
        public const int LENGTH_LONG = 1;

        private string text;
        private int duration;

        private Toast() { }

        public static Toast makeText(Context context, string text, int duration)
        {
            Toast toast = new Toast();
            toast.text = text;
            toast.duration = duration;
            return toast;
        }

        public void show() { }

        public void cancel() { }

        public void setText(string text) { this.text = text; }

        public void setDuration(int duration) { this.duration = duration; }
    }
}
