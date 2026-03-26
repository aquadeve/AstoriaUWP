using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Abstract base class for SeekBar and RatingBar. Maps to UWP Slider.
    /// </summary>
    public abstract class AbsSeekBar : ProgressBar
    {
        public AbsSeekBar(Context context, AttributeSet attrs) : base(context, attrs) { }

        public void setThumbOffset(int thumbOffset) { }

        public int getThumbOffset() { return 0; }
    }
}
