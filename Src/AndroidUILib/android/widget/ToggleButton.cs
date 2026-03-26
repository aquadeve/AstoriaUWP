using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A button that has two states: checked and unchecked.
    /// Maps to UWP ToggleButton control.
    /// </summary>
    public class ToggleButton : CompoundButton
    {
        private Windows.UI.Xaml.Controls.Primitives.ToggleButton winToggle
            = new Windows.UI.Xaml.Controls.Primitives.ToggleButton();
        private string textOn = "ON";
        private string textOff = "OFF";

        public ToggleButton(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            winToggle.Content = mChecked ? textOn : textOff;
            winToggle.Checked += (s, e) =>
            {
                mChecked = true;
                winToggle.Content = textOn;
            };
            winToggle.Unchecked += (s, e) =>
            {
                mChecked = false;
                winToggle.Content = textOff;
            };
            WinUI.Content = winToggle;
        }

        public override void setText(string text) { winToggle.Content = text; }

        public void setTextOn(string text) { textOn = text; }

        public void setTextOff(string text) { textOff = text; }

        public override void setChecked(bool checked_)
        {
            base.setChecked(checked_);
            winToggle.IsChecked = checked_;
            winToggle.Content = checked_ ? textOn : textOff;
        }
    }
}
