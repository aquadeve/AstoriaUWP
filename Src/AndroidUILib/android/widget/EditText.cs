using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.org.xmlpull.v1;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    public class EditText : TextView
    {
        private TextBox textBox = new TextBox();

        public EditText(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // TextBox inherits Control (not ContentControl), so use WinUI.Content.
            WinUI.Content = textBox;
            if (obj != null && obj.Length > 1 && obj[1] is AttributeSet a)
            {
                string hint = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "hint");
                if (hint != null) textBox.PlaceholderText = hint;

                string text = a.getAttributeValue(XmlPullParser.ANDROID_NAMESPACE, "text");
                if (text != null) textBox.Text = text;
            }
        }

        public override void setText(string text)
        {
            textBox.Text = text ?? string.Empty;
        }

        public new string getText()
        {
            return textBox.Text;
        }

        public void setHint(string hint)
        {
            textBox.PlaceholderText = hint ?? string.Empty;
        }

        public string getHint()
        {
            return textBox.PlaceholderText;
        }

        public void setInputType(int type) { }
        public void setMaxLines(int maxLines) { textBox.AcceptsReturn = maxLines > 1; }
        public void setSingleLine(bool singleLine) { textBox.AcceptsReturn = !singleLine; }
        public void setSelectAllOnFocus(bool selectAllOnFocus) { }
        public void addTextChangedListener(object watcher) { }
        public void setError(string error) { }
    }
}
