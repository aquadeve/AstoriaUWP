using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A layout that arranges its children in a single horizontal row.
    /// Used as a row inside a TableLayout.
    /// </summary>
    public class TableRow : LinearLayout
    {
        StackPanel rowContent = new StackPanel();

        public TableRow(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            rowContent.Orientation = Orientation.Horizontal;
            rowContent.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = rowContent;
        }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            rowContent.Children.Add(view);
        }

        public override void removeView(View view)
        {
            rowContent.Children.Remove(view);
        }
    }
}
