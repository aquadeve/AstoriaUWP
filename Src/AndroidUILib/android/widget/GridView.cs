using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A view that shows items in a two-dimensional, scrolling grid.
    /// Maps to UWP GridView control wrapped in a ContentControl.
    /// </summary>
    public class GridView : ViewGroup
    {
        // Use field initializer so it is ready before base constructor calls CreateWinUI
        private Windows.UI.Xaml.Controls.GridView gridView
            = new Windows.UI.Xaml.Controls.GridView();

        public interface OnItemClickListener
        {
            void onItemClick(GridView parent, View view, int position, long id);
        }

        private OnItemClickListener listener;

        public GridView(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            gridView.HorizontalAlignment = HorizontalAlignment.Stretch;
            gridView.VerticalAlignment = VerticalAlignment.Stretch;
            gridView.ItemClick += (s, e) =>
            {
                int idx = gridView.Items.IndexOf(e.ClickedItem);
                listener?.onItemClick(this, null, idx, idx);
            };
            WinUI.Content = gridView;
        }

        public void setAdapter(object adapter) { }

        public void setNumColumns(int numColumns) { }

        public void setOnItemClickListener(OnItemClickListener l) { listener = l; }

        public override void addView(View view)
        {
            gridView.Items.Add(view.WinUI);
        }

        public override void addView(View view, LayoutParams param)
        {
            gridView.Items.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            gridView.Items.Remove(view.WinUI);
        }
    }
}
