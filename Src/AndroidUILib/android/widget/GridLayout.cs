using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A layout that places its children in a rectangular grid.
    /// Maps to UWP Grid control.
    /// </summary>
    public class GridLayout : ViewGroup
    {
        public const int HORIZONTAL = 0;
        public const int VERTICAL = 1;

        private Grid grid = new Grid();
        private int columnCount = 1;
        private int rowCount = 1;

        public GridLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            grid.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = grid;
        }

        public void setColumnCount(int count)
        {
            columnCount = count;
            grid.ColumnDefinitions.Clear();
            for (int i = 0; i < count; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        public void setRowCount(int count)
        {
            rowCount = count;
            grid.RowDefinitions.Clear();
            for (int i = 0; i < count; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        public int getColumnCount() { return columnCount; }

        public int getRowCount() { return rowCount; }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            grid.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            grid.Children.Remove(view.WinUI);
        }
    }
}
