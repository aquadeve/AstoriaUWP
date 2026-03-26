using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Allows the user to select a value by sliding a thumb on a track.
    /// Maps to UWP Slider control.
    /// </summary>
    public class SeekBar : AbsSeekBar
    {
        private Slider slider = new Slider();

        public interface OnSeekBarChangeListener
        {
            void onProgressChanged(SeekBar seekBar, int progress, bool fromUser);
            void onStartTrackingTouch(SeekBar seekBar);
            void onStopTrackingTouch(SeekBar seekBar);
        }

        private OnSeekBarChangeListener listener;

        public SeekBar(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.ValueChanged += (s, e) =>
            {
                listener?.onProgressChanged(this, (int)slider.Value, true);
            };
            slider.PointerPressed += (s, e) =>
            {
                listener?.onStartTrackingTouch(this);
            };
            slider.PointerReleased += (s, e) =>
            {
                listener?.onStopTrackingTouch(this);
            };
            WinUI.Content = slider;
        }

        public new void setProgress(int progress) { slider.Value = progress; }

        public new int getProgress() { return (int)slider.Value; }

        public new void setMax(int max) { slider.Maximum = max; }

        public void setMin(int min) { slider.Minimum = min; }

        public void setOnSeekBarChangeListener(OnSeekBarChangeListener l)
        {
            listener = l;
        }
    }
}
