using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OptiHub
{
    // Reconoce el equipo: CPU, RAM, gráfica, disco del sistema, portátil o sobremesa y versión de Windows.
    class Hw
    {
        public string Cpu = "desconocido", Gpu = "desconocida", Disk = "desconocido", DiskKind = "sin identificar", Windows = "Windows";
        public int Cores, Threads, Build;
        public double RamGB, VramGB, DiskGB;
        public bool DedicatedGpu, SystemSsd, Nvme, Laptop;
        public string Tier = "medio"; // básico, medio, alto

        public string TierLabel { get { return Tier == "básico" ? "equipo básico" : Tier == "alto" ? "equipo potente" : "equipo medio"; } }

        static List<ManagementObject> Q(string scope, string wql)
        {
            try { using (var s = new ManagementObjectSearcher(scope, wql)) return s.Get().Cast<ManagementObject>().ToList(); }
            catch { return new List<ManagementObject>(); }
        }
        static string Str(object o) { return o == null ? "" : o.ToString().Trim(); }
        static long Num(object o) { try { return o == null ? 0 : Convert.ToInt64(o); } catch { return 0; } }

        public static Hw Detect()
        {
            var h = new Hw();
            foreach (var m in Q(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                h.Cpu = Regex.Replace(Str(m["Name"]), @"\s+", " ");
                h.Cores += (int)Num(m["NumberOfCores"]); h.Threads += (int)Num(m["NumberOfLogicalProcessors"]);
            }
            foreach (var m in Q(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                h.RamGB = Num(m["TotalPhysicalMemory"]) / 1073741824.0;
            // capacidad instalada real (Windows reserva una parte y muestra menos)
            long mods = Q(@"root\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory").Sum(m => Num(m["Capacity"]));
            if (mods > 0) h.RamGB = mods / 1073741824.0;

            DetectGpu(h);
            DetectDisk(h);

            int[] laptop = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
            foreach (var m in Q(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            {
                var t = m["ChassisTypes"] as ushort[];
                if (t != null && t.Any(x => laptop.Contains((int)x))) h.Laptop = true;
            }
            if (!h.Laptop && Q(@"root\cimv2", "SELECT Name FROM Win32_Battery").Count > 0) h.Laptop = true;

            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                if (k != null)
                {
                    int b; int.TryParse(Str(k.GetValue("CurrentBuild")), out b); h.Build = b;
                    h.Windows = ((b >= 22000 ? "Windows 11 " : "Windows 10 ") + Str(k.GetValue("EditionID")) + " " + Str(k.GetValue("DisplayVersion"))).Trim();
                }

            if (h.RamGB < 8.5 || h.Threads <= 4) h.Tier = "básico";
            else if (h.RamGB >= 15.5 && h.Threads >= 12 && h.DedicatedGpu) h.Tier = "alto";
            return h;
        }

        static double Bytes(object o)
        {
            if (o == null) return 0;
            var b = o as byte[];
            if (b != null) return b.Length >= 8 ? BitConverter.ToInt64(b, 0) : b.Length >= 4 ? BitConverter.ToUInt32(b, 0) : 0;
            if (o is int) return (uint)(int)o; // DWORD de más de 2 GB llega en negativo
            try { return Convert.ToDouble(o); } catch { return 0; }
        }

        static void DetectGpu(Hw h)
        {
            // el registro da la VRAM real; WMI se queda en 4 GB como máximo
            const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            string best = null; double bestV = -1;
            using (var root = Registry.LocalMachine.OpenSubKey(cls))
                if (root != null)
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Str(k.GetValue("DriverDesc"));
                                if (name.Length == 0 || name.Contains("Basic") || name.Contains("Remote") || name.Contains("Virtual") || name.Contains("Parsec")) continue;
                                double v = Bytes(k.GetValue("HardwareInformation.qwMemorySize"));
                                if (v == 0) v = Bytes(k.GetValue("HardwareInformation.MemorySize"));
                                if (v > bestV) { bestV = v; best = name; }
                            }
                        }
                        catch { }
                    }
            if (best == null)
                foreach (var m in Q(@"root\cimv2", "SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    double v = Bytes(m["AdapterRAM"]);
                    if (v > bestV) { bestV = v; best = Str(m["Name"]); }
                }
            if (best != null) { h.Gpu = best; h.VramGB = Math.Max(0, bestV) / 1073741824.0; }
            string n = h.Gpu.ToUpper();
            h.DedicatedGpu = n.Contains("NVIDIA") || n.Contains(" ARC") || ((n.Contains("RADEON") || n.Contains("AMD")) && h.VramGB >= 3);
        }

        static void DetectDisk(Hw h)
        {
            const string st = @"root\Microsoft\Windows\Storage";
            string diskNo = null;
            foreach (var m in Q(st, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
                if (Num(m["DriveLetter"]) == 'C') { diskNo = Str(m["DiskNumber"]); break; }
            foreach (var m in Q(st, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk"))
            {
                if (diskNo != null && Str(m["DeviceId"]) != diskNo) continue;
                long media = Num(m["MediaType"]), bus = Num(m["BusType"]);
                h.Nvme = bus == 17;
                h.SystemSsd = h.Nvme || media == 4 || media == 5;
                h.DiskKind = h.Nvme ? "SSD NVMe" : h.SystemSsd ? "SSD" : media == 3 ? "HDD (disco duro)" : "sin identificar";
                h.Disk = Str(m["FriendlyName"]); h.DiskGB = Num(m["Size"]) / 1e9;
                break;
            }
        }
    }
}
