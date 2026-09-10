using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sunny.UI;

namespace LianDian.UI.Forms
{
    /// <summary>SunnyUI 下拉框是密封类型，在输入消息分发前刷新，避免 DropDown 事件晚于列表生成。</summary>
    public sealed class ProductDropdownRefresh : IMessageFilter, IDisposable
    {
        private readonly UIComboBox _combo;
        private readonly Func<Task> _refresh;
        private bool _opening;
        private bool _disposed;
        public ProductDropdownRefresh(UIComboBox combo, Func<Task> refresh)
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
                if (target == _combo || (target != null && _combo.Contains(target)))
                {
                    if (!_opening) OpenAfterRefresh();
                    return true;
                }
            }
            return false;
        }
        private void OnDisposed(object sender, EventArgs e) => Dispose();
        private async void OpenAfterRefresh()
        {
            _opening = true;
            try
            {
                _combo.Focus();
                await _refresh();
                if (!_disposed && !_combo.IsDisposed && _combo.ContainsFocus) _combo.ShowDropDown();
            }
            catch (InvalidOperationException) { /* 窗体关闭期间不再展开。 */ }
            finally { _opening = false; }
        }
        public void Dispose()
        {
            _disposed = true;
            Application.RemoveMessageFilter(this);
            _combo.Disposed -= OnDisposed;
        }
    }
}
