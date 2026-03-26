using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// Chips are compact elements that represent an input, attribute, or action.
    /// Maps to a styled UWP Button with rounded corners.
    /// </summary>
    public class Chip : View
    {
        private Button button = new Button();

        public Chip(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // Windows.UI.Xaml.Controls.Button inherits ContentControl, assign directly.
            button.CornerRadius = new Windows.UI.Xaml.CornerRadius(16);
            button.Margin = new Thickness(4, 2, 4, 2);
            button.Padding = new Thickness(12, 4, 12, 4);
            button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224));
            WinUI = button;
        }

        public void setText(string text) { button.Content = text; }

        public string getText() { return button.Content?.ToString() ?? string.Empty; }

        public void setCheckable(bool checkable) { }

        public void setChecked(bool checked_) { }

        public bool isChecked() { return false; }

        public void setClickable(bool clickable) { button.IsEnabled = clickable; }

        public void setCloseIconVisible(bool visible) { }

        public void setChipIcon(object drawable) { }

        public void setChipBackgroundColor(int color)
        {
            button.Background = new SolidColorBrush(ticomware.interop.Util.IntToColor(color));
        }
    }
}
