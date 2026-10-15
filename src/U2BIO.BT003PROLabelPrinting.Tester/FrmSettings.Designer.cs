namespace U2BIO.BT003PROLabelPrinting.Tester
{
    partial class FrmSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.layoutControl1 = new DevExpress.XtraLayout.LayoutControl();
            this.lblWarning = new DevExpress.XtraEditors.LabelControl();
            this.tabSettings = new DevExpress.XtraTab.XtraTabControl();
            this.pageConnection = new DevExpress.XtraTab.XtraTabPage();
            this.gridConnection = new DevExpress.XtraVerticalGrid.VGridControl();
            this.repositoryPort = new DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox();
            this.repositoryDistance = new DevExpress.XtraEditors.Repository.RepositoryItemSpinEdit();
            this.categoryConnection = new DevExpress.XtraVerticalGrid.Rows.CategoryRow();
            this.rowPort = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.rowDetected = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.rowDistance = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.pageTemplate = new DevExpress.XtraTab.XtraTabPage();
            this.gridTemplate = new DevExpress.XtraVerticalGrid.VGridControl();
            this.repositoryFolder = new DevExpress.XtraEditors.Repository.RepositoryItemButtonEdit();
            this.categoryTemplate = new DevExpress.XtraVerticalGrid.Rows.CategoryRow();
            this.rowFolder = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.pageCalibration = new DevExpress.XtraTab.XtraTabPage();
            this.gridCalibration = new DevExpress.XtraVerticalGrid.VGridControl();
            this.repositoryDpi = new DevExpress.XtraEditors.Repository.RepositoryItemComboBox();
            this.repositoryOffset = new DevExpress.XtraEditors.Repository.RepositoryItemTextEdit();
            this.categoryCalibration = new DevExpress.XtraVerticalGrid.Rows.CategoryRow();
            this.rowDpi = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.rowSensor = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.rowVertical = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.rowHorizontal = new DevExpress.XtraVerticalGrid.Rows.EditorRow();
            this.btnOK = new DevExpress.XtraEditors.SimpleButton();
            this.btnCancel = new DevExpress.XtraEditors.SimpleButton();
            this.layoutControlGroup1 = new DevExpress.XtraLayout.LayoutControlGroup();
            this.layoutItemWarning = new DevExpress.XtraLayout.LayoutControlItem();
            this.layoutItemTabs = new DevExpress.XtraLayout.LayoutControlItem();
            this.emptySpaceItem1 = new DevExpress.XtraLayout.EmptySpaceItem();
            this.layoutItemOK = new DevExpress.XtraLayout.LayoutControlItem();
            this.layoutItemCancel = new DevExpress.XtraLayout.LayoutControlItem();
            ((System.ComponentModel.ISupportInitialize)(this.layoutControl1)).BeginInit();
            this.layoutControl1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tabSettings)).BeginInit();
            this.tabSettings.SuspendLayout();
            this.pageConnection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridConnection)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryDistance)).BeginInit();
            this.pageTemplate.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridTemplate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryFolder)).BeginInit();
            this.pageCalibration.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridCalibration)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryDpi)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryOffset)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutControlGroup1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemWarning)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemTabs)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.emptySpaceItem1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemOK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemCancel)).BeginInit();
            this.SuspendLayout();
            //
            // layoutControl1
            //
            this.layoutControl1.Controls.Add(this.lblWarning);
            this.layoutControl1.Controls.Add(this.tabSettings);
            this.layoutControl1.Controls.Add(this.btnOK);
            this.layoutControl1.Controls.Add(this.btnCancel);
            this.layoutControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutControl1.Location = new System.Drawing.Point(0, 0);
            this.layoutControl1.Name = "layoutControl1";
            this.layoutControl1.Root = this.layoutControlGroup1;
            this.layoutControl1.Size = new System.Drawing.Size(640, 360);
            this.layoutControl1.TabIndex = 0;
            this.layoutControl1.Text = "layoutControl1";
            //
            // lblWarning
            //
            this.lblWarning.Appearance.ForeColor = System.Drawing.Color.Red;
            this.lblWarning.Appearance.Options.UseForeColor = true;
            this.lblWarning.Appearance.Options.UseTextOptions = true;
            this.lblWarning.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.lblWarning.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.Vertical;
            this.lblWarning.Location = new System.Drawing.Point(4, 4);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(632, 14);
            this.lblWarning.StyleController = this.layoutControl1;
            this.lblWarning.TabIndex = 3;
            //
            // tabSettings
            //
            this.tabSettings.Location = new System.Drawing.Point(4, 22);
            this.tabSettings.Name = "tabSettings";
            this.tabSettings.SelectedTabPage = this.pageConnection;
            this.tabSettings.Size = new System.Drawing.Size(632, 303);
            this.tabSettings.TabIndex = 0;
            this.tabSettings.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.pageConnection,
            this.pageTemplate,
            this.pageCalibration});
            //
            // pageConnection
            //
            this.pageConnection.Controls.Add(this.gridConnection);
            this.pageConnection.Name = "pageConnection";
            this.pageConnection.Size = new System.Drawing.Size(630, 277);
            this.pageConnection.Text = "연결";
            //
            // gridConnection
            //
            this.gridConnection.Appearance.Category.Font = new System.Drawing.Font("굴림", 9F, System.Drawing.FontStyle.Bold);
            this.gridConnection.Appearance.Category.Options.UseFont = true;
            this.gridConnection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridConnection.Location = new System.Drawing.Point(0, 0);
            this.gridConnection.Name = "gridConnection";
            this.gridConnection.OptionsFind.Visibility = DevExpress.XtraVerticalGrid.FindPanelVisibility.Never;
            this.gridConnection.OptionsView.MinRowAutoHeight = 20;
            this.gridConnection.RecordWidth = 200;
            this.gridConnection.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] {
            this.repositoryPort,
            this.repositoryDistance});
            this.gridConnection.RowHeaderWidth = 280;
            this.gridConnection.Rows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.categoryConnection});
            this.gridConnection.ShowButtonMode = DevExpress.XtraVerticalGrid.ShowButtonModeEnum.ShowAlways;
            this.gridConnection.Size = new System.Drawing.Size(630, 277);
            this.gridConnection.TabIndex = 0;
            this.gridConnection.ValidatingEditor += new DevExpress.XtraEditors.Controls.BaseContainerValidateEditorEventHandler(this.grid_ValidatingEditor);
            //
            // repositoryPort
            //
            this.repositoryPort.AutoHeight = false;
            this.repositoryPort.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo),
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Redo)});
            this.repositoryPort.Name = "repositoryPort";
            this.repositoryPort.ButtonClick += new DevExpress.XtraEditors.Controls.ButtonPressedEventHandler(this.repositoryPort_ButtonClick);
            //
            // repositoryDistance
            //
            this.repositoryDistance.AutoHeight = false;
            this.repositoryDistance.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.repositoryDistance.IsFloatValue = false;
            this.repositoryDistance.Name = "repositoryDistance";
            //
            // categoryConnection
            //
            this.categoryConnection.ChildRows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.rowPort,
            this.rowDetected,
            this.rowDistance});
            this.categoryConnection.Name = "categoryConnection";
            this.categoryConnection.Properties.Caption = "USB 연결";
            //
            // rowPort
            //
            this.rowPort.Name = "rowPort";
            this.rowPort.Properties.Caption = "포트";
            this.rowPort.Properties.RowEdit = this.repositoryPort;
            //
            // rowDetected
            //
            this.rowDetected.Name = "rowDetected";
            this.rowDetected.Properties.Caption = "감지 상태";
            this.rowDetected.Properties.ReadOnly = true;
            //
            // rowDistance
            //
            this.rowDistance.Name = "rowDistance";
            this.rowDistance.Properties.Caption = "배출 거리(mm, 0~1000)";
            this.rowDistance.Properties.RowEdit = this.repositoryDistance;
            //
            // pageTemplate
            //
            this.pageTemplate.Controls.Add(this.gridTemplate);
            this.pageTemplate.Name = "pageTemplate";
            this.pageTemplate.Size = new System.Drawing.Size(630, 277);
            this.pageTemplate.Text = "서식";
            //
            // gridTemplate
            //
            this.gridTemplate.Appearance.Category.Font = new System.Drawing.Font("굴림", 9F, System.Drawing.FontStyle.Bold);
            this.gridTemplate.Appearance.Category.Options.UseFont = true;
            this.gridTemplate.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridTemplate.Location = new System.Drawing.Point(0, 0);
            this.gridTemplate.Name = "gridTemplate";
            this.gridTemplate.OptionsFind.Visibility = DevExpress.XtraVerticalGrid.FindPanelVisibility.Never;
            this.gridTemplate.OptionsView.MinRowAutoHeight = 20;
            this.gridTemplate.RecordWidth = 200;
            this.gridTemplate.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] {
            this.repositoryFolder});
            this.gridTemplate.RowHeaderWidth = 280;
            this.gridTemplate.Rows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.categoryTemplate});
            this.gridTemplate.ShowButtonMode = DevExpress.XtraVerticalGrid.ShowButtonModeEnum.ShowAlways;
            this.gridTemplate.Size = new System.Drawing.Size(630, 277);
            this.gridTemplate.TabIndex = 0;
            this.gridTemplate.ValidatingEditor += new DevExpress.XtraEditors.Controls.BaseContainerValidateEditorEventHandler(this.grid_ValidatingEditor);
            //
            // repositoryFolder
            //
            this.repositoryFolder.AutoHeight = false;
            this.repositoryFolder.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton()});
            this.repositoryFolder.Name = "repositoryFolder";
            this.repositoryFolder.ButtonClick += new DevExpress.XtraEditors.Controls.ButtonPressedEventHandler(this.repositoryFolder_ButtonClick);
            //
            // categoryTemplate
            //
            this.categoryTemplate.ChildRows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.rowFolder});
            this.categoryTemplate.Name = "categoryTemplate";
            this.categoryTemplate.Properties.Caption = "서식";
            //
            // rowFolder
            //
            this.rowFolder.Name = "rowFolder";
            this.rowFolder.Properties.Caption = "서식 폴더";
            this.rowFolder.Properties.RowEdit = this.repositoryFolder;
            //
            // pageCalibration
            //
            this.pageCalibration.Controls.Add(this.gridCalibration);
            this.pageCalibration.Name = "pageCalibration";
            this.pageCalibration.Size = new System.Drawing.Size(630, 277);
            this.pageCalibration.Text = "프린터 보정";
            //
            // gridCalibration
            //
            this.gridCalibration.Appearance.Category.Font = new System.Drawing.Font("굴림", 9F, System.Drawing.FontStyle.Bold);
            this.gridCalibration.Appearance.Category.Options.UseFont = true;
            this.gridCalibration.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridCalibration.Location = new System.Drawing.Point(0, 0);
            this.gridCalibration.Name = "gridCalibration";
            this.gridCalibration.OptionsFind.Visibility = DevExpress.XtraVerticalGrid.FindPanelVisibility.Never;
            this.gridCalibration.OptionsView.MinRowAutoHeight = 20;
            this.gridCalibration.RecordWidth = 200;
            this.gridCalibration.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] {
            this.repositoryDpi,
            this.repositoryOffset});
            this.gridCalibration.RowHeaderWidth = 280;
            this.gridCalibration.Rows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.categoryCalibration});
            this.gridCalibration.ShowButtonMode = DevExpress.XtraVerticalGrid.ShowButtonModeEnum.ShowAlways;
            this.gridCalibration.Size = new System.Drawing.Size(630, 277);
            this.gridCalibration.TabIndex = 0;
            this.gridCalibration.ValidatingEditor += new DevExpress.XtraEditors.Controls.BaseContainerValidateEditorEventHandler(this.grid_ValidatingEditor);
            //
            // repositoryDpi
            //
            this.repositoryDpi.AutoHeight = false;
            this.repositoryDpi.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.repositoryDpi.Items.AddRange(new object[] {
            "203",
            "300"});
            this.repositoryDpi.Name = "repositoryDpi";
            this.repositoryDpi.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            //
            // repositoryOffset
            //
            this.repositoryOffset.AutoHeight = false;
            this.repositoryOffset.Name = "repositoryOffset";
            //
            // categoryCalibration
            //
            this.categoryCalibration.ChildRows.AddRange(new DevExpress.XtraVerticalGrid.Rows.BaseRow[] {
            this.rowDpi,
            this.rowSensor,
            this.rowVertical,
            this.rowHorizontal});
            this.categoryCalibration.Name = "categoryCalibration";
            this.categoryCalibration.Properties.Caption = "프린터 보정";
            //
            // rowDpi
            //
            this.rowDpi.Name = "rowDpi";
            this.rowDpi.Properties.Caption = "dpi (203/300)";
            this.rowDpi.Properties.ToolTip = "프린터 헤드 해상도입니다. 미리보기 영역의 mm를 dot로 바꿀 때 씁니다.";
            this.rowDpi.Properties.RowEdit = this.repositoryDpi;
            //
            // rowSensor
            //
            this.rowSensor.Name = "rowSensor";
            this.rowSensor.Properties.Caption = "센서 보정(mm, -1000~1000)";
            this.rowSensor.Properties.ToolTip = "자로 잰 마크~라벨 간격과 프린터가 쓰는 위치 값(^Q 셋째 인수)의 차이입니다.";
            this.rowSensor.Properties.RowEdit = this.repositoryOffset;
            //
            // rowVertical
            //
            this.rowVertical.Name = "rowVertical";
            this.rowVertical.Properties.Caption = "세로 보정(mm, 0~1000)";
            this.rowVertical.Properties.ToolTip = "서식 원점(Y0)부터 흰 라벨 위쪽 끝까지의 거리입니다.";
            this.rowVertical.Properties.RowEdit = this.repositoryOffset;
            //
            // rowHorizontal
            //
            this.rowHorizontal.Name = "rowHorizontal";
            this.rowHorizontal.Properties.Caption = "가로 보정(mm, -1000~1000)";
            this.rowHorizontal.Properties.ToolTip = "가운데 정렬 위치에서 흰 라벨 왼쪽 끝까지의 거리입니다. +는 오른쪽, −는 왼쪽입니다.";
            this.rowHorizontal.Properties.RowEdit = this.repositoryOffset;
            //
            // btnOK
            //
            this.btnOK.Location = new System.Drawing.Point(436, 333);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(96, 23);
            this.btnOK.StyleController = this.layoutControl1;
            this.btnOK.TabIndex = 1;
            this.btnOK.Text = "확인(F12)";
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.CausesValidation = false;
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(536, 333);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(96, 23);
            this.btnCancel.StyleController = this.layoutControl1;
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "취소(ESC)";
            //
            // layoutControlGroup1
            //
            this.layoutControlGroup1.EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.True;
            this.layoutControlGroup1.GroupBordersVisible = false;
            this.layoutControlGroup1.Items.AddRange(new DevExpress.XtraLayout.BaseLayoutItem[] {
            this.layoutItemWarning,
            this.layoutItemTabs,
            this.emptySpaceItem1,
            this.layoutItemOK,
            this.layoutItemCancel});
            this.layoutControlGroup1.Name = "Root";
            this.layoutControlGroup1.Padding = new DevExpress.XtraLayout.Utils.Padding(2, 2, 2, 2);
            this.layoutControlGroup1.Size = new System.Drawing.Size(640, 360);
            this.layoutControlGroup1.TextVisible = false;
            //
            // layoutItemWarning
            //
            this.layoutItemWarning.Control = this.lblWarning;
            this.layoutItemWarning.Location = new System.Drawing.Point(0, 0);
            this.layoutItemWarning.Name = "layoutItemWarning";
            this.layoutItemWarning.Size = new System.Drawing.Size(636, 18);
            this.layoutItemWarning.TextSize = new System.Drawing.Size(0, 0);
            this.layoutItemWarning.TextVisible = false;
            //
            // layoutItemTabs
            //
            this.layoutItemTabs.Control = this.tabSettings;
            this.layoutItemTabs.Location = new System.Drawing.Point(0, 18);
            this.layoutItemTabs.Name = "layoutItemTabs";
            this.layoutItemTabs.Size = new System.Drawing.Size(636, 311);
            this.layoutItemTabs.TextSize = new System.Drawing.Size(0, 0);
            this.layoutItemTabs.TextVisible = false;
            //
            // emptySpaceItem1
            //
            this.emptySpaceItem1.AllowHotTrack = false;
            this.emptySpaceItem1.Location = new System.Drawing.Point(0, 329);
            this.emptySpaceItem1.Name = "emptySpaceItem1";
            this.emptySpaceItem1.Size = new System.Drawing.Size(436, 27);
            this.emptySpaceItem1.TextSize = new System.Drawing.Size(0, 0);
            //
            // layoutItemOK
            //
            this.layoutItemOK.Control = this.btnOK;
            this.layoutItemOK.Location = new System.Drawing.Point(436, 329);
            this.layoutItemOK.MaxSize = new System.Drawing.Size(100, 27);
            this.layoutItemOK.MinSize = new System.Drawing.Size(100, 27);
            this.layoutItemOK.Name = "layoutItemOK";
            this.layoutItemOK.Size = new System.Drawing.Size(100, 27);
            this.layoutItemOK.SizeConstraintsType = DevExpress.XtraLayout.SizeConstraintsType.Custom;
            this.layoutItemOK.TextSize = new System.Drawing.Size(0, 0);
            this.layoutItemOK.TextVisible = false;
            //
            // layoutItemCancel
            //
            this.layoutItemCancel.Control = this.btnCancel;
            this.layoutItemCancel.Location = new System.Drawing.Point(536, 329);
            this.layoutItemCancel.MaxSize = new System.Drawing.Size(100, 27);
            this.layoutItemCancel.MinSize = new System.Drawing.Size(100, 27);
            this.layoutItemCancel.Name = "layoutItemCancel";
            this.layoutItemCancel.Size = new System.Drawing.Size(100, 27);
            this.layoutItemCancel.SizeConstraintsType = DevExpress.XtraLayout.SizeConstraintsType.Custom;
            this.layoutItemCancel.TextSize = new System.Drawing.Size(0, 0);
            this.layoutItemCancel.TextVisible = false;
            //
            // FrmSettings
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(640, 360);
            this.Controls.Add(this.layoutControl1);
            this.IconOptions.ShowIcon = false;
            this.KeyPreview = true;
            this.MinimizeBox = false;
            this.Name = "FrmSettings";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "환경설정";
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FrmSettings_KeyDown);
            ((System.ComponentModel.ISupportInitialize)(this.layoutControl1)).EndInit();
            this.layoutControl1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.tabSettings)).EndInit();
            this.tabSettings.ResumeLayout(false);
            this.pageConnection.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridConnection)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryDistance)).EndInit();
            this.pageTemplate.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridTemplate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryFolder)).EndInit();
            this.pageCalibration.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridCalibration)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryDpi)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repositoryOffset)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutControlGroup1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemWarning)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemTabs)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.emptySpaceItem1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemOK)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.layoutItemCancel)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraLayout.LayoutControl layoutControl1;
        private DevExpress.XtraLayout.LayoutControlGroup layoutControlGroup1;
        private DevExpress.XtraEditors.LabelControl lblWarning;
        private DevExpress.XtraLayout.LayoutControlItem layoutItemWarning;
        private DevExpress.XtraTab.XtraTabControl tabSettings;
        private DevExpress.XtraTab.XtraTabPage pageConnection;
        private DevExpress.XtraVerticalGrid.VGridControl gridConnection;
        private DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox repositoryPort;
        private DevExpress.XtraEditors.Repository.RepositoryItemSpinEdit repositoryDistance;
        private DevExpress.XtraVerticalGrid.Rows.CategoryRow categoryConnection;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowPort;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowDetected;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowDistance;
        private DevExpress.XtraTab.XtraTabPage pageTemplate;
        private DevExpress.XtraVerticalGrid.VGridControl gridTemplate;
        private DevExpress.XtraEditors.Repository.RepositoryItemButtonEdit repositoryFolder;
        private DevExpress.XtraVerticalGrid.Rows.CategoryRow categoryTemplate;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowFolder;
        private DevExpress.XtraTab.XtraTabPage pageCalibration;
        private DevExpress.XtraVerticalGrid.VGridControl gridCalibration;
        private DevExpress.XtraEditors.Repository.RepositoryItemComboBox repositoryDpi;
        private DevExpress.XtraEditors.Repository.RepositoryItemTextEdit repositoryOffset;
        private DevExpress.XtraVerticalGrid.Rows.CategoryRow categoryCalibration;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowDpi;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowSensor;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowVertical;
        private DevExpress.XtraVerticalGrid.Rows.EditorRow rowHorizontal;
        private DevExpress.XtraEditors.SimpleButton btnOK;
        private DevExpress.XtraEditors.SimpleButton btnCancel;
        private DevExpress.XtraLayout.LayoutControlItem layoutItemTabs;
        private DevExpress.XtraLayout.EmptySpaceItem emptySpaceItem1;
        private DevExpress.XtraLayout.LayoutControlItem layoutItemOK;
        private DevExpress.XtraLayout.LayoutControlItem layoutItemCancel;
    }
}
