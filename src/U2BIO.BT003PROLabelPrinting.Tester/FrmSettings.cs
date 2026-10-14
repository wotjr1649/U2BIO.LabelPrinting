using System;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraTab;
using DevExpress.XtraVerticalGrid;
using DevExpress.XtraVerticalGrid.Rows;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal partial class FrmSettings : XtraForm
    {
        private readonly TesterSettings settings;

        internal FrmSettings(TesterSettings settings)
        {
            InitializeComponent();
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            rowPort.Properties.Value = settings.Port;
            rowDistance.Properties.Value = settings.PresentationDistanceMm;
            rowFolder.Properties.Value = settings.TemplateFolder;
            lblWarning.Text = settings.LoadWarning;
            layoutItemWarning.Visibility = settings.LoadWarning == null ? LayoutVisibility.Never : LayoutVisibility.Always;
        }

        private void FrmSettings_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F12) return;
            e.Handled = true;
            btnOK.PerformClick();
        }

        private void grid_ValidatingEditor(object sender, BaseContainerValidateEditorEventArgs e)
        {
            var grid = (VGridControl)sender;
            string error = Check(grid.FocusedRow, e.Value);
            grid.SetRowError(grid.FocusedRow.Properties, error);
            if (error == null) return;
            e.Valid = false;
            e.ErrorText = error;
        }

        private void repositoryFolder_ButtonClick(object sender, ButtonPressedEventArgs e)
        {
            var edit = (ButtonEdit)sender;
            using (var dialog = new FolderBrowserDialog { SelectedPath = edit.Text })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) edit.EditValue = dialog.SelectedPath;
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            // 버튼이 DialogResult.OK를 먼저 넣으므로 실패 경로마다 None으로 되돌려 창을 유지한다.
            DialogResult = DialogResult.None;
            if (!gridConnection.PostEditor() || !gridTemplate.PostEditor()) return;
            gridConnection.CloseEditor();
            gridTemplate.CloseEditor();
            if (ShowError(gridConnection, pageConnection, rowPort) || ShowError(gridConnection, pageConnection, rowDistance)
                || ShowError(gridTemplate, pageTemplate, rowFolder)) return;
            try
            {
                settings.Update(Convert.ToString(rowPort.Properties.Value), ReadDistance(rowDistance.Properties.Value),
                    Convert.ToString(rowFolder.Properties.Value));
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException error)
            {
                XtraMessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                XtraMessageBox.Show(this, "설정을 저장하지 못했습니다." + Environment.NewLine + error.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ShowError(VGridControl grid, XtraTabPage page, EditorRow row)
        {
            string error = Check(row, row.Properties.Value);
            grid.SetRowError(row.Properties, error);
            if (error == null) return false;
            tabSettings.SelectedTabPage = page;
            grid.Focus();
            grid.FocusedRow = row;
            return true;
        }

        private string Check(BaseRow row, object value)
        {
            if (row == rowPort) return TesterSettings.CheckPort(Convert.ToString(value));
            if (row == rowDistance) return TesterSettings.CheckDistance(ReadDistance(value));
            if (row == rowFolder) return TesterSettings.CheckFolder(Convert.ToString(value));
            return null;
        }

        // 스핀 편집기는 decimal을 주므로 정수가 아니거나 int 범위 밖이면 범위 오류 값(-1)으로 바꾼다.
        private static int ReadDistance(object value)
        {
            if (!(value is int || value is decimal)) return -1;
            decimal number = Convert.ToDecimal(value);
            return number == decimal.Truncate(number) && number >= 0 && number <= int.MaxValue ? (int)number : -1;
        }
    }
}
