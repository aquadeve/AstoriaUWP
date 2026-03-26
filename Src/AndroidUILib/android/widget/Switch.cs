using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A two-state toggle widget. Maps to UWP ToggleSwitch.
    /// Note: "Switch" is a valid C# class name (lowercase "switch" is the keyword).
    /// </summary>
    public class Switch : CompoundButton
    {
        private ToggleSwitch toggleSwitch = new ToggleSwitch();

        public Switch(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            toggleSwitch.IsOn = mChecked;
            toggleSwitch.Toggled += (s, e) => { mChecked = toggleSwitch.IsOn; };
            WinUI.Content = toggleSwitch;
        }

        public override void setText(string text) { toggleSwitch.Header = text; }

        public void setTextOn(string text) { toggleSwitch.OnContent = text; }

        public void setTextOff(string text) { toggleSwitch.OffContent = text; }

        public override bool isChecked() { return toggleSwitch.IsOn; }

        public override void setChecked(bool checked_)
        {
            base.setChecked(checked_);
            toggleSwitch.IsOn = checked_;
        }
    }
}
