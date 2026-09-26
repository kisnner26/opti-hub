using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OptiHub
{
    enum Profile { Gaming, General, Oficina }

    static class App
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiHub");
        public static string BackupDir { get { var d = Path.Combine(Root, "backup"); Directory.CreateDirectory(d); return d; } }

        public static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                return o;
            }
        }

        // Devuelve null si salió bien, o el motivo del fallo.
        public static string CreateRestorePoint()
        {
            try
            {
                var mc = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
                var inp = mc.GetMethodParameters("CreateRestorePoint");
                inp["Description"] = "Opti Hub"; inp["RestorePointType"] = 12; inp["EventType"] = 100;
                var outp = mc.InvokeMethod("CreateRestorePoint", inp, null);
                uint rc = Convert.ToUInt32(outp["ReturnValue"]);
                return rc == 0 ? null : (rc == 1058 ? "la Restauración del sistema está desactivada" : "código " + rc);
            }
            catch (Exception e) { return e.Message; }
        }

        // Borra el contenido de una carpeta sin seguir enlaces; devuelve los bytes liberados.
        public static long Clean(string dir)
        {
            long n = 0; string[] files, dirs;
            try { files = Directory.GetFiles(dir); } catch { return 0; }
            foreach (var f in files) try { long l = new FileInfo(f).Length; File.Delete(f); n += l; } catch { }
            try { dirs = Directory.GetDirectories(dir); } catch { dirs = new string[0]; }
            foreach (var d in dirs)
            {
                try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                n += Clean(d);
                try { Directory.Delete(d); } catch { }
            }
            return n;
        }
    }

    static class Reg
    {
        public static RegistryKey Hive(string path, out string sub)
        {
            int i = path.IndexOf('\\'); sub = path.Substring(i + 1);
            return path.Substring(0, i) == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        }
        public static object Get(string path, string name)
        {
            string sub;
            try { using (var k = Hive(path, out sub).OpenSubKey(sub)) return k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
            catch { return null; }
        }
        public static RegistryValueKind KindOf(string path, string name)
        {
            string sub;
            using (var k = Hive(path, out sub).OpenSubKey(sub)) return k == null ? RegistryValueKind.String : k.GetValueKind(name);
        }
        public static void Set(string path, string name, object v, RegistryValueKind kind)
        {
            string sub;
            using (var k = Hive(path, out sub).CreateSubKey(sub)) k.SetValue(name, v, kind);
        }
        public static void Delete(string path, string name)
        {
            string sub;
            using (var k = Hive(path, out sub).OpenSubKey(sub, true)) if (k != null) k.DeleteValue(name, false);
        }
        public static string Ser(object v)
        {
            var b = v as byte[]; if (b != null) return BitConverter.ToString(b).Replace("-", "");
            var a = v as string[]; if (a != null) return string.Join("\u0001", a);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        public static object De(string s, RegistryValueKind k)
        {
            switch (k)
            {
                case RegistryValueKind.DWord: return int.Parse(s, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return long.Parse(s, CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary:
                    var b = new byte[s.Length / 2];
                    for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                    return b;
                case RegistryValueKind.MultiString: return s.Split('\u0001');
                default: return s;
            }
        }
        public static bool Same(object cur, object target)
        {
            if (cur == null) return false;
            if (cur is int && target is int) return (int)cur == (int)target;
            return Ser(cur) == Ser(target);
        }
    }

    abstract class Tweak
    {
        public string Id, Name, Desc, Group, WhyNot = "";
        public bool NeedsReboot;
        public Func<Hw, bool> Applies = h => true;
        public Func<Profile, Hw, bool> Recommended = (p, h) => false;

        protected string BackupPath { get { return Path.Combine(App.BackupDir, Id + ".bak"); } }
        public bool HasBackup { get { return File.Exists(BackupPath); } }
        public void SaveBackup(string s) { if (!HasBackup) File.WriteAllText(BackupPath, s); }
        public string LoadBackup() { return HasBackup ? File.ReadAllText(BackupPath) : ""; }
        public void DropBackup() { if (HasBackup) File.Delete(BackupPath); }

        public abstract bool IsApplied();
        public abstract void Apply();
        public abstract void Revert();
    }

    class RegVal
    {
        public string Path, Name; public object Value; public RegistryValueKind Kind;
        public RegVal(string p, string n, object v, RegistryValueKind k) { Path = p; Name = n; Value = v; Kind = k; }
    }

    class RegTweak : Tweak
    {
        public List<RegVal> Values = new List<RegVal>();
        public RegTweak Dw(string path, string name, int v) { Values.Add(new RegVal(path, name, v, RegistryValueKind.DWord)); return this; }
        public RegTweak Sz(string path, string name, string v) { Values.Add(new RegVal(path, name, v, RegistryValueKind.String)); return this; }
        public RegTweak Bin(string path, string name, byte[] v) { Values.Add(new RegVal(path, name, v, RegistryValueKind.Binary)); return this; }

        public override bool IsApplied() { return Values.All(v => Reg.Same(Reg.Get(v.Path, v.Name), v.Value)); }

        public override void Apply()
        {
            // la copia se guarda solo la primera vez: siempre refleja el estado original
            var lines = new List<string>();
            foreach (var v in Values)
            {
                var cur = Reg.Get(v.Path, v.Name);
                lines.Add(v.Path + "\t" + v.Name + "\t" + (cur == null ? "-" : Reg.KindOf(v.Path, v.Name).ToString()) + "\t" + (cur == null ? "" : Reg.Ser(cur)));
            }
            SaveBackup(string.Join("\n", lines.ToArray()));
            foreach (var v in Values) Reg.Set(v.Path, v.Name, v.Value, v.Kind);
        }

        public override void Revert()
        {
            if (!HasBackup) return;
            foreach (var l in LoadBackup().Split('\n'))
            {
                var f = l.Split('\t'); if (f.Length < 4) continue;
                if (f[2] == "-") Reg.Delete(f[0], f[1]);
                else
                {
                    var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), f[2]);
                    Reg.Set(f[0], f[1], Reg.De(f[3], kind), kind);
                }
            }
            DropBackup();
        }
    }

    class ServiceTweak : Tweak
    {
        const string Base = @"HKLM\SYSTEM\CurrentControlSet\Services\";
        public string[] Services = new string[0];
        public int Start = 3; // 3 = manual, 4 = desactivado

        public ServiceTweak() { Applies = h => Services.Any(Exists); WhyNot = "Tu Windows no tiene este servicio."; }
        static bool Exists(string s) { return Reg.Get(Base + s, "Start") != null; }

        public override bool IsApplied()
        {
            var ex = Services.Where(Exists).ToList();
            return ex.Count > 0 && ex.All(s => Reg.Same(Reg.Get(Base + s, "Start"), Start));
        }

        public override void Apply()
        {
            SaveBackup(string.Join("\n", Services.Where(Exists).Select(s => s + "\t" + Reg.Ser(Reg.Get(Base + s, "Start"))).ToArray()));
            var failed = new List<string>();
            foreach (var s in Services.Where(Exists))
            {
                try { Reg.Set(Base + s, "Start", Start, RegistryValueKind.DWord); }
                catch { failed.Add(s); continue; }
                if (Start == 4)
                    try { using (var sc = new ServiceController(s)) if (sc.Status == ServiceControllerStatus.Running) sc.Stop(); } catch { }
            }
            if (failed.Count > 0) throw new Exception("Windows no dejó cambiar: " + string.Join(", ", failed.ToArray()));
        }

        public override void Revert()
        {
            foreach (var l in LoadBackup().Split('\n'))
            {
                var f = l.Split('\t'); if (f.Length < 2) continue;
                int v; if (int.TryParse(f[1], out v)) Reg.Set(Base + f[0], "Start", v, RegistryValueKind.DWord);
            }
            DropBackup();
        }
    }

    class ActionTweak : Tweak
    {
        public Func<ActionTweak, bool> Check;
        public Action<ActionTweak> DoApply, DoRevert;
        public override bool IsApplied() { try { return Check(this); } catch { return false; } }
        public override void Apply() { DoApply(this); }
        public override void Revert() { if (HasBackup) { DoRevert(this); DropBackup(); } }
    }

    static class Power
    {
        const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        static string Guid(string s) { var m = Regex.Match(s, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"); return m.Success ? m.Value.ToLower() : null; }
        public static string Active() { return Guid(App.Run("powercfg", "/getactivescheme")); }

        public static bool IsOn(ActionTweak t) { var b = t.LoadBackup().Split('|'); return b.Length == 2 && Active() == b[1]; }
        public static void On(ActionTweak t)
        {
            string created;
            if (t.HasBackup) created = t.LoadBackup().Split('|')[1];
            else
            {
                string old = Active();
                created = Guid(App.Run("powercfg", "-duplicatescheme " + Ultimate));
                if (created == null || created == Ultimate) throw new Exception("tu Windows no permite el plan Máximo rendimiento");
                t.SaveBackup(old + "|" + created);
            }
            App.Run("powercfg", "/setactive " + created);
        }
        public static void Off(ActionTweak t)
        {
            var b = t.LoadBackup().Split('|');
            if (b.Length != 2) return;
            if (b[0].Length > 0) App.Run("powercfg", "/setactive " + b[0]);
            App.Run("powercfg", "-delete " + b[1]);
        }
    }

    static class Tweaks
    {
        const string G1 = "Rendimiento del sistema", G2 = "Juegos", G3 = "Privacidad y segundo plano", G4 = "Apariencia", G5 = "Servicios";
        const string MM = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string CDM = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string SYS = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
        const string ADV = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string PWR = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";

        static RegTweak R(string id, string group, string name, string desc) { return new RegTweak { Id = id, Group = group, Name = name, Desc = desc }; }

        public static List<Tweak> Build(Hw hw)
        {
            Func<Profile, Hw, bool> all = (p, h) => true, gaming = (p, h) => p == Profile.Gaming;
            Func<Hw, bool> desktop = h => !h.Laptop;
            var L = new List<Tweak>();
            RegTweak r;

            // ---- rendimiento del sistema ----
            int ramKb = (int)Math.Min(int.MaxValue, hw.RamGB * 1024 * 1024);
            r = R("svchost", G1, "Agrupar procesos svchost según tu RAM",
                "Windows abre decenas de svchost.exe separados. Este ajuste (el mismo de WinUtil) los agrupa según tu memoria: verás muchos menos procesos.")
                .Dw(@"HKLM\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", ramKb);
            r.NeedsReboot = true; r.Recommended = all; L.Add(r);

            L.Add(new ActionTweak
            {
                Id = "powerplan", Group = G1, Name = "Plan de energía Máximo rendimiento",
                Desc = "Activa el plan oculto de Windows que evita que la CPU baje de frecuencia. Más rendimiento a cambio de algo más de consumo.",
                Applies = desktop, WhyNot = "No aplica en portátiles: gasta batería y calienta el equipo.",
                Recommended = (p, h) => p == Profile.Gaming || (p == Profile.General && h.Tier == "alto"),
                Check = Power.IsOn, DoApply = Power.On, DoRevert = Power.Off
            });

            r = R("throttling", G1, "Quitar la limitación de energía", "Windows deja de frenar los procesos en segundo plano para ahorrar energía.")
                .Dw(PWR + @"\PowerThrottling", "PowerThrottlingOff", 1);
            r.Applies = desktop; r.WhyNot = "En portátiles esta limitación alarga la batería: mejor mantenerla."; r.Recommended = gaming; L.Add(r);

            L.Add(new ActionTweak
            {
                Id = "hibernate", Group = G1, Name = "Desactivar la hibernación",
                Desc = "Borra hiberfil.sys (varios GB) y desactiva el inicio rápido. Útil en sobremesas con SSD, que ya arrancan rápido.",
                Applies = desktop, WhyNot = "En portátiles la hibernación protege tu trabajo cuando se agota la batería.",
                Recommended = (p, h) => h.SystemSsd,
                Check = t => Reg.Same(Reg.Get(PWR, "HibernateEnabled"), 0),
                DoApply = t => { var cur = Reg.Get(PWR, "HibernateEnabled"); t.SaveBackup(cur == null ? "1" : Reg.Ser(cur)); App.Run("powercfg", "/hibernate off"); },
                DoRevert = t => { if (t.LoadBackup().Trim() != "0") App.Run("powercfg", "/hibernate on"); }
            });

            r = R("startupdelay", G1, "Abrir las apps de inicio sin espera", "Windows retrasa unos segundos las apps que arrancan con el sistema. Esto quita ese retraso.")
                .Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
            r.Recommended = all; L.Add(r);

            r = R("menudelay", G1, "Menús más rápidos", "Los submenús se abren a los 100 ms en lugar de 400 ms. Se nota al volver a iniciar sesión.")
                .Sz(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "100");
            r.Recommended = (p, h) => p != Profile.Gaming; L.Add(r);

            r = R("responsiveness", G1, "Prioridad a juegos y multimedia", "Reserva menos CPU para tareas de fondo mientras juegas o ves vídeo, y quita el límite de red para multimedia.")
                .Dw(MM, "SystemResponsiveness", 10).Dw(MM, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
            r.Recommended = gaming; L.Add(r);

            // ---- juegos ----
            r = R("gamestask", G2, "Prioridad alta para juegos", "Da más prioridad de CPU y GPU a los juegos que Windows reconoce.")
                .Dw(MM + @"\Tasks\Games", "GPU Priority", 8).Dw(MM + @"\Tasks\Games", "Priority", 6)
                .Sz(MM + @"\Tasks\Games", "Scheduling Category", "High").Sz(MM + @"\Tasks\Games", "SFIO Priority", "High");
            r.Recommended = gaming; L.Add(r);

            r = R("gamemode", G2, "Activar el modo juego", "Windows pausa actualizaciones y tareas de fondo mientras juegas.")
                .Dw(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1).Dw(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
            r.Recommended = gaming; L.Add(r);

            r = R("gamedvr", G2, "Quitar la grabación de fondo de Xbox Game Bar", "Game DVR graba en segundo plano y resta FPS. Las capturas y grabaciones de la Game Bar quedan desactivadas.")
                .Dw(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0)
                .Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)
                .Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
            r.Recommended = (p, h) => p == Profile.Gaming || h.Tier == "básico"; L.Add(r);

            r = R("hags", G2, "Programación de GPU acelerada por hardware", "La tarjeta gráfica gestiona su propia memoria y baja la latencia en juegos.")
                .Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
            r.Applies = h => h.DedicatedGpu && h.Build >= 19041; r.WhyNot = "Necesita una tarjeta gráfica dedicada compatible.";
            r.NeedsReboot = true; r.Recommended = gaming; L.Add(r);

            r = R("mouseaccel", G2, "Quitar la aceleración del ratón", "Desactiva «Mejorar precisión del puntero»: el ratón responde igual siempre y apuntas mejor.")
                .Sz(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0").Sz(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0").Sz(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0");
            r.Recommended = gaming; L.Add(r);

            // ---- privacidad y segundo plano ----
            r = R("telemetry", G3, "Reducir la telemetría", "Windows envía a Microsoft el mínimo de datos de diagnóstico.")
                .Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
            r.Recommended = all; L.Add(r);

            L.Add(new ServiceTweak
            {
                Id = "diagtrack", Group = G3, Name = "Desactivar el servicio de telemetría",
                Desc = "Detiene «Experiencias del usuario y telemetría asociadas» (DiagTrack), que recopila y envía datos en segundo plano.",
                Services = new[] { "DiagTrack", "dmwappushservice" }, Start = 4, Recommended = all
            });

            r = R("activity", G3, "Quitar el historial de actividad", "Windows deja de registrar las apps y archivos que usas.")
                .Dw(SYS, "EnableActivityFeed", 0).Dw(SYS, "PublishUserActivities", 0).Dw(SYS, "UploadUserActivities", 0);
            r.Recommended = all; L.Add(r);

            r = R("bgapps", G3, "Bloquear apps en segundo plano", "Las apps de la Tienda no se ejecutan cuando no las usas. Algunas (WhatsApp, Teams) pueden dejar de avisarte.")
                .Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2);
            r.Recommended = (p, h) => p == Profile.Gaming || (h.Tier == "básico" && p != Profile.Oficina); L.Add(r);

            r = R("suggestions", G3, "Quitar sugerencias y apps promocionadas", "Windows deja de instalar apps sugeridas y de mostrar anuncios en Inicio y Configuración.")
                .Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1)
                .Dw(CDM, "SilentInstalledAppsEnabled", 0).Dw(CDM, "SystemPaneSuggestionsEnabled", 0)
                .Dw(CDM, "SubscribedContent-338388Enabled", 0).Dw(CDM, "SubscribedContent-338389Enabled", 0);
            r.Recommended = all; L.Add(r);

            r = R("copilot", G3, "Desactivar Copilot", "Quita el asistente Copilot de Windows y su proceso en segundo plano.")
                .Dw(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
            r.Recommended = (p, h) => p != Profile.Oficina; L.Add(r);

            r = R("widgets", G3, "Desactivar los widgets", "Quita el panel de noticias y tiempo, que ocupa memoria aunque no lo abras.")
                .Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
            r.Recommended = all; L.Add(r);

            r = R("bingsearch", G3, "Buscar solo en tu equipo", "La búsqueda del menú Inicio deja de consultar Bing: resultados más rápidos y sin anuncios.")
                .Dw(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
            r.Recommended = all; L.Add(r);

            // ---- apariencia ----
            r = R("visualfx", G4, "Efectos visuales para rendimiento", "Quita animaciones, sombras y transiciones, pero mantiene las fuentes suavizadas. Se nota al volver a iniciar sesión.")
                .Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3)
                .Bin(@"HKCU\Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 })
                .Sz(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0")
                .Dw(ADV, "TaskbarAnimations", 0).Dw(ADV, "ListviewShadow", 0)
                .Dw(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0);
            r.Recommended = (p, h) => h.Tier == "básico"; L.Add(r);

            r = R("transparency", G4, "Quitar la transparencia", "Desactiva el efecto cristal de la barra de tareas y los menús. Libera algo de GPU en equipos modestos.")
                .Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
            r.Recommended = (p, h) => h.Tier == "básico"; L.Add(r);

            // ---- servicios ----
            L.Add(new ServiceTweak
            {
                Id = "rareservices", Group = G5, Name = "Servicios poco usados en manual",
                Desc = "Fax, mapas, modo demo, control parental, pagos NFC, teléfono e informes de errores se inician solo cuando hacen falta.",
                Services = new[] { "Fax", "MapsBroker", "RetailDemo", "WpcMonSvc", "SEMgrSvc", "PhoneSvc", "WerSvc", "wisvc" }, Start = 3, Recommended = all
            });

            var sysmain = new ServiceTweak
            {
                Id = "sysmain", Group = G5, Name = "Desactivar SysMain (Superfetch)",
                Desc = "SysMain precarga apps en memoria. En un SSD apenas ayuda y gasta RAM y disco en segundo plano.",
                Services = new[] { "SysMain" }, Start = 4, Recommended = gaming
            };
            sysmain.Applies = h => h.SystemSsd; sysmain.WhyNot = "En discos duros HDD, SysMain acelera la apertura de apps: mejor mantenerlo.";
            L.Add(sysmain);

            L.Add(new ServiceTweak
            {
                Id = "wsearch", Group = G5, Name = "Desactivar la indexación de búsqueda",
                Desc = "Windows deja de indexar tus archivos en segundo plano. Libera disco y CPU, pero buscar archivos será más lento.",
                Services = new[] { "WSearch" }, Start = 4, Recommended = (p, h) => p == Profile.Gaming && h.Tier == "básico"
            });

            return L;
        }
    }

    class StartupItem { public string Name, Command, Source, ApprovedPath, ApprovedName; public bool Enabled; }

    // Apps que arrancan con Windows. Se activan/desactivan igual que el Administrador de tareas (StartupApproved).
    static class Startup
    {
        const string AP = @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> Load()
        {
            var list = new List<StartupItem>();
            FromRun(list, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU" + AP + "Run", "solo tu usuario");
            FromRun(list, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM" + AP + "Run", "todos los usuarios");
            FromRun(list, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM" + AP + "Run32", "todos los usuarios · 32 bits");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU" + AP + "StartupFolder", "carpeta Inicio de tu usuario");
            FromFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM" + AP + "StartupFolder", "carpeta Inicio común");
            return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void FromRun(List<StartupItem> list, string run, string ap, string src)
        {
            string sub;
            using (var k = Reg.Hive(run, out sub).OpenSubKey(sub))
            {
                if (k == null) return;
                foreach (var n in k.GetValueNames())
                    if (n.Length > 0)
                        list.Add(new StartupItem { Name = n, Command = Convert.ToString(k.GetValue(n)), Source = src, ApprovedPath = ap, ApprovedName = n, Enabled = IsOn(ap, n) });
            }
        }

        static void FromFolder(List<StartupItem> list, string dir, string ap, string src)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                string fn = Path.GetFileName(f);
                if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupItem { Name = Path.GetFileNameWithoutExtension(f), Command = f, Source = src, ApprovedPath = ap, ApprovedName = fn, Enabled = IsOn(ap, fn) });
            }
        }

        static bool IsOn(string ap, string name)
        {
            var b = Reg.Get(ap, name) as byte[];
            return b == null || b.Length == 0 || (b[0] & 1) == 0;
        }

        public static void SetEnabled(StartupItem it, bool on)
        {
            var b = new byte[12]; b[0] = (byte)(on ? 2 : 3);
            if (!on) Array.Copy(BitConverter.GetBytes(DateTime.Now.ToFileTime()), 0, b, 4, 8);
            Reg.Set(it.ApprovedPath, it.ApprovedName, b, RegistryValueKind.Binary);
        }
    }
}
