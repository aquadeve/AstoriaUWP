using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A view group that allows the view hierarchy placed within it to be scrolled horizontally.
    /// Maps to UWP ScrollViewer with horizontal scrolling.
    /// </summary>
    public class HorizontalScrollView : ViewGroup
    {
        private ScrollViewer scrollViewer = new ScrollViewer();
        private StackPanel contentPanel = new StackPanel { Orientation = Orientation.Horizontal };

        public HorizontalScrollView() : base(null, null)
        {
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scrollViewer.Content = contentPanel;
            this.WinUI = scrollViewer;
        }

        public override void CreateWinUI(params object[] obj)
        {
            // Initialized in constructor
        }

        public override void addView(View view, LayoutParams param)
        {
            contentPanel.Children.Add(view.WinUI);
        }

        public override void addView(View view)
        {
            contentPanel.Children.Add(view.WinUI);
        }

        public void addView(UIElement element)
        {
            contentPanel.Children.Add(element);
        }

        public override void removeView(View view)
        {
            contentPanel.Children.Remove(view.WinUI);
        }
    }
}
