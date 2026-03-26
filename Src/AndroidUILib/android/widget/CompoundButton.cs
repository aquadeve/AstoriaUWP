using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// Base class for compound buttons (CheckBox, RadioButton, ToggleButton, Switch).
    /// Extends View directly (not Button) because UWP compound controls have their own
    /// visual presentation and do not share the UWP Button WinUI backing field.
    /// Maps to Windows UWP toggle-style controls.
    /// </summary>
    public abstract class CompoundButton : View
    {
        public interface OnCheckedChangeListener
        {
            void onCheckedChanged(CompoundButton buttonView, bool isChecked);
        }

        private OnCheckedChangeListener checkedChangeListener;

        protected bool mChecked = false;

        public CompoundButton(Context context, AttributeSet attrs) : base(context, attrs) { }

        public virtual bool isChecked() { return mChecked; }

        public virtual void setChecked(bool checked_)
        {
            if (mChecked != checked_)
            {
                mChecked = checked_;
                checkedChangeListener?.onCheckedChanged(this, mChecked);
            }
        }

        public void toggle() { setChecked(!mChecked); }

        public void setOnCheckedChangeListener(OnCheckedChangeListener listener)
        {
            checkedChangeListener = listener;
        }

        /// <summary>Sets the label text of the compound button control.</summary>
        public virtual void setText(string text) { }

        public virtual void setText(int resId) { }
    }
}
