using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// ViewAnimator that switches between two views. Only one child is shown at a time.
    /// Maps to UWP Grid (overlay container).
    /// </summary>
    public class ViewSwitcher : ViewGroup
    {
        private Grid container = new Grid();
        private int displayedChild;

        public ViewSwitcher(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            container.HorizontalAlignment = HorizontalAlignment.Stretch;
            container.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = container;
        }

        public int getDisplayedChild() { return displayedChild; }

        public void setDisplayedChild(int whichChild)
        {
            displayedChild = whichChild;
            for (int i = 0; i < container.Children.Count; i++)
            {
                container.Children[i].Visibility = (i == whichChild)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public void showNext()
        {
            if (container.Children.Count > 0)
                setDisplayedChild((displayedChild + 1) % container.Children.Count);
        }

        public void showPrevious()
        {
            if (container.Children.Count > 0)
                setDisplayedChild((displayedChild - 1 + container.Children.Count) % container.Children.Count);
        }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param)
        {
            container.Children.Add(view.WinUI);
            if (container.Children.Count > 1)
                view.WinUI.Visibility = Visibility.Collapsed;
        }

        public override void removeView(View view)
        {
            container.Children.Remove(view.WinUI);
        }
    }
}
