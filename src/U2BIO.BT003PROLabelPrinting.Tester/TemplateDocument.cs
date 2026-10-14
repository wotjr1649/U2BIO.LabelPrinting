using System;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class TemplateDocument
    {
        internal BtplTemplate Template => throw new NotImplementedException();
        internal bool IsDirty => throw new NotImplementedException();
        internal DateTime? LastLoadedOrSaved => throw new NotImplementedException();
        internal string DisplayPath => throw new NotImplementedException();

        internal void Open(string path) => throw new NotImplementedException();
        internal void Reload() => throw new NotImplementedException();
        internal void Apply(Func<BtplTemplate, BtplTemplate> edit) => throw new NotImplementedException();
        internal void Save() => throw new NotImplementedException();
        internal void SaveAs(string path, bool overwrite) => throw new NotImplementedException();
        internal static string PreviewText(string payload, string variableData) => throw new NotImplementedException();
        internal static bool HasGapLabelSetting(BtplTemplate template) => throw new NotImplementedException();
    }
}
