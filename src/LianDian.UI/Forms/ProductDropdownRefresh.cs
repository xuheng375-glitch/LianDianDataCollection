using System;
using System.Windows.Forms;
using Sunny.UI;

namespace LianDian.UI.Forms
{
    /// <summary>SunnyUI 下拉框是密封类型，在输入消息分发前刷新，避免 DropDown 事件晚于列表生成。</summary>
    public sealed class ProductDropdownRefresh : IMessageFilter, IDisposable
    {
        private readonly UIComboBox _combo;
        private readonly Action _refresh;
        public ProductDropdownRefresh(UIComboBox combo, Action refresh)
        {
            _combo = combo;
            _refresh = refresh;
            Application.AddMessageFilter(this);
            _combo.Disposed += OnDisposed;
        }
        public bool PreFilterMessage(ref Message m)
        {
            bool opening = m.Msg == 0x201 || ((m.Msg == 0x100 || m.Msg == 0x104) &&
                ((Keys)m.WParam.ToInt32() == Keys.F4 || (Keys)m.WParam.ToInt32() == Keys.Down));
            if (opening && !_combo.IsDisposed && !_combo.DroppedDown)
            {
                Control target = Control.FromHandle(m.HWnd);
                if (target == _combo || (target != null && _combo.Contains(target))) _refresh();
            }
            return false;
        }
        private void OnDisposed(object sender, EventArgs e) => Dispose();
        public void Dispose()
        {
            Application.RemoveMessageFilter(this);
            _combo.Disposed -= OnDisposed;
        }
    }
}
