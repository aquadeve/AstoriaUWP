using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Displays a list of tab labels representing each page in the parent's tab collection.
    /// Maps to a horizontal StackPanel.
    /// </summary>
    public class TabWidget : LinearLayout
    {
        public TabWidget(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            base.CreateWinUI(obj);
        }

        public void setCurrentTab(int index) { }

        public int getTabCount() { return 0; }
    }
}
