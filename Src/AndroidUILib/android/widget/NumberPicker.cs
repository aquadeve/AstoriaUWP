using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A widget that enables the user to select a number from a predefined range.
    /// Maps to UWP ComboBox populated with number items.
    /// </summary>
    public class NumberPicker : LinearLayout
    {
        private ComboBox comboBox = new ComboBox();
        private int minValue = 0;
        private int maxValue = 100;

        public NumberPicker(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = comboBox;
            UpdateItems();
        }

        public void setMinValue(int min)
        {
            minValue = min;
            UpdateItems();
        }

        public void setMaxValue(int max)
        {
            maxValue = max;
            UpdateItems();
        }

        public void setValue(int value)
        {
            if (value >= minValue && value <= maxValue)
                comboBox.SelectedIndex = value - minValue;
        }

        public int getValue()
        {
            return comboBox.SelectedIndex >= 0 ? comboBox.SelectedIndex + minValue : minValue;
        }

        public int getMinValue() { return minValue; }

        public int getMaxValue() { return maxValue; }

        private void UpdateItems()
        {
            comboBox.Items.Clear();
            for (int i = minValue; i <= maxValue; i++)
                comboBox.Items.Add(i);
        }
    }
}
