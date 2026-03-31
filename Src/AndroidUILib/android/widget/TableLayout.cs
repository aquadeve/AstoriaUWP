using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A layout that arranges its children into rows and columns.
    /// Maps to UWP Grid control.
    /// </summary>
    public class TableLayout : LinearLayout
    {
        private Grid grid = new Grid();
        private int rowIndex = 0;

        public TableLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            grid.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = grid;
        }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(view.WinUI, rowIndex);
            grid.Children.Add(view.WinUI);
            rowIndex++;
        }

        public override void removeView(View view)
        {
            grid.Children.Remove(view.WinUI);
        }

        public void setColumnStretchable(int col, bool stretchable) { }

        public void setColumnShrinkable(int col, bool shrinkable) { }
    }
}
