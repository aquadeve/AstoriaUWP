using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.support.design.widget
{
    public class BottomAppBar : ViewGroup
    {
        private CommandBar commandBar = new CommandBar();

        public BottomAppBar(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            commandBar.VerticalAlignment = VerticalAlignment.Bottom;
            commandBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI = commandBar;
        }

        public void setTitle(string title) { }

        public void setSubtitle(string subtitle) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
