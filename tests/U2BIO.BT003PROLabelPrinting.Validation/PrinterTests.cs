using System;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterTests
    {
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Printer lifecycle contract failed."); }
        internal static BtplPrintRequest Request() => BtplPrintRequest.Fixed(BtplTemplate.Parse(
            "^Q40,5,7.5+\n^W70\n^G0\n^S5\n^H19\n^P1\n^C1\n^R0\n^E15\n^L\nAW,80,120,10,1,0,0,SAMPLE\nE\n"));
        private static void Disposed(Action action)
        {
            try { action(); }
            catch (ObjectDisposedException) { return; }
            throw new InvalidOperationException("Disposed printer accepted an operation.");
        }
        internal static void Run()
        {
            Check(Bt003ConnectionOptions.Usb("USB1").Port == "USB1");
            Check(Bt003ConnectionOptions.Serial("com003").Port == "3");
            Check(Bt003ConnectionOptions.Tcp("2001:0db8::1", 9100).IpAddress == "2001:db8::1");
            foreach (Action invalid in new Action[] {
                () => Bt003ConnectionOptions.Usb(""), () => Bt003ConnectionOptions.Usb("0\0alias"),
                () => Bt003ConnectionOptions.Serial("COM0"), () => Bt003ConnectionOptions.Tcp("example.invalid", 9100),
                () => Bt003ConnectionOptions.Tcp("192.0.2.1", 65536), () => new Bt003Printer(TimeSpan.Zero)
            }) {
                bool rejected = false; try { invalid(); } catch (ArgumentException) { rejected = true; }
                Check(rejected);
            }
            var printer = new Bt003Printer(); var request = Request();
            Check(printer.GetConnectionStatus().State == PrinterState.Disconnected);
            var preflight = printer.ValidateForPrint(request);
            Check(!preflight.IsReady && preflight.Code == "NotConnected" && preflight.ManagedValidation == null && preflight.DeviceValidation == null);
            var print = printer.Print(request);
            Check(print.StopReason == PrintStopReason.NotConnected && print.Rows.Count == 1 && print.NotAttemptedCount == 1);
            Check(print.Validation == null && print.Rows[0].Validation == null && !print.Rows[0].PrintInvoked && !print.NativeCallPending);
            Check(printer.Disconnect().IsSuccess && printer.GetConnectionStatus().ResourcesReleased);
            printer.Dispose(); printer.Dispose();
            Check(printer.GetConnectionStatus().State == PrinterState.Disposed && printer.GetConnectionStatus().ResourcesReleased);
            Disposed(() => printer.Connect(Bt003ConnectionOptions.Usb("synthetic-missing")));
            Disposed(() => printer.ValidateForPrint(request)); Disposed(() => printer.Print(request)); Disposed(() => printer.Disconnect());
            PrinterConnectionTests.Run();
            PrinterPrintingTests.Run();
            Console.WriteLine("PASS: public connection options, disconnected and disposed result contracts; no SDK/device calls");
        }
    }
}
