using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils.Menu;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraLayout.Utils;
using DevExpress.XtraVerticalGrid.Rows;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal partial class FrmMain : XtraForm
    {
        private const string Title = "BT-003 Pro 서식 Tester";
        private const string FileFilter = "TXT 서식 (*.txt)|*.txt|모든 파일 (*.*)|*.*";
        private const string AlignNotice = "^Q가 바뀌었습니다. 새 서식으로 한 장 인쇄한 뒤 물리 FEED 키와 위치 확인으로 정렬하세요.";
        private static readonly string[] ExternalEditCodes = { "AmbiguousFrame", "AmbiguousSetting", "LockedTextArguments" };
        private const NumberStyles MmStyle = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

        private readonly TesterSettings settings;
        private readonly TemplateDocument doc = new TemplateDocument();
        private readonly DXPopupMenu recentMenu = new DXPopupMenu();
        private readonly string startWarning;

        internal FrmMain()
        {
            InitializeComponent();
            settings = TesterSettings.Load(TesterSettings.DefaultFilePath);
            btnOpen.DropDownControl = recentMenu;
            startWarning = settings.LoadWarning;
            if (startWarning != null) ShowError(startWarning);
            else ClearMessage();
            RefreshView();
        }

        internal void ShowError(string message) => SetMessage(message, Color.Red);

        internal void ShowNotice(string message) => SetMessage(message, Color.Blue);

        private void ClearMessage() => SetMessage("", Color.Blue);

        private void SetMessage(string message, Color color)
        {
            lblMessage.Appearance.ForeColor = color;
            lblMessage.Text = message;
        }

        private void ShowException(Exception error)
        {
            if (!(error is BtplFormatException format))
            {
                ShowError(error.Message);
                return;
            }
            string line = format.TemplateLine.HasValue ? " (줄 " + format.TemplateLine.Value + ")" : "";
            string hint = ExternalEditCodes.Contains(format.Code) ? " 외부 편집기에서 고친 뒤 다시 여세요." : "";
            ShowError(format.Code + line + ": " + format.Message + hint);
        }

        // 서식 열기·저장에서 예상되는 실패만 오류 줄로 보내고, 그 밖의 예외는 전역 처리기에 맡긴다.
        private static bool IsDocumentError(Exception error) =>
            error is BtplFormatException || error is IOException || error is UnauthorizedAccessException
            || error is InvalidOperationException || error is ArgumentException || error is NotSupportedException;

        internal void RefreshView()
        {
            BtplTemplate template = doc.Template;
            bool open = template != null;
            Text = open ? Title + " - " + Path.GetFileName(template.FilePath ?? doc.DisplayPath) + (doc.IsDirty ? " *" : "") : Title;
            txtPath.Text = doc.DisplayPath;
            txtPath.ToolTip = doc.DisplayPath;
            lblDirty.Text = doc.IsDirty ? "수정됨" : "";
            lblTime.Text = doc.LastLoadedOrSaved.HasValue ? "열기/저장 시각 " + doc.LastLoadedOrSaved.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "";
            RefreshTexts(template);
            rowQ.Properties.Value = Parameters(template, "^Q") ?? "";
            rowW.Properties.Value = Parameters(template, "^W") ?? "";
            rowS.Properties.Value = Parameters(template, "^S") ?? "";
            rowH.Properties.Value = Parameters(template, "^H") ?? "";
            memoRaw.Text = open ? template.ToBtpl() : "";
            bool unsupported = open && template.UnsupportedLineNumbers.Count > 0;
            lblUnsupported.Text = unsupported ? "미지원 줄: " + string.Join(", ", template.UnsupportedLineNumbers) : "";
            layoutItemUnsupported.Visibility = unsupported ? LayoutVisibility.Always : LayoutVisibility.Never;
            RefreshRecentMenu();
            btnSave.Enabled = open;
            btnSaveAs.Enabled = open;
            gridTexts.Enabled = open;
            btnAddText.Enabled = open;
            btnRemoveText.Enabled = open && template.TextElements.Count > 0;
            gridSettings.Enabled = open;
            btnApplyBlackMark.Enabled = open;
            previewPanel.Invalidate();
        }

        private void RefreshTexts(BtplTemplate template)
        {
            int focused = gridViewTexts.FocusedRowHandle;
            gridTexts.DataSource = template == null ? new List<TextRow>()
                : template.TextElements.Select((text, index) => new TextRow(index, text)).ToList();
            if (focused >= 0 && gridViewTexts.RowCount > 0) gridViewTexts.FocusedRowHandle = Math.Min(focused, gridViewTexts.RowCount - 1);
        }

        private void RefreshRecentMenu()
        {
            recentMenu.Items.Clear();
            foreach (string file in settings.RecentFiles)
            {
                recentMenu.Items.Add(new DXMenuItem(file, recentItem_Click) { Tag = file });
            }
            if (settings.RecentFiles.Count == 0) recentMenu.Items.Add(new DXMenuItem("최근 파일 없음") { Enabled = false });
        }

        private static string Parameters(BtplTemplate template, string command) =>
            template?.Settings.FirstOrDefault(setting => setting.Command == command)?.Parameters;

        internal bool ConfirmDiscard()
        {
            if (!doc.IsDirty) return true;
            DialogResult answer = XtraMessageBox.Show(this, "저장하지 않은 수정이 있습니다. 저장할까요?", Title,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.No) return true;
            if (answer != DialogResult.Yes) return false;
            try
            {
                doc.Save();
                ClearMessage();
                return true;
            }
            // 원본 충돌을 포함해 저장이 실패하면 편집을 잃지 않도록 전환을 멈춘다.
            catch (Exception error) when (IsDocumentError(error))
            {
                ShowException(error);
                return false;
            }
            finally
            {
                RefreshView();
            }
        }

        private void btnOpen_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = FileFilter })
            {
                if (settings.TemplateFolder.Length > 0) dialog.InitialDirectory = settings.TemplateFolder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                OpenTemplate(dialog.FileName);
            }
        }

        private void recentItem_Click(object sender, EventArgs e) => OpenTemplate((string)((DXMenuItem)sender).Tag);

        private void OpenTemplate(string path)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                doc.Open(path);
                ClearMessage();
            }
            catch (Exception error) when (IsDocumentError(error))
            {
                ShowException(error);
                RefreshView();
                return;
            }
            AddRecent(doc.Template.FilePath);
            RefreshView();
        }

        private void AddRecent(string fullPath)
        {
            try
            {
                settings.AddRecent(fullPath);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                ShowError("최근 파일 목록을 저장하지 못했습니다. " + error.Message);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                doc.Save();
                ClearMessage();
            }
            catch (BtplSaveConflictException conflict)
            {
                ResolveConflict(conflict);
            }
            catch (Exception error) when (IsDocumentError(error))
            {
                ShowException(error);
            }
            RefreshView();
        }

        private void ResolveConflict(BtplSaveConflictException conflict)
        {
            if (File.Exists(doc.Template.FilePath))
            {
                DialogResult answer = XtraMessageBox.Show(this, "원본이 다른 곳에서 바뀌었습니다. 예=편집 버리고 다시 읽기, 아니요=다른 이름 저장, 취소=편집 유지",
                    Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes) Reload();
                else if (answer == DialogResult.No) SaveAs();
                else ShowException(conflict);
                return;
            }
            if (XtraMessageBox.Show(this, "원본이 없습니다. 예=다른 이름 저장, 아니요=편집 유지", Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) SaveAs();
            else ShowException(conflict);
        }

        private void Reload()
        {
            try
            {
                doc.Reload();
                ClearMessage();
            }
            catch (Exception error) when (IsDocumentError(error))
            {
                ShowException(error);
            }
        }

        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            SaveAs();
            RefreshView();
        }

        private void SaveAs()
        {
            string current = doc.Template.FilePath;
            using (var dialog = new SaveFileDialog { Filter = FileFilter, OverwritePrompt = false })
            {
                if (current != null)
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(current);
                    dialog.FileName = Path.GetFileName(current);
                }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = Path.GetFullPath(dialog.FileName);
                if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotice("현재 파일입니다. 저장을 사용하세요.");
                    return;
                }
                bool overwrite = File.Exists(path);
                if (overwrite && XtraMessageBox.Show(this, "이미 있는 파일입니다. 덮어쓸까요?" + Environment.NewLine + path, Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    doc.SaveAs(path, overwrite);
                    ClearMessage();
                }
                catch (Exception error) when (IsDocumentError(error))
                {
                    ShowException(error);
                    return;
                }
                AddRecent(doc.Template.FilePath);
            }
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using (var form = new FrmSettings(settings)) form.ShowDialog(this);
            if (startWarning != null && settings.LoadWarning == null && lblMessage.Text == startWarning) ClearMessage();
            RefreshView();
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
        }

        // 구조 오류는 입력 칸이 아니라 서식 전체 문제이므로 공통 오류 줄에 표시한다.
        private bool ApplyEdit(Func<BtplTemplate, BtplTemplate> edit)
        {
            string labelBefore = Parameters(doc.Template, "^Q");
            try
            {
                doc.Apply(edit);
            }
            catch (BtplFormatException error)
            {
                ShowException(error);
                return false;
            }
            if (Parameters(doc.Template, "^Q") != labelBefore) ShowNotice(AlignNotice);
            else ClearMessage();
            return true;
        }

        private void gridViewTexts_ValidatingEditor(object sender, BaseContainerValidateEditorEventArgs e)
        {
            var view = (GridView)sender;
            if (doc.Template == null || !(view.GetFocusedRow() is TextRow row)) return;
            BtplTextElement current = doc.Template.TextElements[row.Index];
            string field = view.FocusedColumn.FieldName;
            string text = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "";
            int number = 0;
            if (field != nameof(TextRow.Payload)
                && !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
            {
                e.Valid = false;
                e.ErrorText = "InvalidArguments";
                return;
            }
            try
            {
                BtplTextElement next = Edit(current, field, number, text);
                doc.Apply(t => t.WithText(row.Index, next));
            }
            catch (BtplFormatException error)
            {
                e.Valid = false;
                e.ErrorText = error.Code;
                return;
            }
            ClearMessage();
            // 검증 중에 다시 바인딩하면 편집기가 닫히는 중인 행을 건드리므로 끝난 뒤 갱신한다.
            BeginInvoke(new Action(RefreshView));
        }

        private static BtplTextElement Edit(BtplTextElement current, string field, int number, string text)
        {
            switch (field)
            {
                case nameof(TextRow.X): return current.WithPosition(number, current.Y);
                case nameof(TextRow.Y): return current.WithPosition(current.X, number);
                case nameof(TextRow.Size): return current.WithSize(number);
                case nameof(TextRow.Weight): return current.WithWeight(number);
                default: return current.WithPayload(text);
            }
        }

        private void btnAddText_Click(object sender, EventArgs e)
        {
            var texts = doc.Template.TextElements;
            int y = texts.Count == 0 ? 80 : texts[texts.Count - 1].Y + 40;
            if (!ApplyEdit(t => t.AddText(new BtplTextElement(80, y, 10, 1, "TEXT")))) return;
            RefreshView();
            gridViewTexts.FocusedRowHandle = gridViewTexts.RowCount - 1;
        }

        private void btnRemoveText_Click(object sender, EventArgs e)
        {
            if (!(gridViewTexts.GetFocusedRow() is TextRow row)) return;
            ApplyEdit(t => t.RemoveText(row.Index));
            RefreshView();
        }

        private void gridSettings_ValidatingEditor(object sender, BaseContainerValidateEditorEventArgs e)
        {
            if (doc.Template == null) return;
            string command = Command(gridSettings.FocusedRow);
            if (command == null) return;
            string value = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "";
            string current = Parameters(doc.Template, command);
            if (current == null && value.Length == 0) return;
            BtplSetting setting;
            try
            {
                setting = new BtplSetting(command, value);
            }
            catch (BtplFormatException error)
            {
                e.Valid = false;
                e.ErrorText = error.Code;
                return;
            }
            // 칸 값은 맞지만 서식 구조 때문에 못 넣으면 칸을 문서 값으로 되돌린다.
            if (!ApplyEdit(t => t.WithSetting(setting))) e.Value = current ?? "";
            BeginInvoke(new Action(RefreshView));
        }

        private string Command(BaseRow row)
        {
            if (row == rowQ) return "^Q";
            if (row == rowW) return "^W";
            if (row == rowS) return "^S";
            if (row == rowH) return "^H";
            return null;
        }

        private void btnApplyBlackMark_Click(object sender, EventArgs e)
        {
            BtplBlackMarkMedia media = CreateMedia(out string error);
            if (media == null)
            {
                ShowError("블랙마크 계산 입력 오류: " + error);
                return;
            }
            // ^Q와 ^W 중 하나만 바뀐 서식이 남지 않게 한 번의 편집으로 반영한다.
            ApplyEdit(t => t.WithSetting(media.LabelSetting).WithSetting(media.WidthSetting));
            RefreshView();
        }

        private void gridBlackMark_CellValueChanged(object sender, DevExpress.XtraVerticalGrid.Events.CellValueChangedEventArgs e) =>
            previewPanel.Invalidate();

        private BtplBlackMarkMedia CreateMedia(out string error)
        {
            error = "InvalidArguments";
            EditorRow[] rows = { rowLinerWidth, rowSegment, rowMarkWidth, rowMarkToLabel, rowLabelWidth, rowLabelHeight, rowSensor, rowVertical, rowHorizontal };
            var mm = new decimal[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                if (!decimal.TryParse(Convert.ToString(rows[i].Properties.Value, CultureInfo.InvariantCulture), MmStyle, CultureInfo.InvariantCulture, out mm[i])) return null;
            }
            if (!int.TryParse(Convert.ToString(rowDpi.Properties.Value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int dpi)) return null;
            try
            {
                var media = new BtplBlackMarkMedia(mm[0], mm[1], mm[2], mm[3], mm[4], mm[5], dpi, mm[6], mm[7], mm[8]);
                error = null;
                return media;
            }
            catch (BtplFormatException format)
            {
                error = format.Code;
                return null;
            }
        }

        private int PreviewDpi() =>
            int.TryParse(Convert.ToString(rowDpi.Properties.Value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int dpi)
            && dpi == 203 ? 203 : 300;

        private void txtVariable_EditValueChanged(object sender, EventArgs e) => RefreshView();

        private void previewPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle client = previewPanel.ClientRectangle;
            BtplTemplate template = doc.Template;
            if (template == null)
            {
                TextRenderer.DrawText(g, "열린 서식 없음", Font, client, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            BtplBlackMarkMedia media = CreateMedia(out _);
            string variable = string.IsNullOrEmpty(txtVariable.Text) ? null : txtVariable.Text;
            int width = CanvasWidth(template, media);
            int height = Math.Max(media?.LabelArea.Bottom ?? 0,
                template.TextElements.Select(text => text.Y + DotHeight(text)).DefaultIfEmpty(0).Max()) + 40;
            float scale = Math.Min((client.Width - 8f) / width, (client.Height - 8f) / height);
            if (scale <= 0) return;
            float left = client.Left + (client.Width - width * scale) / 2;
            float top = client.Top + (client.Height - height * scale) / 2;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.White, left, top, width * scale, height * scale);
            g.DrawRectangle(Pens.Gray, left, top, width * scale, height * scale);
            if (media != null) DrawAreas(g, media, left, top, scale);
            foreach (BtplTextElement text in template.TextElements)
            {
                float pixels = Math.Max(1f, DotHeight(text) * scale);
                using (var font = new Font("굴림", pixels, text.Weight >= 2 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.DrawString(TemplateDocument.PreviewText(text.Payload, variable), font, Brushes.Black, left + text.X * scale, top + text.Y * scale);
                }
            }
        }

        // SDK가 LOGFONT 높이를 size*340/72 dot로 계산한다.
        private static int DotHeight(BtplTextElement text) => text.Size * 340 / 72;

        private int CanvasWidth(BtplTemplate template, BtplBlackMarkMedia media)
        {
            if (decimal.TryParse(Parameters(template, "^W"), MmStyle, CultureInfo.InvariantCulture, out decimal mm) && mm > 0)
                return Math.Max(1, (int)(mm * PreviewDpi() / 25.4m));
            int right = Math.Max(media?.LabelArea.Right ?? 0, template.TextElements.Select(text => text.X).DefaultIfEmpty(0).Max());
            return right + 40;
        }

        private static void DrawAreas(Graphics g, BtplBlackMarkMedia media, float left, float top, float scale)
        {
            RectangleF label = Scale(media.LabelArea, left, top, scale);
            using (var solid = new Pen(Color.SteelBlue, 1.5f))
            using (var dashed = new Pen(Color.SeaGreen) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(solid, label.X, label.Y, label.Width, label.Height);
                RectangleF safe = Scale(media.SafeArea, left, top, scale);
                g.DrawRectangle(dashed, safe.X, safe.Y, safe.Width, safe.Height);
            }
            using (var font = new Font("굴림", 8f))
            {
                g.DrawString("계산 입력 기준 (실선 라벨, 점선 안전 영역)", font, Brushes.SteelBlue, left + 2, top + 2);
            }
        }

        private static RectangleF Scale(BtplArea area, float left, float top, float scale) =>
            new RectangleF(left + area.Left * scale, top + area.Top * scale, (area.Right - area.Left) * scale, (area.Bottom - area.Top) * scale);

        // 그리드 행은 문서의 AW 순서(index)를 기억하고 편집 결과는 문서에서만 읽는다.
        private sealed class TextRow
        {
            internal TextRow(int index, BtplTextElement text)
            {
                Index = index;
                Line = text.TemplateLine;
                X = text.X;
                Y = text.Y;
                Size = text.Size;
                Weight = text.Weight;
                Payload = text.Payload;
            }

            internal int Index { get; }
            public int? Line { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Size { get; set; }
            public int Weight { get; set; }
            public string Payload { get; set; }
        }
    }
}
