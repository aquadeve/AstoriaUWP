using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A search bar widget.
    /// Maps to UWP AutoSuggestBox control.
    /// </summary>
    public class SearchView : LinearLayout
    {
        private AutoSuggestBox searchBox = new AutoSuggestBox();

        public interface OnQueryTextListener
        {
            bool onQueryTextSubmit(string query);
            bool onQueryTextChange(string newText);
        }

        private OnQueryTextListener queryTextListener;

        public SearchView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            searchBox.QuerySubmitted += (s, e) =>
            {
                queryTextListener?.onQueryTextSubmit(e.QueryText);
            };
            searchBox.TextChanged += (s, e) =>
            {
                queryTextListener?.onQueryTextChange(searchBox.Text);
            };
            WinUI.Content = searchBox;
        }

        public void setQuery(string query, bool submit)
        {
            searchBox.Text = query ?? string.Empty;
            if (submit)
                queryTextListener?.onQueryTextSubmit(query);
        }

        public string getQuery() { return searchBox.Text; }

        public void setQueryHint(string hint)
        {
            searchBox.PlaceholderText = hint ?? string.Empty;
        }

        public void setIconified(bool iconified) { }

        public void setOnQueryTextListener(OnQueryTextListener listener)
        {
            queryTextListener = listener;
        }
    }
}
