using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.Skins;
using DevExpress.UserSkins;
using DevExpress.XtraEditors;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal static class Program
    {
        private const string Title = "BT-003 Pro 서식 Tester";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-check") return SelfCheck.Run(args);
            return RunUi();
        }

        // 자가 검사 경로가 DevExpress 형식을 JIT 시점에 끌어오지 않도록 화면 시작을 분리한다.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunUi()
        {
            // 같은 물리 프린터와 설정 파일은 한 프로그램만 쓴다.
            using (var mutex = new Mutex(true, @"Local\U2BIO.BT003Tester", out bool created))
            {
                if (!created)
                {
                    XtraMessageBox.Show("Tester가 이미 실행 중입니다.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                Application.EnableVisualStyles();
                SkinManager.EnableFormSkins();
                BonusSkins.Register();
                Application.ThreadException += (sender, e) => ShowError(e.Exception);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => ShowError(e.ExceptionObject as Exception);
                UserLookAndFeel.Default.SetSkinStyle("VS2010");
                Application.SetCompatibleTextRenderingDefault(false);
                WindowsFormsSettings.LoadApplicationSettings();
                WindowsFormsSettings.DefaultFont = new Font("굴림", 9f);
                Application.Run(new FrmMain());
                return 0;
            }
        }

        private static void ShowError(Exception error)
        {
            string text = error == null ? "알 수 없는 오류가 발생했습니다." : error.GetType().Name + ": " + error.Message;
            XtraMessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
