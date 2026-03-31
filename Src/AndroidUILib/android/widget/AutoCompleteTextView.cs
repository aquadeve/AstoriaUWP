using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// An editable text view that shows completion suggestions automatically.
    /// Maps to UWP AutoSuggestBox control.
    /// </summary>
    public class AutoCompleteTextView : EditText
    {
        private AutoSuggestBox autoSuggestBox = new AutoSuggestBox();
        private int threshold = 1;

        public AutoCompleteTextView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            autoSuggestBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            WinUI.Content = autoSuggestBox;
        }

        public void setThreshold(int threshold) { this.threshold = threshold; }

        public int getThreshold() { return threshold; }

        public new void setAdapter(object adapter) { }

        public void showDropDown() { autoSuggestBox.IsSuggestionListOpen = true; }

        public void dismissDropDown() { autoSuggestBox.IsSuggestionListOpen = false; }
    }
}
