using System;
using System.Collections.Generic;
using System.Globalization;
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
            // Designer 직렬화 형식을 지키려고 버튼 툴팁은 코드에서 정한다.
            repositoryPort.Buttons[1].ToolTip = "다시 감지";
            FillPorts(settings.Port, repositoryPort.Items);
            rowPort.Properties.Value = settings.Port;
            rowDistance.Properties.Value = settings.PresentationDistanceMm;
            PrinterCalibration calibration = settings.Calibration;
            // 저장 값이 열거형이라 항목은 코드에서 채운다.
            repositoryMediaMode.Items.AddRange(new[]
            {
                new ImageComboBoxItem("블랙마크(^Q 세 인수)", BtplLabelMode.BlackMark),
                new ImageComboBoxItem("간격(^Q 두 인수)", BtplLabelMode.Gap),
            });
            rowMediaMode.Properties.Value = settings.LabelMediaMode;
            rowDpi.Properties.Value = calibration.Dpi.ToString(CultureInfo.InvariantCulture);
            rowSensor.Properties.Value = Mm(calibration.SensorOffsetMm);
            rowVertical.Properties.Value = Mm(calibration.VerticalOffsetMm);
            rowHorizontal.Properties.Value = Mm(calibration.HorizontalOffsetMm);
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

        // 저장 값은 목록 번호 문자열이고 화면에는 번호와 장치 이름을 함께 보인다.
        private void FillPorts(string currentPort, params ImageComboBoxItemCollection[] targets)
        {
            IReadOnlyList<UsbPrinterPort> ports = UsbPrinterList.Detect(currentPort, out string summary);
            foreach (ImageComboBoxItemCollection items in targets)
            {
                items.BeginUpdate();
                try
                {
                    items.Clear();
                    foreach (UsbPrinterPort port in ports) items.Add(new ImageComboBoxItem(port.Display, port.Port));
                }
                finally
                {
                    items.EndUpdate();
                }
            }
            rowDetected.Properties.Value = summary;
            // 긴 안내가 칸에서 잘려도 전체를 볼 수 있게 한다.
            rowDetected.Properties.ToolTip = summary;
        }

        private void repositoryPort_ButtonClick(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button.Kind != ButtonPredefines.Redo) return;
            var edit = (ImageComboBoxEdit)sender;
            string current = Convert.ToString(edit.EditValue);
            // 셀 편집기는 저장소 항목의 복사본을 쓰므로 둘 다 다시 채운다.
            FillPorts(current, repositoryPort.Items, edit.Properties.Items);
            edit.EditValue = current;
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (!gridConnection.PostEditor() || !gridCalibration.PostEditor()) return;
            gridConnection.CloseEditor();
            gridCalibration.CloseEditor();
            if (ShowError(gridConnection, pageConnection, rowPort) || ShowError(gridConnection, pageConnection, rowDistance)
                || ShowError(gridCalibration, pageCalibration, rowDpi)
                || ShowError(gridCalibration, pageCalibration, rowSensor) || ShowError(gridCalibration, pageCalibration, rowVertical)
                || ShowError(gridCalibration, pageCalibration, rowHorizontal)) return;
            try
            {
                var calibration = new PrinterCalibration(ReadDpi(rowDpi.Properties.Value), ReadMm(rowSensor.Properties.Value),
                    ReadMm(rowVertical.Properties.Value), ReadMm(rowHorizontal.Properties.Value));
                settings.Update(Convert.ToString(rowPort.Properties.Value), ReadDistance(rowDistance.Properties.Value),
                    calibration, (BtplLabelMode)rowMediaMode.Properties.Value);
                // 버튼에 DialogResult를 두지 않고 저장이 성공했을 때만 닫는다.
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
            if (row == rowDpi) return PrinterCalibration.CheckDpi(ReadDpi(value));
            if (row == rowSensor || row == rowHorizontal || row == rowVertical)
            {
                if (!TryMm(value, out decimal mm)) return "숫자(mm)를 입력하세요.";
                return PrinterCalibration.CheckOffset(mm, row != rowVertical);
            }
            return null;
        }

        private static string Mm(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static int ReadDpi(object value) =>
            int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int dpi) ? dpi : -1;

        private static bool TryMm(object value, out decimal mm) =>
            decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out mm);

        // 확인 전에 Check로 걸렀으므로 여기서는 읽기만 한다.
        private static decimal ReadMm(object value) => TryMm(value, out decimal mm) ? mm : 0m;

        // 스핀 편집기에 범위를 두면 1001이 1000으로 바뀌어 오류 없이 저장되므로 범위는 여기서만 검사한다.
        // 정수가 아니거나 int 범위 밖·빈 값은 범위 오류 값(-1)으로 바꾼다.
        private static int ReadDistance(object value)
        {
            if (!(value is int || value is decimal)) return -1;
            decimal number = Convert.ToDecimal(value);
            return number == decimal.Truncate(number) && number >= 0 && number <= int.MaxValue ? (int)number : -1;
        }
    }
}
