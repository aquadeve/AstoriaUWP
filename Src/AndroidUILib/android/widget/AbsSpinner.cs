using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Abstract base class for Spinner-like widgets.
    /// </summary>
    public abstract class AbsSpinner : ViewGroup
    {
        private object adapter;

        public AbsSpinner(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj) { }

        public void setAdapter(object adapter) { this.adapter = adapter; }

        public object getAdapter() { return adapter; }

        public int getCount() { return 0; }

        public void setSelection(int position) { }

        public override void addView(View view) { addView(view, null); }

        public override void addView(View view, LayoutParams param) { }

        public override void removeView(View view) { }
    }
}
