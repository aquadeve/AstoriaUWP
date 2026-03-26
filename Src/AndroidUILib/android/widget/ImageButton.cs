using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Displays a button with an image (instead of text). Maps to UWP Button with Image content.
    /// </summary>
    public class ImageButton : ImageView
    {
        private Windows.UI.Xaml.Controls.Button button = new Windows.UI.Xaml.Controls.Button();

        public ImageButton(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // Windows.UI.Xaml.Controls.Button inherits ContentControl, assign directly.
            image.Width = 24;
            image.Height = 24;
            button.Content = image;
            button.Padding = new Windows.UI.Xaml.Thickness(8);
            WinUI = button;
        }

        public void setOnClickListener(System.Action onClick)
        {
            button.Click += (s, e) => onClick?.Invoke();
        }
    }
}
