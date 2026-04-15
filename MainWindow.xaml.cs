using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.IO;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private VideoCapture? _capture;
        private CascadeClassifier? _headDetector;
        private System.Drawing.Rectangle _currentRestrictedZone;
        private UdpClient _udpClient = new UdpClient();
        private Random _rng = new Random();

        public MainWindow()
        {
            InitializeComponent();
            InitializeSystem();
        }

        private void InitializeSystem()
        {
            try
            {
                _capture = new VideoCapture(0);

                // اسم الملف المستهدف
                string fileName = "haarcascade_frontalface_default.xml";

                // البحث في مجلد التشغيل الحالي
                string path1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                // البحث في المجلد الأب (للاحتياط)
                string path2 = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName, fileName);

                string finalPath = "";

                if (File.Exists(path1)) finalPath = path1;
                else if (File.Exists(path2)) finalPath = path2;

                if (!string.IsNullOrEmpty(finalPath))
                {
                    _headDetector = new CascadeClassifier(finalPath);
                    Log("✅ تم تحميل الملف من: " + finalPath);
                }
                else
                {
                    Log("❌ لم أجد الملف. أنا أبحث هنا: " + AppDomain.CurrentDomain.BaseDirectory);
                    Log("تأكد أن الملف ليس haarcascade_frontalface_default.xml.xml");
                }

                System.Windows.Media.CompositionTarget.Rendering += (s, e) => ProcessLoop();
            }
            catch (Exception ex)
            {
                Log("⚠️ فشل: " + ex.Message);
            }
        }

        private void ProcessLoop()
        {
            if (_capture == null || _headDetector == null) return;

            using (Mat frame = new Mat())
            {
                _capture.Read(frame);
                if (frame.IsEmpty) return;

                using (Mat smallFrame = new Mat())
                {
                    CvInvoke.Resize(frame, smallFrame, new System.Drawing.Size(640, 480));
                    UpdateZoneRect(smallFrame.Width, smallFrame.Height);

                    using (Mat gray = new Mat())
                    {
                        CvInvoke.CvtColor(smallFrame, gray, ColorConversion.Bgr2Gray);
                        CvInvoke.EqualizeHist(gray, gray);

                        System.Drawing.Rectangle[] heads = _headDetector.DetectMultiScale(gray, 1.1, 5, new System.Drawing.Size(30, 30));

                        bool alert = false;
                        foreach (var head in heads)
                        {
                            bool isInside = head.IntersectsWith(_currentRestrictedZone);
                            if (isInside) alert = true;

                            MCvScalar color = isInside ? new MCvScalar(0, 0, 255) : new MCvScalar(0, 255, 65);
                            CvInvoke.Rectangle(smallFrame, head, color, 2);

                            SyncTo3D(head, smallFrame.Width, smallFrame.Height);
                        }

                        CvInvoke.Rectangle(smallFrame, _currentRestrictedZone, new MCvScalar(0, 0, 255), 2);

                        Dispatcher.Invoke(() => {
                            CameraFeed.Source = ToBitmapSource(smallFrame);
                            TxtLiveCount.Text = heads.Length.ToString("D2");
                            BrdAlert.Visibility = alert ? Visibility.Visible : Visibility.Hidden;
                            TxtThreatLevel.Text = alert ? "اختراق أمني" : "الوضع آمن";
                            TxtThreatLevel.Foreground = alert ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.SpringGreen;
                        });
                    }
                }
            }
        }

        private void UpdateZoneRect(int w, int h)
        {
            Dispatcher.Invoke(() => {
                double x = (SldZoneX.Value + 200) / 400.0 * w;
                double y = (200 - SldZoneY.Value) / 400.0 * h;
                _currentRestrictedZone = new System.Drawing.Rectangle((int)(x - SldZoneSize.Value / 2), (int)(y - SldZoneSize.Value / 2), (int)SldZoneSize.Value, (int)SldZoneSize.Value);
            });
        }

        private void SyncTo3D(System.Drawing.Rectangle rect, int w, int h)
        {
            float x = ((float)rect.X / w) * 400 - 200;
            float z = ((float)rect.Y / h) * 400 - 200;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes($"{x},{z}");
                _udpClient.Send(data, data.Length, "127.0.0.1", 5005);
                if (DroneMarker != null) Dispatcher.Invoke(() => DroneMarker.Center = new Point3D(x, -z, 15));
            }
            catch { }
        }

        private void Log(string m) => Dispatcher.Invoke(() => ThreatsLog.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {m}"));

        private void Button_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

        public static BitmapSource ToBitmapSource(Mat source)
        {
            using (Bitmap bmp = source.ToBitmap())
            {
                IntPtr h = bmp.GetHbitmap();
                try { return Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                finally { DeleteObject(h); }
            }
        }
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        private void UpdateZoneLive(object sender, RoutedPropertyChangedEventArgs<double> e) { }
    }
}
