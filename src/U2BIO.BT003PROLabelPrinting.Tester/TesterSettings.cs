using System;
using System.Collections.Generic;
using System.IO;

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

        internal string FilePath => throw new NotImplementedException();
        internal string Port => throw new NotImplementedException();
        internal int PresentationDistanceMm => throw new NotImplementedException();
        internal string TemplateFolder => throw new NotImplementedException();
        internal IReadOnlyList<string> RecentFiles => throw new NotImplementedException();
        internal string LoadWarning => throw new NotImplementedException();

        internal static TesterSettings Load(string filePath) => throw new NotImplementedException();
        internal static string CheckPort(string port) => throw new NotImplementedException();
        internal static string CheckDistance(int distanceMm) => throw new NotImplementedException();
        internal static string CheckFolder(string folder) => throw new NotImplementedException();
        internal void Update(string port, int distanceMm, string folder) => throw new NotImplementedException();
        internal void AddRecent(string fullPath) => throw new NotImplementedException();
    }
}
