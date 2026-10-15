using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils.Menu;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Grid;
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
        private const int MaxDots = 100000;
        // 블랙마크 탭 칸과 운영 기준값이다. 앞의 네 칸은 ^W·^Q에서 채우고 라벨 너비·길이는 미리보기에만 쓴다.
        private static readonly string[] MediaDefaults = { "70", "35", "5", "7.5", "60", "20" };
        private const decimal MaxWidthMm = 10000;
        private const string GapBlocked = "GapLabelSetting";
        private const string NoResponse = "미응답";
        private const string ActionConnect = "연결";
        private const string ActionStatus = "상태";
        private const string ActionDisconnect = "해제";
        private const string ActionConfirm = "위치 확인";
        private const string ActionFeed = "FEED+확인";

        private readonly TesterSettings settings;
        private readonly TemplateDocument doc = new TemplateDocument();
        private readonly DXPopupMenu recentMenu = new DXPopupMenu();
        private readonly string startWarning;
        // 생성만으로는 SDK를 읽지 않는다. 연결·인쇄를 호출할 때 처음 로드된다.
        private readonly Bt003Printer printer = new Bt003Printer();
        private readonly BindingList<ResultRow> results = new BindingList<ResultRow>();
        private string[] recentShown;
        private bool previewFailed;
        // 블랙마크 칸을 프로그램이 채우는 동안에는 사용자 편집으로 보지 않는다.
        private bool syncing;
        private bool labelLinked;
        private bool widthLinked;
        private bool syncFailed;
        private string labelProblem;
        private string widthProblem;
        private string fontFace;
        private bool fontConfigured;
        private Dictionary<string, TextRaster> rasters = new Dictionary<string, TextRaster>();
        private bool busy;
        private bool closeRequested;
        private bool connectAttempted;
        private string connectionState = "미연결";
        private string connectionTarget = "";
        private string pendingTarget = "";
        private string positionState = "미확인";

        internal FrmMain()
        {
            InitializeComponent();
            settings = TesterSettings.Load(TesterSettings.DefaultFilePath);
            btnOpen.DropDownControl = recentMenu;
            gridResults.DataSource = results;
            startWarning = settings.LoadWarning;
            fontFace = SdkTextRenderer.FontFace(out fontConfigured);
            SyncMediaRows(true);
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
            RefreshMedia();
            RefreshRecentMenu();
            UpdateEnabled();
            lblStatus.Text = "연결: " + connectionState + connectionTarget + " · 위치: " + positionState;
        }

        private EditorRow[] MediaRows => new[] { rowLinerWidth, rowSegment, rowMarkWidth, rowMarkToLabel, rowLabelWidth, rowLabelHeight };

        // 서식의 ^W·^Q를 블랙마크 칸에 옮기고 칸 값이 서식을 그대로 다시 만드는지 DLL로 확인한다. 문서는 바꾸지 않는다.
        // reset은 성공한 열기·다시 읽기·닫기에서만 쓴다. 저장·다른 이름 저장은 라벨 치수 같은 세션 입력을 유지한다.
        private void SyncMediaRows(bool reset)
        {
            syncing = true;
            try
            {
                EditorRow[] rows = MediaRows;
                if (reset) for (int i = 0; i < rows.Length; i++) rows[i].Properties.Value = MediaDefaults[i];
                BtplTemplate template = doc.Template;
                labelLinked = widthLinked = syncFailed = false;
                labelProblem = widthProblem = null;
                if (template != null)
                {
                    if (TemplateDocument.TryReadWidth(Parameters(template, "^W"), out decimal liner)) rowLinerWidth.Properties.Value = Mm(liner);
                    decimal sensor = settings.Calibration.SensorOffsetMm;
                    if (TemplateDocument.TryReadLabel(Parameters(template, "^Q"), out decimal segment, out decimal mark, out decimal z) && z - sensor >= 0)
                    {
                        rowSegment.Properties.Value = Mm(segment);
                        rowMarkWidth.Properties.Value = Mm(mark);
                        rowMarkToLabel.Properties.Value = Mm(z - sensor);
                    }
                    BtplBlackMarkMedia media = CreateMedia(out string error);
                    if (media == null)
                    {
                        syncFailed = true;
                        labelProblem = widthProblem = "계산 입력 오류(" + error + ")";
                    }
                    else
                    {
                        labelLinked = Matches(template, media.LabelSetting, out labelProblem);
                        widthLinked = Matches(template, media.WidthSetting, out widthProblem);
                    }
                }
                bool open = template != null;
                rowSegment.Properties.ReadOnly = rowMarkWidth.Properties.ReadOnly = rowMarkToLabel.Properties.ReadOnly = open && !labelLinked;
                rowLinerWidth.Properties.ReadOnly = open && !widthLinked;
            }
            finally
            {
                syncing = false;
            }
        }

        private static bool Matches(BtplTemplate template, BtplSetting setting, out string problem)
        {
            try
            {
                problem = TemplateDocument.SameSetting(template, setting) ? null
                    : Parameters(template, setting.Command) == null ? "서식에 없음" : "계산기로 그대로 만들 수 없음";
            }
            catch (BtplFormatException format)
            {
                problem = format.Code;
            }
            return problem == null;
        }

        private static string Mm(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // 블랙마크 상태, 문구 그림, 미리보기 아래 경고 줄을 함께 갱신한다. 문서는 바꾸지 않는다.
        private void RefreshMedia()
        {
            BtplTemplate template = doc.Template;
            BtplBlackMarkMedia media = CreateMedia(out string error);
            string variable = VariableData();
            RefreshRasters(template, variable);
            syncing = true;
            try
            {
                rowResult.Properties.Value = template == null ? ""
                    : (Parameters(template, "^Q") is string q ? "^Q" + q : "^Q 없음") + " / " + (Parameters(template, "^W") is string w ? "^W" + w : "^W 없음");
                string state;
                if (template == null) state = "열린 서식 없음";
                else if (media == null) state = "계산 입력 오류(" + error + "): 서식은 바뀌지 않았습니다";
                else state = LinkState("^Q", labelLinked, labelProblem, template, media.LabelSetting) + " · "
                    + LinkState("^W", widthLinked, widthProblem, template, media.WidthSetting);
                rowMediaState.Properties.Value = state;
                rowMediaState.Properties.ToolTip = state + Environment.NewLine + "^Q·^W가 같아도 라벨 크기·dpi·보정까지 같다는 뜻은 아닙니다.";
            }
            finally
            {
                syncing = false;
            }

            // 줄을 숨기거나 글자 길이로 너비를 바꾸면 왼쪽 열 배치가 흔들리므로 항상 같은 자리에 두고 긴 내용은 툴팁으로 본다.
            bool unsupported = template != null && template.UnsupportedLineNumbers.Count > 0;
            var cut = new List<string>();
            var unsafeLines = new List<string>();
            var skipped = new List<string>();
            var failed = new List<string>();
            var notes = new List<string>();
            if (template != null)
            {
                notes.Add("미지원 줄: " + (unsupported ? string.Join(", ", template.UnsupportedLineNumbers) : "없음"));
                if (media == null) notes.Add("계산 입력 오류로 영역 판정 안 함");
                else
                {
                    TemplateDocument.CheckInk(template, media.LabelArea, media.SafeArea, text => RasterOf(text, variable), cut, unsafeLines, skipped, failed);
                    if (cut.Count > 0) notes.Add("라벨 밖(잘림): " + string.Join(", ", cut));
                    if (unsafeLines.Count > 0) notes.Add("안전 영역 밖: " + string.Join(", ", unsafeLines));
                    if (failed.Count > 0) notes.Add("판정 불가: " + string.Join(", ", failed));
                    if (skipped.Count > 0) notes.Add("판정 제외(정렬·회전): " + string.Join(", ", skipped));
                }
            }
            lblUnsupported.Text = string.Join(" · ", notes);
            lblUnsupported.ToolTip = lblUnsupported.Text;
            lblUnsupported.Appearance.ForeColor = unsupported || cut.Count > 0 || failed.Count > 0 ? Color.Red
                : unsafeLines.Count > 0 ? Color.DarkOrange : Color.Gray;
            previewPanel.Invalidate();
        }

        private static string LinkState(string command, bool linked, string problem, BtplTemplate template, BtplSetting computed)
        {
            if (!linked) return command + " " + problem + ": 용지·인쇄 탭에서 고치세요";
            try
            {
                return command + (TemplateDocument.SameSetting(template, computed) ? " 서식과 같음" : " 반영 안 됨");
            }
            catch (BtplFormatException format)
            {
                return command + " " + format.Code;
            }
        }

        // 지금 서식에 필요한 문구 그림만 남긴다. 다시 쓰는 그림과 새 그림, 아직 버리지 않은 그림을 합쳐 예산 안에서만 새로 만든다.
        private void RefreshRasters(BtplTemplate template, string variable)
        {
            var next = new Dictionary<string, TextRaster>();
            long used = rasters.Values.Sum(raster => raster.Bytes);
            if (template != null)
            {
                foreach (BtplTextElement text in template.TextElements)
                {
                    string shown = TemplateDocument.PreviewText(text.Payload, variable);
                    string key = RasterKey(text, shown);
                    if (next.ContainsKey(key)) continue;
                    // 실패한 그림은 예산이 풀렸을 수 있으므로 다시 만든다.
                    if (rasters.TryGetValue(key, out TextRaster old) && old.Failure == null)
                    {
                        next[key] = old;
                        continue;
                    }
                    TextRaster made;
                    try
                    {
                        made = SdkTextRenderer.Render(fontFace, text.Size, text.Weight, shown, used);
                    }
                    catch (Exception failure) when (failure is ArgumentException || failure is ExternalException || failure is OutOfMemoryException)
                    {
                        made = new TextRaster(null, null, 0, "그리기 실패: " + failure.Message);
                    }
                    used += made.Bytes;
                    next[key] = made;
                }
            }
            foreach (KeyValuePair<string, TextRaster> pair in rasters)
            {
                if (!next.TryGetValue(pair.Key, out TextRaster kept) || !ReferenceEquals(kept, pair.Value)) pair.Value.Dispose();
            }
            rasters = next;
        }

        private static string RasterKey(BtplTextElement text, string shown) =>
            text.Size.ToString(CultureInfo.InvariantCulture) + "|" + text.Weight.ToString(CultureInfo.InvariantCulture) + "|" + shown;

        private TextRaster RasterOf(BtplTextElement text, string variable) =>
            rasters.TryGetValue(RasterKey(text, TemplateDocument.PreviewText(text.Payload, variable)), out TextRaster raster) ? raster : null;

        private void DisposeRasters()
        {
            foreach (TextRaster raster in rasters.Values) raster.Dispose();
            rasters = new Dictionary<string, TextRaster>();
        }

        // 블랙마크·문구·용지 그리드에 남은 편집기를 확정한다. 값이 거부돼 편집기가 남으면 false다.
        private bool CommitEditors()
        {
            gridViewTexts.CloseEditor();
            gridSettings.CloseEditor();
            gridBlackMark.CloseEditor();
            return gridViewTexts.ActiveEditor == null && gridSettings.ActiveEditor == null && gridBlackMark.ActiveEditor == null;
        }

        // 활성 여부를 한곳에서 !busy와 함께 계산해 장치 호출 중 RefreshView가 버튼을 다시 켜지 않게 한다.
        private void UpdateEnabled()
        {
            bool idle = !busy;
            BtplTemplate template = doc.Template;
            bool open = idle && template != null;
            btnOpen.Enabled = idle;
            btnSave.Enabled = open;
            btnSaveAs.Enabled = open;
            btnClose.Enabled = open;
            btnSettings.Enabled = idle;
            gridTexts.Enabled = open;
            btnAddText.Enabled = open;
            btnRemoveText.Enabled = open && template.TextElements.Count > 0;
            gridSettings.Enabled = open;
            gridBlackMark.Enabled = idle;
            btnConnect.Enabled = idle;
            btnDisconnect.Enabled = idle;
            btnStatus.Enabled = idle;
            btnConfirmPosition.Enabled = idle;
            btnFeed.Enabled = idle;
            btnPrintEdit.Enabled = open;
            btnPrintFile.Enabled = open && template.FilePath != null;
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
            if (recentShown != null && recentShown.SequenceEqual(settings.RecentFiles)) return;
            recentShown = settings.RecentFiles.ToArray();
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
            // 셀 편집기에 남은 값을 먼저 문서에 반영해야 수정됨 여부가 맞다. 값이 거부돼 편집기가 남으면 멈춘다.
            if (!CommitEditors()) return false;
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

        private void recentItem_Click(object sender, EventArgs e)
        {
            string path = (string)((DXMenuItem)sender).Tag;
            // 메뉴가 닫히는 중에 대화 상자를 띄우거나 메뉴 항목을 다시 만들지 않도록 클릭 처리가 끝난 뒤 연다.
            BeginInvoke(new Action(() => OpenTemplate(path)));
        }

        private void OpenTemplate(string path)
        {
            if (busy || !ConfirmDiscard()) return;
            try
            {
                doc.Open(path);
                ClearMessage();
                SyncMediaRows(true);
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

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (busy || !ConfirmDiscard()) return;
            doc.Close();
            ClearMessage();
            SyncMediaRows(true);
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
            if (busy || doc.Template == null || !CommitEditors()) return;
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
                    Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
                if (answer == DialogResult.Yes) Reload();
                else if (answer == DialogResult.No) SaveAs();
                else ShowException(conflict);
                return;
            }
            if (XtraMessageBox.Show(this, "원본이 없습니다. 예=다른 이름 저장, 아니요=편집 유지", Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes) SaveAs();
            else ShowException(conflict);
        }

        private void Reload()
        {
            try
            {
                doc.Reload();
                ClearMessage();
                SyncMediaRows(true);
            }
            catch (Exception error) when (IsDocumentError(error))
            {
                ShowException(error);
            }
        }

        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            if (busy || doc.Template == null || !CommitEditors()) return;
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
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
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
            using (var form = new FrmSettings(settings))
            {
                // 설정은 열린 서식의 ^Q·^W를 바꾸지 않는다. 보정에 따라 칸 표시와 미리보기만 다시 계산한다.
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    fontFace = SdkTextRenderer.FontFace(out fontConfigured);
                    DisposeRasters();
                    SyncMediaRows(false);
                }
            }
            if (startWarning != null && settings.LoadWarning == null && lblMessage.Text == startWarning) ClearMessage();
            RefreshView();
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 장치 호출 중에는 닫지 않고, 호출이 끝나면 RunDevice가 닫기를 다시 시도한다.
            if (busy)
            {
                e.Cancel = true;
                closeRequested = true;
                return;
            }
            if (!ConfirmDiscard())
            {
                e.Cancel = true;
                closeRequested = false;
                return;
            }
            ReleasePrinter();
            DisposeRasters();
        }

        // 정리 실패는 알리기만 하고 종료는 막지 않는다. 연결한 적이 없으면 SDK를 읽지 않는다.
        private void ReleasePrinter()
        {
            try
            {
                if (connectAttempted)
                {
                    PrinterResult result = printer.Disconnect();
                    if (!result.IsSuccess) XtraMessageBox.Show(this, "연결 해제 실패: " + result.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                printer.Dispose();
            }
            catch (Exception error)
            {
                XtraMessageBox.Show(this, "프린터 정리 실패: " + error.GetType().Name + ": " + error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 모든 장치 호출은 여기서만 실행해 중복 호출을 막고 예외도 결과 행으로 남긴다. 자동 재시도는 하지 않는다.
        private async Task RunDevice(string action, Func<PrinterResult> call)
        {
            if (busy) return;
            busy = true;
            UpdateEnabled();
            try
            {
                PrinterResult result = await Task.Run(call);
                Record(new ResultRow(action, result.IsSuccess, result.NativeCode, result.PrintInvoked, result.PrintCode, result.Message));
            }
            catch (Exception error)
            {
                Record(new ResultRow(action, false, null, null, null, error.GetType().Name + ": " + error.Message));
                if (error is InvalidOperationException && error.Message.StartsWith(GapBlocked, StringComparison.Ordinal)) ShowError(error.Message);
            }
            finally
            {
                busy = false;
                RefreshView();
                if (closeRequested) BeginInvoke(new Action(Close));
            }
        }

        private void Record(ResultRow row)
        {
            results.Add(row);
            gridViewResults.FocusedRowHandle = gridViewResults.RowCount - 1;
            gridViewResults.MakeRowVisible(gridViewResults.FocusedRowHandle);
            string failure = row.Success ? null : "실패: " + row.Message;
            switch (row.Action)
            {
                case ActionConnect:
                case ActionStatus:
                    // 실패가 닫힘을 뜻하지는 않는다(이미 연결됨, 조회 예외 등). Message를 해석하지 않고 불명으로 둔다.
                    connectionState = failure == null ? "열림" : "불명·" + failure;
                    // 실제 연결에 쓴 값만 표시하도록 연결 성공 때만 포트·배출 거리를 바꾼다.
                    if (failure == null && row.Action == ActionConnect) connectionTarget = pendingTarget;
                    break;
                case ActionDisconnect:
                    connectionState = failure == null ? "닫힘" : "불명·" + failure;
                    break;
                case ActionConfirm:
                case ActionFeed:
                    positionState = row.Action + " " + (failure ?? "성공 (" + row.Time + ")");
                    break;
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (busy) return;
            string port = settings.Port;
            int distance = settings.PresentationDistanceMm;
            connectAttempted = true;
            pendingTarget = " · 포트 " + port + " · 배출 " + distance.ToString(CultureInfo.InvariantCulture) + " mm";
            await RunDevice(ActionConnect, () => printer.ConnectUsb(port, distance));
        }

        private async void btnStatus_Click(object sender, EventArgs e) => await RunDevice(ActionStatus, printer.GetConnectionStatus);

        private async void btnDisconnect_Click(object sender, EventArgs e) => await RunDevice(ActionDisconnect, printer.Disconnect);

        private async void btnConfirmPosition_Click(object sender, EventArgs e) => await RunDevice(ActionConfirm, printer.ConfirmPrintPosition);

        private async void btnFeed_Click(object sender, EventArgs e) => await RunDevice(ActionFeed, printer.FeedAndConfirmPosition);

        // 가변데이터는 입력 그대로 보내고, 빈 칸만 변수 없음(null)으로 본다.
        private string VariableData() => string.IsNullOrEmpty(txtVariable.Text) ? null : txtVariable.Text;

        private async void btnPrintEdit_Click(object sender, EventArgs e)
        {
            if (busy || doc.Template == null || !CommitEditors()) return;
            // 두 인수 ^Q를 보내면 프린터 라벨 유형이 간격 라벨로 바뀐다.
            if (TemplateDocument.HasGapLabelSetting(doc.Template))
            {
                ShowError(GapBlocked + ": 인수 두 개 ^Q가 있어 인쇄하지 않습니다. 프린터 라벨 유형이 간격 라벨로 바뀝니다.");
                return;
            }
            string btpl = doc.Template.ToBtpl();
            string data = VariableData();
            await RunDevice("편집 중 서식 인쇄", () => printer.Print(btpl, data));
        }

        private async void btnPrintFile_Click(object sender, EventArgs e)
        {
            if (busy || doc.Template?.FilePath == null || !CommitEditors()) return;
            string path = doc.Template.FilePath;
            if (doc.IsDirty && XtraMessageBox.Show(this, "저장하지 않은 수정은 이 인쇄에 반영되지 않습니다. 계속할까요?", Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string data = VariableData();
            // 편집본이 아니라 실제로 인쇄할 디스크 내용으로 두 인수 ^Q를 검사한다.
            await RunDevice("저장된 파일 인쇄", () =>
            {
                if (TemplateDocument.HasGapLabelSetting(BtplTemplate.Load(path)))
                    throw new InvalidOperationException(GapBlocked + ": 디스크 서식에 인수 두 개 ^Q가 있어 인쇄하지 않습니다.");
                return printer.PrintFile(path, data);
            });
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
            // ^Q·^W가 바뀌면 블랙마크 칸도 서식 값으로 다시 채운다(라벨 치수는 유지).
            BeginInvoke(new Action(() =>
            {
                if (command == "^Q" || command == "^W") SyncMediaRows(false);
                RefreshView();
            }));
        }

        private string Command(BaseRow row)
        {
            if (row == rowQ) return "^Q";
            if (row == rowW) return "^W";
            if (row == rowS) return "^S";
            if (row == rowH) return "^H";
            return null;
        }

        // 사용자가 칸을 확정하면 서식과 연결된 설정만 바로 반영한다. 연결 확인 때 칸이 서식을 그대로 만들었으므로
        // 바꾸지 않은 쪽 설정은 같은 객체가 돌아와 바뀌지 않는다(받침지 너비만 고치면 ^W만 바뀐다).
        private void gridBlackMark_CellValueChanged(object sender, DevExpress.XtraVerticalGrid.Events.CellValueChangedEventArgs e)
        {
            if (syncing || e.Row == rowResult || e.Row == rowMediaState) return;
            if (!busy && doc.Template != null)
            {
                // 직전 연결 확인이 계산 오류로 실패했으면 칸을 서식 값으로 다시 채워 연결부터 다시 확인한다.
                if (syncFailed) SyncMediaRows(false);
                BtplBlackMarkMedia media = CreateMedia(out _);
                if (media != null && (labelLinked || widthLinked))
                {
                    bool label = labelLinked;
                    bool width = widthLinked;
                    if (ApplyEdit(t =>
                    {
                        BtplTemplate next = t;
                        if (label) next = next.WithSetting(media.LabelSetting);
                        if (width) next = next.WithSetting(media.WidthSetting);
                        return next;
                    })) SyncMediaRows(false);
                }
            }
            RefreshView();
        }

        private BtplBlackMarkMedia CreateMedia(out string error)
        {
            error = "InvalidArguments";
            EditorRow[] rows = MediaRows;
            var mm = new decimal[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                if (!decimal.TryParse(Convert.ToString(rows[i].Properties.Value, CultureInfo.InvariantCulture), MmStyle, CultureInfo.InvariantCulture, out mm[i])) return null;
            }
            PrinterCalibration calibration = settings.Calibration;
            try
            {
                var media = new BtplBlackMarkMedia(mm[0], mm[1], mm[2], mm[3], mm[4], mm[5], calibration.Dpi,
                    calibration.SensorOffsetMm, calibration.VerticalOffsetMm, calibration.HorizontalOffsetMm);
                error = null;
                return media;
            }
            catch (BtplFormatException format)
            {
                error = format.Code;
                return null;
            }
        }

        private int PreviewDpi() => settings.Calibration.Dpi;

        private void txtVariable_EditValueChanged(object sender, EventArgs e) => RefreshView();

        private void previewPanel_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                DrawPreview(e.Graphics);
                previewFailed = false;
            }
            // 참고 미리보기 실패가 Paint마다 예외 창을 반복해 띄우지 않도록 오류 줄에 한 번만 알린다.
            catch (Exception error)
            {
                if (previewFailed) return;
                previewFailed = true;
                ShowError("미리보기를 그리지 못했습니다. " + error.GetType().Name + ": " + error.Message);
            }
        }

        private void DrawPreview(Graphics g)
        {
            Rectangle client = previewPanel.ClientRectangle;
            BtplTemplate template = doc.Template;
            if (template == null)
            {
                TextRenderer.DrawText(g, "열린 서식 없음", Font, client, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            BtplBlackMarkMedia media = CreateMedia(out _);
            string variable = VariableData();
            int width = CanvasWidth(template, media);
            int height = Clamp(Math.Max(media?.LabelArea.Bottom ?? 0,
                template.TextElements.Select(text => (long)text.Y + (RasterOf(text, variable)?.Image?.Height ?? CellDots(text))).DefaultIfEmpty(0).Max()) + 40);
            float scale = Math.Min((client.Width - 8f) / width, (client.Height - 8f) / height);
            if (scale <= 0) return;
            float left = client.Left + (client.Width - width * scale) / 2;
            float top = client.Top + (client.Height - height * scale) / 2;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.White, left, top, width * scale, height * scale);
            g.DrawRectangle(Pens.Gray, left, top, width * scale, height * scale);
            string legend = "계산 입력 기준 (실선 라벨, 점선 안전 영역) · 글꼴 " + fontFace + (fontConfigured ? "" : "(SDK 기본값)");
            if (media != null) DrawAreas(g, media, left, top, scale, legend);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            foreach (BtplTextElement text in template.TextElements)
            {
                TextRaster raster = RasterOf(text, variable);
                float x = left + text.X * scale;
                float y = top + text.Y * scale;
                if (raster?.Image == null)
                {
                    // 그리지 못한 줄은 SDK 셀 높이 상자로만 자리를 보인다. 빈 문구는 그리지 않는다.
                    if (raster?.Failure == null) continue;
                    using (var pen = new Pen(Color.Red) { DashStyle = DashStyle.Dot })
                        g.DrawRectangle(pen, x, y, Math.Max(1f, CellDots(text) * scale), Math.Max(1f, CellDots(text) * scale));
                    continue;
                }
                InkState state = media == null ? InkState.Inside : TemplateDocument.Classify(text, raster, media.LabelArea, media.SafeArea, out _);
                SdkTextRenderer.SetInkColor(raster.Image, state == InkState.Cut ? Color.Red : state == InkState.Unsafe ? Color.DarkOrange : Color.Black);
                g.DrawImage(raster.Image, x, y, raster.Image.Width * scale, raster.Image.Height * scale);
            }
        }

        // SDK가 LOGFONT 셀 높이를 size*340/72 dot로 계산한다. 그림을 만들지 못한 줄의 자리 표시와 캔버스 크기에만 쓴다.
        private static int CellDots(BtplTextElement text) => Clamp((long)text.Size * 340 / 72);

        // 극단적인 서식 값으로 int 계산이 넘치거나 그리기가 실패하지 않게 dot 값을 제한한다.
        private static int Clamp(long dots) => (int)Math.Max(1, Math.Min(MaxDots, dots));

        private int CanvasWidth(BtplTemplate template, BtplBlackMarkMedia media)
        {
            // 비현실적으로 큰 ^W는 없는 것으로 보고 문구 위치로 폭을 정한다.
            if (decimal.TryParse(Parameters(template, "^W"), MmStyle, CultureInfo.InvariantCulture, out decimal mm) && mm > 0 && mm <= MaxWidthMm)
                return Clamp((long)(mm * PreviewDpi() / 25.4m));
            long right = Math.Max(media?.LabelArea.Right ?? 0, template.TextElements.Select(text => (long)text.X).DefaultIfEmpty(0).Max());
            return Clamp(right + 40);
        }

        private static void DrawAreas(Graphics g, BtplBlackMarkMedia media, float left, float top, float scale, string legend)
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
                g.DrawString(legend, font, Brushes.SteelBlue, left + 2, top + 2);
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

        // 결과 표는 표시 문자열만 갖고, 응답이 없던 코드는 "미응답"으로 보여 준다.
        private sealed class ResultRow
        {
            internal ResultRow(string action, bool success, int? nativeCode, bool? printInvoked, int? printCode, string message)
            {
                Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Action = action;
                Success = success;
                NativeCode = nativeCode?.ToString(CultureInfo.InvariantCulture) ?? NoResponse;
                PrintInvoked = printInvoked.HasValue ? (printInvoked.Value ? "예" : "아니요") : NoResponse;
                PrintCode = printCode?.ToString(CultureInfo.InvariantCulture) ?? NoResponse;
                Message = message;
            }

            public string Time { get; }
            public string Action { get; }
            public bool Success { get; }
            public string NativeCode { get; }
            public string PrintInvoked { get; }
            public string PrintCode { get; }
            public string Message { get; }
        }
    }
}
