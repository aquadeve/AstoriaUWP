using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Visual indicator of progress in some operation. Maps to UWP ProgressBar
    /// (horizontal, determinate/indeterminate) or ProgressRing (circular indeterminate).
    /// </summary>
    public class ProgressBar : View
    {
        private Windows.UI.Xaml.Controls.ProgressBar progressBar
            = new Windows.UI.Xaml.Controls.ProgressBar();
        private ProgressRing progressRing = new ProgressRing();
        private bool isIndeterminate = true;

        public ProgressBar(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // ProgressBar and ProgressRing inherit Control (not ContentControl), so use WinUI.Content.
            if (isIndeterminate)
            {
                progressRing.IsActive = true;
                WinUI.Content = progressRing;
            }
            else
            {
                progressBar.IsIndeterminate = false;
                WinUI.Content = progressBar;
            }
        }

        public void setProgress(int progress) { progressBar.Value = progress; }

        public int getProgress() { return (int)progressBar.Value; }

        public void setMax(int max) { progressBar.Maximum = max; }

        public int getMax() { return (int)progressBar.Maximum; }

        public void setIndeterminate(bool indeterminate)
        {
            isIndeterminate = indeterminate;
            progressBar.IsIndeterminate = indeterminate;
        }

        public bool isIndeterminateState() { return isIndeterminate; }

        public void incrementProgressBy(int diff) { progressBar.Value += diff; }

        public void show() { WinUI.Visibility = Visibility.Visible; }

        public void hide() { WinUI.Visibility = Visibility.Collapsed; }
    }
}
