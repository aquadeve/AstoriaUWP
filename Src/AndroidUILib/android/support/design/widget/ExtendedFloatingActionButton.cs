using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AndroidInteropLib.android.support.design.widget
{
    public class ExtendedFloatingActionButton : android.widget.Button
    {
        private bool extended = true;

        public ExtendedFloatingActionButton(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            base.CreateWinUI(obj);

            int toolBarRef = (int)(mContext.getR().color.get("colorAccent") ?? -1);
            if (toolBarRef != -1)
            {
                int color = mContext.getResources().getColor(toolBarRef);
                WinUI.Background = new SolidColorBrush(ticomware.interop.Util.IntToColor(color));
            }
            else
            {
                WinUI.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 64, 129));
            }
        }

        public void extend()
        {
            extended = true;
            WinUI.Visibility = Visibility.Visible;
        }

        public void shrink()
        {
            extended = false;
        }

        public void show()
        {
            WinUI.Visibility = Visibility.Visible;
        }

        public void hide()
        {
            WinUI.Visibility = Visibility.Collapsed;
        }

        public bool isExtended() { return extended; }
    }
}
