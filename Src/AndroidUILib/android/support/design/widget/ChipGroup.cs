using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// A ChipGroup is used to hold multiple Chip widgets. By default, the chips are
    /// reflowed across multiple lines. Maps to UWP StackPanel with horizontal orientation.
    /// </summary>
    public class ChipGroup : ViewGroup
    {
        private StackPanel stackPanel = new StackPanel { Orientation = Orientation.Horizontal };

        public ChipGroup(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            // StackPanel inherits Panel (not ContentControl), so use WinUI.Content.
            stackPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = stackPanel;
        }

        public void setSingleSelection(bool singleSelection) { }

        public void setSingleLine(bool singleLine) { }

        public void setChipSpacing(int chipSpacing) { }

        public int getCheckedChipId() { return -1; }

        public void clearCheck() { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            stackPanel.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            stackPanel.Children.Remove(view.WinUI);
        }
    }
}
