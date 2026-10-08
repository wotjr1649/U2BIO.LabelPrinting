using System;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // Test data only. No geometry, sensor choice or synthetic text is baked into the common DLL.
    internal static class LabelDiagnosticForms
    {
        internal static string Create(string recipe)
        {
            string media, text, settings;
            if (recipe == "50x25gap3") {
                media = "^Q25,3\r\n^W50\r\n";
                text = "AC,24,24,1,1,0,0,U2BIO TEST\r\n";
                settings = "^H10\r\n^S2\r\n^P1\r\n^C1\r\n^R0\r\n^E0\r\n^D0\r\n^O0\r\n^L\r\n";
            } else if (recipe == "blackmark60x20" || recipe == "blackmark60x20ac") {
                // User-confirmed test stock/form; these settings are not common DLL defaults.
                media = "^Q40,5,7.5+\r\n^W70\r\n^G0\r\n";
                settings = "^E7\r\n^S5\r\n^H19\r\n^P1\r\n^C1\r\n^L\r\n";
                text = (recipe == "blackmark60x20ac" ? "AC" : "AW") + ",80,120,1,1,0,0,U2BIO TEST2\r\n";
            } else throw new ArgumentException("InvalidDiagnosticRecipe");
            return media + settings + text + "E\r\n";
        }
    }
}
