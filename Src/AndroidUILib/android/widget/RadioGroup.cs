using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A group of RadioButtons. Only one RadioButton can be checked at a time.
    /// Maps to a StackPanel containing RadioButtons with shared GroupName.
    /// </summary>
    public class RadioGroup : LinearLayout
    {
        public interface OnCheckedChangeListener
        {
            void onCheckedChanged(RadioGroup group, int checkedId);
        }

        private OnCheckedChangeListener listener;
        private static int groupCounter = 0;
        private string groupName;

        public RadioGroup(Context context, AttributeSet attrs) : base(context, attrs)
        {
            groupName = "RadioGroup_" + (++groupCounter);
        }

        public void setOnCheckedChangeListener(OnCheckedChangeListener l)
        {
            listener = l;
        }

        public int getCheckedRadioButtonId()
        {
            return -1;
        }

        public void check(int id) { }

        public void clearCheck() { }
    }
}
