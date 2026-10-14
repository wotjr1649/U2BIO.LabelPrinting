using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class PreviewPanel : PanelControl
    {
        public PreviewPanel()
        {
            // 크기를 바꾸거나 다시 그릴 때 깜박이지 않게 한 번에 그린다.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }
    }
}
