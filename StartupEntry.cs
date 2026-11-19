using System;
using System.IO;
using Microsoft.Win32;

namespace MiniPlayer
{
    // Inicialização com o Windows pela chave "Run" do usuário (mais confiável que atalho na pasta Inicializar:
    // não depende do Windows Script Host, que alguns antivírus/empresas bloqueiam, e é corrigida se o exe mudar de lugar)
    static class StartupEntry
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Name = "MiniPlayer";
        static readonly string OldLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MiniPlayer.lnk");
        static string Command => "\"" + Environment.ProcessPath + "\"";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                    if (k?.GetValue(Name) == null) return false;
                    // desativado no Gerenciador de Tarefas (aba Inicializar) também conta como desligado
                    using var a = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                    return !(a?.GetValue(Name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1);
                }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                try { if (File.Exists(OldLnk)) File.Delete(OldLnk); } catch { } // remove o atalho das versões antigas
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                using var a = Registry.CurrentUser.CreateSubKey(ApprovedKey);
                if (!on) { k.DeleteValue(Name, false); a.DeleteValue(Name, false); return; }
                k.SetValue(Name, Command);
                a.SetValue(Name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); // "habilitado"
                // exe baixado da internet: tira a marca "veio da internet" para o Windows não pedir confirmação silenciosa no logon
                Native.DeleteFile(Environment.ProcessPath + ":Zone.Identifier");
            }
            catch { }
        }

        // Ao abrir: migra o atalho antigo e corrige o caminho se o exe foi movido/renomeado
        public static void Fix()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                var cur = k?.GetValue(Name) as string;
                if (File.Exists(OldLnk) || (cur != null && !string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase)))
                    Set(true);
            }
            catch { }
        }
    }
}
