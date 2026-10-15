using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class UsbPrinterPort
    {
        internal UsbPrinterPort(string port, string name, bool detected)
        {
            Port = port;
            Name = name;
            Detected = detected;
        }

        internal string Port { get; }
        internal string Name { get; }
        internal bool Detected { get; }
        internal string Display => Port + " — " + Name;
    }

    // SDK에는 USB 장치 조회 API가 없어 Windows USB 프린터 드라이버(usbprint)의 장치 목록만 읽는다.
    // SetPrinterPort도 이 목록에서 장치를 찾으며, 목록 번호와 SDK 포트 값의 대응은 프린터 1대·포트 "0"에서만 실기로 확인했다.
    internal static class UsbPrinterList
    {
        private const string EnumKey = @"SYSTEM\CurrentControlSet\Services\usbprint\Enum";
        private const string DeviceKey = @"SYSTEM\CurrentControlSet\Enum\";

        internal const string NotDetected = "감지 안 됨";

        // 실패해도 설정 화면은 열려야 하므로 예외 대신 빈 목록과 이유를 돌려준다. 장치 식별자는 밖으로 내보내지 않는다.
        internal static IReadOnlyList<UsbPrinterPort> Detect(string savedPort, out string summary)
        {
            List<string> instances;
            try
            {
                instances = ReadInstances();
            }
            catch (Exception error) when (error is SecurityException || error is UnauthorizedAccessException || error is IOException)
            {
                summary = "USB 프린터 목록을 읽지 못했습니다. " + error.Message;
                return Build(new string[0], _ => null, savedPort);
            }
            IReadOnlyList<UsbPrinterPort> ports = Build(instances, ReadName, savedPort);
            summary = Summary(ports);
            return ports;
        }

        // 목록 번호가 포트 값이다. 비어 있는 번호는 장치가 없으므로 건너뛴다. 저장된 포트가 목록에 없으면 감지 안 됨으로 남긴다.
        internal static IReadOnlyList<UsbPrinterPort> Build(IList<string> instances, Func<string, string> nameOf, string savedPort)
        {
            var ports = new List<UsbPrinterPort>();
            for (int index = 0; index < instances.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(instances[index])) continue;
                string name = nameOf(instances[index])?.Trim();
                ports.Add(new UsbPrinterPort(index.ToString(CultureInfo.InvariantCulture), string.IsNullOrEmpty(name) ? "이름 없음" : name, true));
            }
            if (!string.IsNullOrWhiteSpace(savedPort) && ports.All(port => port.Port != savedPort))
                ports.Insert(0, new UsbPrinterPort(savedPort, NotDetected, false));
            return ports;
        }

        internal static string Summary(IReadOnlyList<UsbPrinterPort> ports)
        {
            int detected = ports.Count(port => port.Detected);
            if (detected == 0) return "USB 프린터가 감지되지 않았습니다.";
            if (detected == 1) return "USB 프린터 1대 감지";
            return "USB 프린터 " + detected + "대 감지: 운영 기준은 1대이며 번호와 장치 대응은 미검증입니다.";
        }

        private static List<string> ReadInstances()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(EnumKey))
            {
                // 장치를 한 번도 연결하지 않았으면 키가 없다.
                if (key == null) return new List<string>();
                int count = key.GetValue("Count") is int value ? value : 0;
                return Enumerable.Range(0, Math.Max(0, count))
                    .Select(index => key.GetValue(index.ToString(CultureInfo.InvariantCulture)) as string).ToList();
            }
        }

        private static string ReadName(string instance)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(DeviceKey + instance))
                {
                    string name = key?.GetValue("FriendlyName") as string;
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                    // DeviceDesc는 "@usbprint.inf,%...%;USB Printing Support"처럼 표시 이름이 마지막 ; 뒤에 있다.
                    string description = key?.GetValue("DeviceDesc") as string;
                    return description?.Substring(description.LastIndexOf(';') + 1);
                }
            }
            catch (Exception error) when (error is SecurityException || error is UnauthorizedAccessException || error is IOException)
            {
                return null;
            }
        }
    }
}
