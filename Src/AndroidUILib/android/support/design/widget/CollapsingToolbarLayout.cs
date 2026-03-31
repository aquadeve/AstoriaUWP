using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using AndroidInteropLib.android.widget;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AndroidInteropLib.android.support.design.widget
{
    public class CollapsingToolbarLayout : FrameLayout
    {
        private string title = string.Empty;

        public CollapsingToolbarLayout(Context c, AttributeSet a) : base(c, a) { }

        public void setTitle(string title)
        {
            this.title = title;
        }

        public string getTitle() { return title; }

        public void setCollapsedTitleTextColor(int color) { }

        public void setExpandedTitleColor(int color) { }
    }
}
