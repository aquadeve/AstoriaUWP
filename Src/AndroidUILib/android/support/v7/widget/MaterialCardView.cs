using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;

namespace AndroidInteropLib.android.support.v7.widget
{
    public class MaterialCardView : CardView
    {
        private bool checked_;
        private bool dragged;

        public MaterialCardView(Context c, AttributeSet a) : base(c, a) { }

        public void setChecked(bool checked_)
        {
            this.checked_ = checked_;
        }

        public bool isChecked() { return checked_; }

        public void setDragged(bool dragged)
        {
            this.dragged = dragged;
        }
    }
}
