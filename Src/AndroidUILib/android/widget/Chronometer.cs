using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A simple timer display that counts up or down.
    /// </summary>
    public class Chronometer : TextView
    {
        private long base_;
        private bool started;

        public Chronometer(Context c, AttributeSet a) : base(c, a) { }

        public void setBase(long base_) { this.base_ = base_; }

        public long getBase() { return base_; }

        public void start() { started = true; }

        public void stop() { started = false; }

        public void setFormat(string format) { }
    }
}
