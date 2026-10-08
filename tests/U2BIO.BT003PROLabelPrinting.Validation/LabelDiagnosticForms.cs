using System;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // Test data only. No geometry, sensor choice or synthetic text is baked into the common DLL.
    internal static class LabelDiagnosticForms
    {
        internal static string Create(string recipe)
        {
            string media, text;
            if (recipe == "50x25gap3") {
                media = "^Q25,3\r\n^W50\r\n";
                text = "AC,24,24,1,1,0,0,U2BIO TEST\r\n";
            } else if (recipe == "blackmark60x20") {
                // Measured test stock: liner70, face60x20, pitch40, mark5, mark-end to face7.5mm.
                media = "^Q40,5,7.5+\r\n^W70\r\n^G0\r\n";
                text = "AC,80,24,1,1,0,0,U2BIO TEST\r\n";
            } else throw new ArgumentException("InvalidDiagnosticRecipe");
            return media + "^H10\r\n^S2\r\n^P1\r\n^C1\r\n^R0\r\n^E0\r\n^D0\r\n^O0\r\n^L\r\n" + text + "E\r\n";
        }
    }
}
