using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A view containing controls for a MediaPlayer.
    /// Maps to a horizontal StackPanel with transport-style controls.
    /// </summary>
    public class MediaController : FrameLayout
    {
        private StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal };

        public MediaController(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            controls.HorizontalAlignment = HorizontalAlignment.Stretch;
            controls.VerticalAlignment = VerticalAlignment.Bottom;
            WinUI.Content = controls;
        }

        public void show() { controls.Visibility = Visibility.Visible; }

        public void hide() { controls.Visibility = Visibility.Collapsed; }

        public void setMediaPlayer(object player) { }
    }
}
