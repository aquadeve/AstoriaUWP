using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.v4.widget
{
    public class SwipeRefreshLayout : ViewGroup
    {
        private Grid container = new Grid();
        private bool refreshing;
        private OnRefreshListener listener;

        public interface OnRefreshListener
        {
            void onRefresh();
        }

        public SwipeRefreshLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            container.HorizontalAlignment = HorizontalAlignment.Stretch;
            container.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = container;
        }

        public void setOnRefreshListener(OnRefreshListener listener)
        {
            this.listener = listener;
        }

        public void setRefreshing(bool refreshing)
        {
            this.refreshing = refreshing;
        }

        public bool isRefreshing() { return refreshing; }

        public void setEnabled(bool enabled)
        {
            WinUI.IsEnabled = enabled;
        }

        public void setColorSchemeColors(params int[] colors) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            container.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            container.Children.Remove(view.WinUI);
        }
    }
}
