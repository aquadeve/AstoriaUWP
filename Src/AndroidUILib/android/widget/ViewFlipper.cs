using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Simple ViewAnimator that automatically flips between children at a regular interval.
    /// Maps to UWP FlipView control.
    /// </summary>
    public class ViewFlipper : ViewGroup
    {
        private FlipView flipView = new FlipView();
        private bool autoStart;
        private bool flipping;
        private int flipInterval;

        public ViewFlipper(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            flipView.HorizontalAlignment = HorizontalAlignment.Stretch;
            flipView.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = flipView;
        }

        public void setAutoStart(bool autoStart) { this.autoStart = autoStart; }

        public void startFlipping() { flipping = true; }

        public void stopFlipping() { flipping = false; }

        public bool isFlipping() { return flipping; }

        public void setFlipInterval(int milliseconds) { flipInterval = milliseconds; }

        public int getDisplayedChild() { return flipView.SelectedIndex; }

        public void setDisplayedChild(int whichChild)
        {
            if (whichChild >= 0 && whichChild < flipView.Items.Count)
                flipView.SelectedIndex = whichChild;
        }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param)
        {
            flipView.Items.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            flipView.Items.Remove(view.WinUI);
        }
    }
}
