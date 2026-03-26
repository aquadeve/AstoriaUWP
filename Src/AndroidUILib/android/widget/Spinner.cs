using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A view that displays one child at a time and lets the user pick among them.
    /// Maps to UWP ComboBox control.
    /// </summary>
    public class Spinner : View
    {
        private ComboBox comboBox = new ComboBox();

        public interface OnItemSelectedListener
        {
            void onItemSelected(Spinner parent, View view, int position, long id);
            void onNothingSelected(Spinner parent);
        }

        private OnItemSelectedListener listener;

        public Spinner(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // ComboBox inherits ItemsControl → Control (not ContentControl), so use WinUI.Content.
            comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedIndex >= 0)
                    listener?.onItemSelected(this, null, comboBox.SelectedIndex, comboBox.SelectedIndex);
                else
                    listener?.onNothingSelected(this);
            };
            WinUI.Content = comboBox;
        }

        public void setAdapter(object adapter) { }

        public int getSelectedItemPosition() { return comboBox.SelectedIndex; }

        public object getSelectedItem() { return comboBox.SelectedItem; }

        public void setSelection(int position) { comboBox.SelectedIndex = position; }

        public void setPrompt(string prompt) { comboBox.PlaceholderText = prompt; }

        public void setOnItemSelectedListener(OnItemSelectedListener l)
        {
            listener = l;
        }
    }
}
