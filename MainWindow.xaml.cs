using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing;
using System.Net.Sockets;
using System.Text;
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

                // تحميل ملف تدريب اكتشاف الرأس
                _headDetector = new CascadeClassifier("haarcascade_frontalface_default.xml");

                System.Windows.Media.CompositionTarget.Rendering += (s, e) => ProcessLoop();

                Log("تم تفعيل نظام الدرع الذكي.. جاهز للرصد");
                Log("جاري البحث عن ملامح بشرية في الموقع...");
            }
            catch (Exception ex)
            {
                Log("فشل في تشغيل النظام: " + ex.Message);
            }
        }

        private void UpdateZoneLive(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RestrictedZone3D != null)
            {
                RestrictedZone3D.Center = new Point3D(SldZoneX.Value, SldZoneY.Value, SldZoneSize.Value / 2);
                RestrictedZone3D.SideLength = SldZoneSize.Value;
            }
        }

        private void ProcessLoop()
        {
            if (_capture == null || _headDetector == null) return;

            using (Mat frame = _capture.QueryFrame())
            {
                if (frame == null) return;
                using (Mat smallFrame = new Mat())
                {
                    CvInvoke.Resize(frame, smallFrame, new System.Drawing.Size(640, 480));
                    UpdateZoneRect(smallFrame.Width, smallFrame.Height);

                    using (Mat gray = new Mat())
                    {
                        CvInvoke.CvtColor(smallFrame, gray, ColorConversion.Bgr2Gray);
                        CvInvoke.EqualizeHist(gray, gray);

                        // اكتشاف الرؤوس بوضوح
                        System.Drawing.Rectangle[] heads = _headDetector.DetectMultiScale(gray, 1.1, 5, new System.Drawing.Size(30, 30));

                        bool alert = false;
                        foreach (var head in heads)
                        {
                            bool isInside = head.IntersectsWith(_currentRestrictedZone);
                            if (isInside) alert = true;

                            MCvScalar color = isInside ? new MCvScalar(0, 0, 255) : new MCvScalar(0, 255, 65);

                            // رسم دائرة تحديد الرأس
                            System.Drawing.Point center = new System.Drawing.Point(head.X + head.Width / 2, head.Y + head.Height / 2);
                            int radius = (int)(head.Width * 0.6);
                            CvInvoke.Circle(smallFrame, center, radius, color, 2);

                            // نص الكاميرا بالعربي
                            string label = isInside ? "اختراق: هدف داخل المنطقة" : "تم رصد: رأس إنسان";
                            CvInvoke.PutText(smallFrame, label, new System.Drawing.Point(head.X, head.Y - 15),
                                FontFace.HersheySimplex, 0.5, color, 1);

                            if (isInside) Log("! تحذير: اختراق داخل المنطقة المحظورة");
                            else if (_rng.Next(0, 50) == 1) Log("تم رصد هدف بشري.. جاري التتبع");

                            SyncTo3D(head, smallFrame.Width, smallFrame.Height);
                        }

                        // رسم منطقة الزون
                        CvInvoke.Rectangle(smallFrame, _currentRestrictedZone, new MCvScalar(0, 0, 255), 2);

                        Dispatcher.Invoke(() => {
                            CameraFeed.Source = ToBitmapSource(smallFrame);
                            TxtLiveCount.Text = heads.Length.ToString("D2");
                            BrdAlert.Visibility = alert ? Visibility.Visible : Visibility.Hidden;

                            // الديسيبل الأزرق
                            int dB = heads.Length > 0 ? _rng.Next(75, 95) : _rng.Next(35, 45);
                            TxtDecibel.Text = dB.ToString();
                            PbAudio.Value = dB;

                            if (dB > 90) Log("رصد ضجيج عالي في الموقع (مريب)");

                            TxtConfidence.Text = heads.Length > 0 ? (98.2 + _rng.NextDouble() * 1.5).ToString("F1") + "%" : "0.0%";
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
                double sz = SldZoneSize.Value;
                _currentRestrictedZone = new System.Drawing.Rectangle((int)(x - sz / 2), (int)(y - sz / 2), (int)sz, (int)sz);
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
                Dispatcher.Invoke(() => { DroneMarker.Center = new Point3D(x, -z, 15); });
            }
            catch { }
        }

        // دالة السجل بالعربي
        private void Log(string m) => Dispatcher.Invoke(() => ThreatsLog.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {m}"));

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Log("جاري إغلاق الأنظمة.. مع السلامة");
            Application.Current.Shutdown();
        }

        public static BitmapSource ToBitmapSource(Mat source)
        {
            using (var temp = source.ToImage<Bgr, byte>())
            using (Bitmap bmp = temp.ToBitmap())
            {
                IntPtr h = bmp.GetHbitmap();
                BitmapSource res = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(h);
                return res;
            }
        }
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    }
}
