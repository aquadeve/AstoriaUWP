using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A flexible layout that allows placing views at arbitrary positions, supporting
    /// constraint-based positioning. Maps to UWP Grid (approximation).
    /// </summary>
    public class ConstraintLayout : ViewGroup
    {
        private Grid grid = new Grid();

        public ConstraintLayout(Context c, AttributeSet a) : base(c, a) { }

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
            grid.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            grid.Children.Remove(view.WinUI);
        }
    }
}
