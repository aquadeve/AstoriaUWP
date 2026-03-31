using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Abstract base class for adapter-based views.
    /// </summary>
    public abstract class AdapterView : ViewGroup
    {
        public interface OnItemClickListener
        {
            void onItemClick(AdapterView parent, View view, int position, long id);
        }

        public interface OnItemLongClickListener
        {
            void onItemLongClick(AdapterView parent, View view, int position, long id);
        }

        public interface OnItemSelectedListener
        {
            void onItemSelected(AdapterView parent, View view, int position, long id);
            void onNothingSelected(AdapterView parent);
        }

        private OnItemClickListener itemClickListener;
        private OnItemSelectedListener itemSelectedListener;

        public AdapterView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj) { }

        public void setOnItemClickListener(OnItemClickListener listener)
        {
            itemClickListener = listener;
        }

        public void setOnItemSelectedListener(OnItemSelectedListener listener)
        {
            itemSelectedListener = listener;
        }

        public int getSelectedItemPosition() { return -1; }

        public object getSelectedItem() { return null; }

        public void setSelection(int position) { }

        public object getItemAtPosition(int position) { return null; }

        public int getCount() { return 0; }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
