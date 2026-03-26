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
            WinUI.Content = navigationView;
        }

        public void setNavigationItemSelectedListener(OnNavigationItemSelectedListener listener) { }

        public void inflateMenu(int resId) { }

        public Menu getMenu() { return new Menu(); }

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
