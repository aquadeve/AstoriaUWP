using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// Represents a standard bottom navigation bar for application-level navigation
    /// between three to five destinations.
    /// Maps to UWP CommandBar positioned at the bottom.
    /// </summary>
    public class BottomNavigationView : ViewGroup
    {
        private CommandBar commandBar = new CommandBar();

        public interface OnNavigationItemSelectedListener
        {
            bool onNavigationItemSelected(MenuItem item);
        }

        public interface OnNavigationItemReselectedListener
        {
            void onNavigationItemReselected(MenuItem item);
        }

        public BottomNavigationView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            // CommandBar inherits AppBar → ContentControl; assign directly.
            commandBar.VerticalAlignment = VerticalAlignment.Bottom;
            commandBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI = commandBar;
        }

        public void setOnNavigationItemSelectedListener(OnNavigationItemSelectedListener listener) { }

        public void setOnNavigationItemReselectedListener(OnNavigationItemReselectedListener listener) { }

        public int getSelectedItemId() { return 0; }

        public void setSelectedItemId(int id) { }

        public void inflateMenu(int resId) { }

        public Menu getMenu()
        {
            // Cannot instantiate interface Menu; return null or a valid implementation if available.
            return null;
        }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
