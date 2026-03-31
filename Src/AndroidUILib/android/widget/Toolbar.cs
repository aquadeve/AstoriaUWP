using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A standard toolbar for use within application content.
    /// Maps to UWP CommandBar control.
    /// </summary>
    public class Toolbar : ViewGroup
    {
        private CommandBar commandBar = new CommandBar();
        private string title;
        private string subtitle;

        public Toolbar(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            commandBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = commandBar;
        }

        public void setTitle(string title)
        {
            this.title = title;
            commandBar.Content = title;
        }

        public string getTitle() { return title; }

        public void setSubtitle(string subtitle) { this.subtitle = subtitle; }

        public string getSubtitle() { return subtitle; }

        public void setNavigationIcon(object icon) { }

        public void setNavigationOnClickListener(object listener) { }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
