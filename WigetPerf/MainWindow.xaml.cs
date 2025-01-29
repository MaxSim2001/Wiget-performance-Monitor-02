using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Drawing; 
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Windows.Threading;  // pour le DispatcherTimer
using LibreHardwareMonitor.Hardware;
using System.Runtime.InteropServices; // Pour DllImport
using System.Windows.Interop; // Pour WindowInteropHelper
using Application = System.Windows.Application; 
using MessageBox = System.Windows.MessageBox;
using Microsoft.Win32; 

namespace WigetPerf
{
    public partial class MainWindow : Window
    {
        // === PerformanceCounter (CPU, RAM, Disques) ===
        private PerformanceCounter _cpuCounter;
        private PerformanceCounter _memCounter;
        private PerformanceCounter _diskReadCounter;
        private PerformanceCounter _diskWriteCounter;

        // === LibreHardwareMonitor ===
        private Computer _computer; // Permet de scanner le hardware
        private DispatcherTimer _timer;

        // Pour activer l'effet Mica (Windows 11)
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38; // DWM constant
        private const int DWMWA_MICA_EFFECT = 1029;       // Ancienne constant Win11
        private const int DWM_SYSTEMBACKDROP_TYPE_AUTO = 3; // auto = Mica sur Win11, fallback sur Win10 ?

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize
        );

        public MainWindow()
        {
            InitializeComponent();
            RegisterInStartup(true);

            // 2) Configuration des compteurs PerformanceCounter classiques
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memCounter = new PerformanceCounter("Memory", "Available MBytes");
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

            // On appelle NextValue() une première fois (les premiers appels retournent souvent 0)
            _cpuCounter.NextValue();
            _memCounter.NextValue();
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();

            // 3) Configuration de LibreHardwareMonitor
            //    Activez IsGpuEnabled pour détecter les GPUs (Nvidia, AMD, Intel iGPU)
            _computer = new Computer()
            {
                IsGpuEnabled = true,
                // Vous pouvez activer d'autres catégories si besoin
                // IsCpuEnabled = true,
                // IsMemoryEnabled = true,
                // etc.
            };
            // Ouvre la session pour scanner le hardware
            _computer.Open();

            // 4) Timer pour rafraîchir toutes les 1s
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += _timer_Tick;
            _timer.Start();

            TryEnableMica();
        }

        // Méthode appelée à chaque tick (chaque seconde)
        private void _timer_Tick(object? sender, EventArgs e)
        {
            // === CPU, RAM, Disque via PerformanceCounter ===
            float cpuUsage = _cpuCounter.NextValue();
            float memAvailable = _memCounter.NextValue();
            float diskRead = _diskReadCounter.NextValue();   // en Bytes/sec
            float diskWrite = _diskWriteCounter.NextValue(); // en Bytes/sec

            // Mise à jour des TextBlock (x:Name) définis dans MainWindow.xaml
            CpuUsageText.Text   = $"CPU : {cpuUsage:F1} %";
            MemUsageText.Text   = $"RAM dispo : {memAvailable:F0} MB";
            DiskReadText.Text   = $"Lecture : {diskRead / 1024:F1} Ko/s";
            DiskWriteText.Text  = $"Écriture : {diskWrite / 1024:F1} Ko/s";

            // === GPU via LibreHardwareMonitor ===
            float gpuLoad = GetAverageGpuLoad();
            // Affichage : vous pouvez afficher la moyenne ou un total
            GpuUsageText.Text = $"GPU : {gpuLoad:F1} %";
        }

        /// <summary>
        /// Scanne tous les GPU (Nvidia, AMD, Intel) détectés par LibreHardwareMonitor
        /// et retourne la moyenne du "GPU Core" load (en %).
        /// </summary>
        private float GetAverageGpuLoad()
        {
            float totalLoad = 0f;
            int gpuCount = 0;

            // Parcourt tous les composants que LibreHardwareMonitor a détectés
            foreach (var hardware in _computer.Hardware)
            {
                // On s’intéresse uniquement aux GPU
                if (hardware.HardwareType == HardwareType.GpuNvidia ||
                    hardware.HardwareType == HardwareType.GpuAmd ||
                    hardware.HardwareType == HardwareType.GpuIntel)
                {
                    // Met à jour les capteurs pour ce hardware
                    hardware.Update();

                    // Parcourt les capteurs de type "Load"
                    foreach (var sensor in hardware.Sensors)
                    {
                        // Selon la carte, le capteur de la charge GPU
                        // peut s’appeler "GPU Core" ou "GPU Total" ou autre.
                        if (sensor.SensorType == SensorType.Load &&
                            (sensor.Name == "GPU Core" || sensor.Name == "GPU Total"))
                        {
                            float? value = sensor.Value; // Valeur en %
                            if (value.HasValue)
                            {
                                totalLoad += value.Value;
                                gpuCount++;
                            }
                        }
                    }
                }
            }

            if (gpuCount > 0)
                return totalLoad / gpuCount; // Moyenne sur tous les GPU détectés
            else
                return 0f; // Aucun GPU trouvé
        }

        // Bouton fermer
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        private void TryEnableMica()
        {
            // Récupère le handle de la fenêtre
            var windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero) return;

            // On essaie d'activer Mica via DWM
            int trueValue = 1;

            // Méthode 1 : activer DWM_SYSTEMBACKDROP_TYPE (valeur 3 => Mica)
            int backdropType = DWM_SYSTEMBACKDROP_TYPE_AUTO;
            DwmSetWindowAttribute(windowHandle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // Méthode 2 : forcer Mica (certains Windows 11 utilisent la const 1029)
            DwmSetWindowAttribute(windowHandle, DWMWA_MICA_EFFECT, ref trueValue, sizeof(int));
        }

        // Événement clic sur la zone "TitleBarDragArea" => on déplace la fenêtre
        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public static void RegisterInStartup(bool enable)
        {
            string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (enable)
                {
                    // Ajoute ou modifie la clé
                    key.SetValue("MonWidgetPerf", $"\"{System.Reflection.Assembly.GetExecutingAssembly().Location}\"");
                }
                else
                {
                    // Supprime la clé
                    if (key.GetValue("MonWidgetPerf") != null)
                        key.DeleteValue("MonWidgetPerf");
                }
            }
        }
    }
}