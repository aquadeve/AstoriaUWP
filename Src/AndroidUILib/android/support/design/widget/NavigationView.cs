using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    /// <summary>
    /// Represents a standard navigation menu for application navigation.
    /// Maps to UWP NavigationView control.
    /// </summary>
    public class NavigationView : ViewGroup
    {
        private Windows.UI.Xaml.Controls.NavigationView navigationView
            = new Windows.UI.Xaml.Controls.NavigationView();

        public interface OnNavigationItemSelectedListener
        {
            bool onNavigationItemSelected(MenuItem item);
        }

        public NavigationView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            // NavigationView inherits Control (not ContentControl), so use WinUI.Content.
            WinUI.Content = navigationView;
        }

        public void setNavigationItemSelectedListener(OnNavigationItemSelectedListener listener) { }

        public void inflateMenu(int resId) { }

        private class SimpleMenu : Menu
        {
            // Implement Menu interface members here as needed.
        }

        public Menu getMenu() { return new SimpleMenu(); }

        public void setCheckedItem(int id) { }

        public void setHeaderLayoutResource(int resId) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            navigationView.Content = view.WinUI;
        }

        public override void removeView(View view)
        {
            navigationView.Content = null;
        }
    }
}
