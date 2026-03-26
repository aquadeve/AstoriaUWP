using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// TabLayout provides a horizontal layout to display tabs.
    /// Maps to UWP Pivot control, where each tab is a PivotItem.
    /// </summary>
    public class TabLayout : ViewGroup
    {
        private Pivot pivot = new Pivot();

        public const int MODE_FIXED = 1;
        public const int MODE_SCROLLABLE = 0;
        public const int GRAVITY_FILL = 0;
        public const int GRAVITY_CENTER = 1;

        public class Tab
        {
            internal PivotItem pivotItem = new PivotItem();

            public Tab setText(string text)
            {
                pivotItem.Header = text ?? string.Empty;
                return this;
            }

            public string getText()
            {
                return pivotItem.Header?.ToString() ?? string.Empty;
            }

            public Tab setIcon(int resId) { return this; }

            public Tab setTag(object tag) { return this; }

            public Tab select() { return this; }
        }

        public interface OnTabSelectedListener
        {
            void onTabSelected(Tab tab);
            void onTabUnselected(Tab tab);
            void onTabReselected(Tab tab);
        }

        public TabLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            // Pivot inherits ItemsControl → Control (not ContentControl), so use WinUI.Content.
            WinUI.Content = pivot;
        }

        public Tab newTab()
        {
            return new Tab();
        }

        public void addTab(Tab tab)
        {
            pivot.Items.Add(tab.pivotItem);
        }

        public void addTab(Tab tab, bool setSelected)
        {
            addTab(tab);
            if (setSelected && pivot.Items.Count > 0)
                pivot.SelectedIndex = pivot.Items.Count - 1;
        }

        public void addTab(Tab tab, int position)
        {
            pivot.Items.Insert(position, tab.pivotItem);
        }

        public Tab getTabAt(int index)
        {
            if (index >= 0 && index < pivot.Items.Count)
            {
                Tab t = new Tab();
                t.pivotItem = (PivotItem)pivot.Items[index];
                return t;
            }
            return null;
        }

        public int getTabCount() { return pivot.Items.Count; }

        public int getSelectedTabPosition() { return pivot.SelectedIndex; }

        public void selectTab(Tab tab) { }

        public void setTabGravity(int gravity) { }

        public void setTabMode(int mode) { }

        public void addOnTabSelectedListener(OnTabSelectedListener listener) { }

        public void removeOnTabSelectedListener(OnTabSelectedListener listener) { }

        public void setupWithViewPager(object viewPager) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
