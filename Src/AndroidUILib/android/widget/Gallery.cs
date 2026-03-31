using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Deprecated gallery widget. Shows items in a center-locked, horizontally scrolling list.
    /// Maps to UWP FlipView control.
    /// </summary>
    public class Gallery : ViewGroup
    {
        private FlipView flipView = new FlipView();

        public Gallery(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            flipView.HorizontalAlignment = HorizontalAlignment.Stretch;
            flipView.VerticalAlignment = VerticalAlignment.Stretch;
            WinUI.Content = flipView;
        }

        public void setSelection(int position)
        {
            if (position >= 0 && position < flipView.Items.Count)
                flipView.SelectedIndex = position;
        }

        public int getSelectedItemPosition() { return flipView.SelectedIndex; }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param)
        {
            flipView.Items.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            flipView.Items.Remove(view.WinUI);
        }
    }
}
