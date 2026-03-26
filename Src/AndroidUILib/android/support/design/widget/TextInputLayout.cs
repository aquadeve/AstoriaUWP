using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// Layout which wraps an EditText (or descendant) to show a floating label when
    /// the hint is hidden due to the user inputting text.
    /// Maps to a StackPanel with a header TextBlock above the input field.
    /// </summary>
    public class TextInputLayout : ViewGroup
    {
        private StackPanel panel = new StackPanel();
        private TextBlock hintLabel = new TextBlock();

        public TextInputLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            hintLabel.FontSize = 12;
            hintLabel.Opacity = 0.7;
            panel.Children.Add(hintLabel);
            WinUI.Content = panel;
        }

        public void setHint(string hint)
        {
            hintLabel.Text = hint ?? string.Empty;
        }

        public string getHint()
        {
            return hintLabel.Text;
        }

        public void setError(string error) { }

        public void setErrorEnabled(bool enabled) { }

        public void setPasswordVisibilityToggleEnabled(bool enabled) { }

        public void setCounterEnabled(bool enabled) { }

        public void setCounterMaxLength(int maxLength) { }

        public void setBoxStrokeColor(int color) { }

        public void setBoxBackgroundColor(int color) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            panel.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            panel.Children.Remove(view.WinUI);
        }
    }
}
