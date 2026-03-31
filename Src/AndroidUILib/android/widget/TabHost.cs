using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Container for a tabbed window view.
    /// Maps to UWP Pivot control.
    /// </summary>
    public class TabHost : FrameLayout
    {
        private Pivot pivot = new Pivot();

        public TabHost(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            pivot.HorizontalAlignment = HorizontalAlignment.Stretch;
            pivot.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = pivot;
        }

        public void setup() { }

        public void addTab(object tabSpec)
        {
            if (tabSpec is PivotItem item)
                pivot.Items.Add(item);
        }

        public void setCurrentTab(int index)
        {
            if (index >= 0 && index < pivot.Items.Count)
                pivot.SelectedIndex = index;
        }

        public int getCurrentTab() { return pivot.SelectedIndex; }
    }
}
