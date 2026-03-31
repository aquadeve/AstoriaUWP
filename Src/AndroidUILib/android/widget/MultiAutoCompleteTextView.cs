using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// An editable text view that can show completion suggestions for the substring
    /// of the text where the user is typing, rather than for the entire thing.
    /// </summary>
    public class MultiAutoCompleteTextView : AutoCompleteTextView
    {
        public MultiAutoCompleteTextView(Context c, AttributeSet a) : base(c, a) { }

        public void setTokenizer(object tokenizer) { }
    }
}
