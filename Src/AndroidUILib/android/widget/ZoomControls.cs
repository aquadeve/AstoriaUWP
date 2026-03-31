using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Simple set of controls used for zooming.
    /// Maps to a horizontal StackPanel with zoom in/out Buttons.
    /// </summary>
    public class ZoomControls : LinearLayout
    {
        private StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal };
        private Button zoomIn = new Button { Content = "+" };
        private Button zoomOut = new Button { Content = "-" };

        public ZoomControls(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            panel.Children.Add(zoomOut);
            panel.Children.Add(zoomIn);
            WinUI.Content = panel;
        }

        public void setOnZoomInClickListener(object listener) { }

        public void setOnZoomOutClickListener(object listener) { }

        public void show() { panel.Visibility = Visibility.Visible; }

        public void hide() { panel.Visibility = Visibility.Collapsed; }
    }
}
