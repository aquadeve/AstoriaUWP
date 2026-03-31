using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Deprecated layout that lets you specify exact (x, y) coordinates for its children.
    /// Maps to UWP Canvas control.
    /// </summary>
    public class AbsoluteLayout : ViewGroup
    {
        private Canvas canvas = new Canvas();

        public AbsoluteLayout(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
            canvas.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = canvas;
        }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, ViewGroup.LayoutParams param)
        {
            canvas.Children.Add(view.WinUI);
            
            // If the param is an AbsoluteLayout.LayoutParams, set the position
            if (param is LayoutParams absoluteParams)
            {
                Canvas.SetLeft(view.WinUI, absoluteParams.x);
                Canvas.SetTop(view.WinUI, absoluteParams.y);
            }
        }

        public override void removeView(View view)
        {
            canvas.Children.Remove(view.WinUI);
        }

        public new class LayoutParams : ViewGroup.LayoutParams
        {
            public int x;
            public int y;

            public LayoutParams(int width, int height, int x, int y) : base(width, height)
            {
                this.x = x;
                this.y = y;
            }
        }
    }
}
