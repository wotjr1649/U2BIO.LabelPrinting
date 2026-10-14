using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class TesterSettings
    {
        internal const string DefaultPort = "0";
        internal const int DefaultDistanceMm = 15;
        internal const int MaxRecentFiles = 10;

        // x86/x64 실행 파일이 같은 설정 하나를 쓴다.
        internal static string DefaultFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U2BIO", "BT003Tester", "settings.xml");

        private List<string> recentFiles = new List<string>();

        private TesterSettings(string filePath)
        {
            FilePath = filePath;
        }

        internal string FilePath { get; }
        internal string Port { get; private set; } = DefaultPort;
        internal int PresentationDistanceMm { get; private set; } = DefaultDistanceMm;
        internal string TemplateFolder { get; private set; } = "";
        internal IReadOnlyList<string> RecentFiles => recentFiles;
        internal string LoadWarning { get; private set; }

        internal static TesterSettings Load(string filePath)
        {
            var settings = new TesterSettings(filePath);
            try
            {
                if (File.Exists(filePath)) settings.Read();
                else settings.CreateDefault();
            }
            // 설정 문제로 프로그램이 멈추지 않게 어떤 실패든 기본값과 경고로 시작한다.
            catch (Exception error)
            {
                settings = new TesterSettings(filePath) { LoadWarning = "설정을 읽지 못해 기본값으로 시작합니다. " + error.Message };
            }
            return settings;
        }

        internal static string CheckPort(string port)
        {
            if (string.IsNullOrWhiteSpace(port)) return "포트를 입력하세요.";
            return port.Any(char.IsControl) ? "포트에 제어문자를 넣을 수 없습니다." : null;
        }

        internal static string CheckDistance(int distanceMm) =>
            distanceMm < 0 || distanceMm > 1000 ? "배출 거리는 0~1000mm 정수여야 합니다." : null;

        internal static string CheckFolder(string folder) =>
            string.IsNullOrEmpty(folder) || Directory.Exists(folder) ? null : "서식 폴더가 없습니다.";

        internal void Update(string port, int distanceMm, string folder)
        {
            folder = folder ?? "";
            string error = CheckPort(port) ?? CheckDistance(distanceMm) ?? CheckFolder(folder);
            if (error != null) throw new ArgumentException(error);
            // 저장이 끝난 뒤에만 메모리 값을 바꿔 실패 시 화면과 파일이 어긋나지 않게 한다.
            Write(port, distanceMm, folder, recentFiles);
            Port = port;
            PresentationDistanceMm = distanceMm;
            TemplateFolder = folder;
            LoadWarning = null;
        }

        internal void AddRecent(string fullPath)
        {
            recentFiles = new[] { fullPath }
                .Concat(recentFiles.Where(file => !string.Equals(file, fullPath, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxRecentFiles).ToList();
            // 경고 상태에서는 손상된 원본을 보존하려고 확인을 누를 때까지 쓰지 않는다.
            if (LoadWarning == null) Write(Port, PresentationDistanceMm, TemplateFolder, recentFiles);
        }

        private void CreateDefault()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));
            Write(Port, PresentationDistanceMm, TemplateFolder, recentFiles);
        }

        private void Read()
        {
            XElement root = XDocument.Load(FilePath).Root;
            if (root.Name != "TesterSettings" || (string)root.Attribute("version") != "1")
                throw new InvalidDataException("설정 파일 버전이나 구조가 다릅니다.");
            string port = Value(root, "Port");
            int distance = int.Parse(Value(root, "PresentationDistanceMm"), NumberStyles.None, CultureInfo.InvariantCulture);
            string folder = Value(root, "TemplateFolder");
            XElement recent = root.Element("RecentFiles") ?? throw new InvalidDataException("RecentFiles 항목이 없습니다.");
            List<string> files = recent.Elements("File").Select(file => file.Value).ToList();
            string error = CheckPort(port) ?? CheckDistance(distance)
                ?? (files.Count > MaxRecentFiles || files.Any(string.IsNullOrWhiteSpace) ? "최근 파일 목록이 잘못됐습니다." : null);
            if (error != null) throw new InvalidDataException(error);
            Port = port;
            PresentationDistanceMm = distance;
            // 나중에 지워진 서식 폴더는 파일 손상이 아니므로 그 값만 비우고 포트·배출 거리는 유지한다.
            TemplateFolder = CheckFolder(folder) == null ? folder : "";
            recentFiles = files;
        }

        private static string Value(XElement root, string name) =>
            root.Element(name)?.Value ?? throw new InvalidDataException(name + " 항목이 없습니다.");

        private void Write(string port, int distanceMm, string folder, IEnumerable<string> files)
        {
            var document = new XDocument(new XElement("TesterSettings", new XAttribute("version", "1"),
                new XElement("Port", port),
                new XElement("PresentationDistanceMm", distanceMm.ToString(CultureInfo.InvariantCulture)),
                new XElement("TemplateFolder", folder),
                new XElement("RecentFiles", files.Select(file => new XElement("File", file)))));
            string target = Path.GetFullPath(FilePath);
            string temp = Path.Combine(Path.GetDirectoryName(target), Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                document.Save(temp);
                // 쓰는 도중 실패해도 기존 설정 파일이 반쯤 쓰인 상태로 남지 않게 교체한다.
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
