using System;
using System.Data;
using System.Globalization;
using System.IO;
using U2BIO.BT003PROLabelPrinting;

// 호출 프로그램 예시다. DB 조회와 업무 열 이름은 제품 DLL에 넣지 않는다.
internal static class CheckupExamples
{
    internal static string CheckupData(DataRow row) =>
        Value(row, "ExamNo") + "!!" + Value(row, "DisplayName") + "!!" + Value(row, "BirthText");

    internal static string SmartCheckupData(DataRow row) =>
        Value(row, "VisitCode") + "!!" + Value(row, "DisplayName") + "!!" + Value(row, "Department");

    private static string Value(DataRow row, string column)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        if (!row.Table.Columns.Contains(column)) throw new ArgumentException("필수 열이 없습니다: " + column);
        string value = row.IsNull(column) ? "" : Convert.ToString(row[column], CultureInfo.InvariantCulture);
        // !!에는 이스케이프 규약이 없으므로 열 값을 몰래 분할하거나 치환하지 않는다.
        if (value.Contains("!!")) throw new ArgumentException("가변데이터 구분자를 포함한 값입니다: " + column);
        return value;
    }

    internal static PrinterResult PrintCheckup(Bt003Printer printer, string path, DataRow row)
    {
        string data = CheckupData(row);
        BtplTemplate template = ReadBlackMark(path);
        return printer.Print(template.ToBtpl(), data);
    }

    internal static PrinterResult PrintSmartCheckup(Bt003Printer printer, string path, DataRow row)
    {
        string data = SmartCheckupData(row);
        ReadBlackMark(path);
        // 배포 후 변경하지 않는 서식 파일용이다. 외부 편집 가능한 파일이면 PrintCheckup처럼 읽은 원문을 Print한다.
        return printer.PrintFile(path, data);
    }

    private static BtplTemplate ReadBlackMark(string path)
    {
        BtplTemplate template = BtplTemplate.Load(path);
        if (template.LabelMode != BtplLabelMode.BlackMark || template.UnprintableTextLineNumbers.Count != 0)
            throw new InvalidDataException("운영 블랙마크 서식과 유효한 AW X 좌표를 확인하세요.");
        return template;
    }

    // 운영자가 인쇄를 요청할 때 호출한다. 오프라인 Main에서는 이 메서드를 실행하지 않는다.
    internal static void PrintOne(string port, string path, DataRow row, bool smartCheckup, bool operatorRequestedFeed)
    {
        var printer = new Bt003Printer();
        try
        {
            PrinterResult connected = printer.ConnectUsb(port, 15);
            Report("연결", connected);
            if (!connected.IsSuccess) return;
            // 운영자가 대기 화면/용지를 확인한 뒤 요청한 경우에만 FEED한다. 오류 뒤 자동 복구하지 않는다.
            if (operatorRequestedFeed)
            {
                PrinterResult aligned = printer.FeedAndConfirmPosition();
                Report("FEED", aligned);
                if (!aligned.IsSuccess) return;
            }
            PrinterResult printed = smartCheckup ? PrintSmartCheckup(printer, path, row) : PrintCheckup(printer, path, row);
            Report("인쇄", printed);
            // PrintCode=0이어도 배출/정리 실패일 수 있다. 실패·불명에 자동 재출력하지 않는다.
        }
        finally
        {
            // 해제 결과를 먼저 알리고 Dispose 실패도 별도로 알린다. 실패 원인을 자동 재연결로 숨기지 않는다.
            try { Report("해제", printer.Disconnect()); }
            finally
            {
                try { printer.Dispose(); }
                catch (InvalidOperationException error) { Console.Error.WriteLine("정리 실패: " + error.Message); }
            }
        }
    }

    private static void Report(string operation, PrinterResult result) => Console.WriteLine(
        "{0}: IsSuccess={1}, NativeCode={2}, PrintInvoked={3}, PrintCode={4}, Message={5}",
        operation, result.IsSuccess, result.NativeCode, result.PrintInvoked, result.PrintCode, result.Message);

    internal static void VerifyOffline(string directory)
    {
        var checkup = new DataTable();
        checkup.Columns.Add("ExamNo"); checkup.Columns.Add("DisplayName"); checkup.Columns.Add("BirthText");
        DataRow first = checkup.Rows.Add("SYN-001", "합성사용자", DBNull.Value);
        var smart = new DataTable();
        smart.Columns.Add("VisitCode"); smart.Columns.Add("DisplayName"); smart.Columns.Add("Department");
        DataRow second = smart.Rows.Add("SYN-002", "", " 합성검사 ");
        Assert(CheckupData(first) == "SYN-001!!합성사용자!!");
        Assert(SmartCheckupData(second) == "SYN-002!!!! 합성검사 ");
        bool missing = false;
        try { CheckupData(second); } catch (ArgumentException) { missing = true; }
        Assert(missing);
        second["DisplayName"] = "A!!B";
        bool delimiter = false;
        try { SmartCheckupData(second); } catch (ArgumentException) { delimiter = true; }
        Assert(delimiter);
        second["DisplayName"] = "";

        var one = BtplTemplate.Parse("^Q35,5,9+\n^W70\n^L\nAW,80,94,10,1,0,0,^V00 ^V01\nAW,80,134,10,1,0,0,^V02\nE\n")
            .SaveAs(Path.Combine(directory, "synthetic-checkup.txt"));
        var two = BtplTemplate.Parse("^Q35,5,9+\n^W70\n^L\nAW,100,94,10,1,0,0,^V02\nAW,100,134,10,1,0,0,^V01 ^V00\nE\n")
            .SaveAs(Path.Combine(directory, "synthetic-smart-checkup.txt"));
        Assert(one.TextElements[0].GetPreviewText(CheckupData(first)) == "SYN-001 합성사용자");
        Assert(one.TextElements[1].GetPreviewText(CheckupData(first)) == "");
        Assert(two.TextElements[0].GetPreviewText(SmartCheckupData(second)) == " 합성검사 ");
        Assert(two.TextElements[1].GetPreviewText(SmartCheckupData(second)) == " SYN-002");
        using (var printer = new Bt003Printer())
        {
            foreach (PrinterResult result in new[] { PrintCheckup(printer, one.FilePath, first), PrintSmartCheckup(printer, two.FilePath, second) })
                Assert(!result.IsSuccess && !result.PrintInvoked && result.NativeCode == null && result.PrintCode == null);
            foreach (string invalid in new[] { "^Q35,5\n^L\nAW,80,94,10,1,0,0,^V00\nE\n", "^Q35,5,9+\n^L\nAW,0,94,10,1,0,0,^V00\nE\n" })
            {
                string path = Path.Combine(directory, "synthetic-invalid.txt");
                File.WriteAllText(path, invalid, System.Text.Encoding.GetEncoding(949));
                bool blocked = false;
                try { PrintCheckup(printer, path, first); } catch (InvalidDataException) { blocked = true; }
                Assert(blocked);
            }
        }
        Console.WriteLine("PASS: two caller DataTable mappings, empty/NULL/delimiter/missing-column checks, Print/PrintFile disconnected; no DB/device");
    }

    private static void Assert(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Checkup caller example check failed.");
    }
}
