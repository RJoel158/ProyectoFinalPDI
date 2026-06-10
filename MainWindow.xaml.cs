// Aliases explícitos para resolver ambigüedades
using AForge.Video;
using AForge.Video.DirectShow;
using ProyectoFinalPDI.AForge;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingBrush = System.Drawing.Brush;
using DrawingColor = System.Drawing.Color;
using DrawingPen = System.Drawing.Pen;
using DrawingPF = System.Drawing.Imaging.PixelFormat;
using DrawingRect = System.Drawing.Rectangle;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPF = System.Windows.Media.PixelFormats;
using WpfRect = System.Windows.Shapes.Rectangle;

namespace ProyectoFinalPDI
{
    public partial class MainWindow : Window
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private SkinDetector _skinDetector = new SkinDetector();
        private GameEngine _gameEngine = new GameEngine();
        private Stopwatch _frameTimer = new Stopwatch();
        private volatile Bitmap _lastFrame;
        private readonly object _frameLock = new object();
        private bool _processingFrame;

        // Calibración de puntos
        private SkinDetector.BodyPoints _calibrationReference = null;
        private bool _isCalibrating = false;
        private int _calibrationFrameCount = 0;

        // Pens y Brushes de System.Drawing (para dibujar sobre Bitmap)
        private static readonly DrawingPen SkeletonPen = new DrawingPen(DrawingColor.FromArgb(180, 123, 97, 255), 4);
        private static readonly DrawingPen GoodPen = new DrawingPen(DrawingColor.FromArgb(200, 0, 200, 83), 4);
        private static readonly DrawingBrush HeadBrush = new System.Drawing.SolidBrush(DrawingColor.FromArgb(220, 255, 193, 7));
        private static readonly DrawingBrush HandBrush = new System.Drawing.SolidBrush(DrawingColor.FromArgb(220, 33, 150, 243));
        private static readonly DrawingBrush HipBrush = new System.Drawing.SolidBrush(DrawingColor.FromArgb(220, 156, 39, 176));
        private static readonly DrawingBrush ShoulderBrush = new System.Drawing.SolidBrush(DrawingColor.FromArgb(220, 255, 255, 255));
        private static readonly DrawingPen WhitePen = new DrawingPen(DrawingColor.White, 2);

        public MainWindow()
        {
            InitializeComponent();
            _gameEngine = new GameEngine();
        }

        // ── Carga inicial ─────────────────────────────────────────
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCameras();
        }

        private void LoadCameras()
        {
            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            CameraSelector.Items.Clear();

            if (_videoDevices.Count == 0)
            {
                StatusLabel.Text = "⚠ No se encontraron cámaras";
                return;
            }

            foreach (FilterInfo fi in _videoDevices)
                CameraSelector.Items.Add(fi.Name);

            CameraSelector.SelectedIndex = 0;
        }

        private void CameraSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            StartCamera(CameraSelector.SelectedIndex);
        }

        // ── Cámara ───────────────────────────────────────────────
        private void StartCamera(int index)
        {
            StopCamera();
            if (_videoDevices == null || index < 0 || index >= _videoDevices.Count) return;

            _videoSource = new VideoCaptureDevice(_videoDevices[index].MonikerString);

            // Buscar la resolución más alta disponible (preferiblemente 1280x720 o superior)
            VideoCapabilities bestCap = null;
            foreach (VideoCapabilities cap in _videoSource.VideoCapabilities)
            {
                // Prioridad: 1280x720, 1920x1080, luego la más grande disponible
                if ((cap.FrameSize.Width >= 1280 && cap.FrameSize.Height >= 720) ||
                    (bestCap == null || cap.FrameSize.Width * cap.FrameSize.Height > bestCap.FrameSize.Width * bestCap.FrameSize.Height))
                {
                    bestCap = cap;
                }
            }

            if (bestCap != null)
                _videoSource.VideoResolution = bestCap;

            _videoSource.NewFrame += OnNewFrame;
            _videoSource.Start();
            _frameTimer.Restart();
            StatusLabel.Text = $"✓ Cámara activa ({_videoSource.VideoResolution.FrameSize.Width}x{_videoSource.VideoResolution.FrameSize.Height}). Inicia el juego.";
        }

        private void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                try
                {
                    _videoSource.NewFrame -= OnNewFrame;
                    _videoSource.SignalToStop();
                    // Esperar a que se detenga completamente (máximo 2 segundos)
                    for (int i = 0; i < 20 && _videoSource.IsRunning; i++)
                        System.Threading.Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deteniendo cámara: {ex.Message}");
                }
                finally
                {
                    _videoSource = null;
                }
            }
        }

        // ── Captura ──────────────────────────────────────────────
        private void OnNewFrame(object sender, NewFrameEventArgs e)
        {
            try
            {
                Bitmap clone = (Bitmap)e.Frame.Clone();
                clone.RotateFlip(RotateFlipType.RotateNoneFlipX);

                lock (_frameLock)
                {
                    _lastFrame?.Dispose();
                    _lastFrame = clone;
                }

                if (_processingFrame) return;
                _processingFrame = true;
                Task.Run(() =>
                {
                    try { ProcessFrame(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error en ProcessFrame: {ex.Message}");
                    }
                    finally { _processingFrame = false; }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en OnNewFrame: {ex.Message}");
            }
        }

        private void ProcessFrame()
        {
            Bitmap frame = null;
            try
            {
                lock (_frameLock)
                {
                    if (_lastFrame == null) return;
                    frame = (Bitmap)_lastFrame.Clone();
                }

                double delta = _frameTimer.Elapsed.TotalSeconds;
                _frameTimer.Restart();

                SkinDetector.BodyPoints pts = _skinDetector.Detect(frame);

                // Manejar calibración
                if (_isCalibrating)
                {
                    _calibrationFrameCount++;
                    if (_calibrationFrameCount == 1)
                        _calibrationReference = pts;
                    else if (_calibrationFrameCount > 1)
                    {
                        // Promediar puntos durante calibración
                        _calibrationReference.Head = AveragePoint(_calibrationReference.Head, pts.Head);
                        _calibrationReference.ShoulderL = AveragePoint(_calibrationReference.ShoulderL, pts.ShoulderL);
                        _calibrationReference.ShoulderR = AveragePoint(_calibrationReference.ShoulderR, pts.ShoulderR);
                        _calibrationReference.HandL = AveragePoint(_calibrationReference.HandL, pts.HandL);
                        _calibrationReference.HandR = AveragePoint(_calibrationReference.HandR, pts.HandR);
                        _calibrationReference.HipCenter = AveragePoint(_calibrationReference.HipCenter, pts.HipCenter);
                    }

                    if (_calibrationFrameCount >= 30) // 1 segundo aprox
                    {
                        _isCalibrating = false;
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            StatusLabel.Text = "✓ Calibración completada. Ya puedes jugar.";
                            BtnCalibrate.IsEnabled = true;
                        }));
                    }
                }

                // Aplicar calibración a puntos detectados
                if (_calibrationReference != null && pts.IsValid && !_isCalibrating)
                    pts = ApplyCalibration(pts);

                double accuracy = 0;

                if (_gameEngine.State == GameState.Playing)
                    accuracy = pts.IsValid
                        ? _gameEngine.UpdateFrame(pts, delta)
                        : _gameEngine.LastAccuracy;

                DrawOverlay(frame, pts, accuracy);

                BitmapSource bmpSrc = ToBitmapSource(frame);
                bmpSrc.Freeze();

                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        CameraFeed.Source = bmpSrc;
                        UpdateUI(accuracy);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error actualizando UI: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error procesando frame: {ex.Message}");
                StatusLabel.Text = $"⚠ Error: {ex.Message}";
            }
            finally
            {
                frame?.Dispose();
            }
        }

        // ── Dibujo skeleton ──────────────────────────────────────
        private void DrawOverlay(Bitmap bmp, SkinDetector.BodyPoints pts, double accuracy)
        {
            if (!pts.IsValid) return;

            int W = bmp.Width, H = bmp.Height;
            DrawingPen pen = accuracy > 72 ? GoodPen : SkeletonPen;

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                PointF head = Denorm(pts.Head, W, H);
                PointF sL = Denorm(pts.ShoulderL, W, H);
                PointF sR = Denorm(pts.ShoulderR, W, H);
                PointF hL = Denorm(pts.HandL, W, H);
                PointF hR = Denorm(pts.HandR, W, H);
                PointF hip = Denorm(pts.HipCenter, W, H);

                g.DrawLine(pen, head, sL);
                g.DrawLine(pen, head, sR);
                g.DrawLine(pen, sL, sR);
                g.DrawLine(pen, sL, hL);
                g.DrawLine(pen, sR, hR);
                g.DrawLine(pen, sL, hip);
                g.DrawLine(pen, sR, hip);

                DrawDot(g, head, 14, HeadBrush);
                DrawDot(g, sL, 10, ShoulderBrush);
                DrawDot(g, sR, 10, ShoulderBrush);
                DrawDot(g, hL, 12, HandBrush);
                DrawDot(g, hR, 12, HandBrush);
                DrawDot(g, hip, 10, HipBrush);

                // Texto de precisión
                DrawingColor textColor = accuracy > 72
                    ? DrawingColor.LimeGreen
                    : accuracy > 50
                        ? DrawingColor.Gold
                        : DrawingColor.OrangeRed;

                using (Font font = new Font("Segoe UI", 22, System.Drawing.FontStyle.Bold))
                using (System.Drawing.SolidBrush shadowBr = new System.Drawing.SolidBrush(DrawingColor.FromArgb(120, 0, 0, 0)))
                using (System.Drawing.SolidBrush textBr = new System.Drawing.SolidBrush(textColor))
                {
                    string accTxt = string.Format("{0:F0}%", accuracy);
                    g.DrawString(accTxt, font, shadowBr, W - 102, H - 48);
                    g.DrawString(accTxt, font, textBr, W - 104, H - 50);
                }
            }
        }

        private void DrawDot(Graphics g, PointF center, int r, DrawingBrush fill)
        {
            g.FillEllipse(fill, center.X - r, center.Y - r, r * 2, r * 2);
            g.DrawEllipse(WhitePen, center.X - r, center.Y - r, r * 2, r * 2);
        }

        private static PointF Denorm(PointF p, int W, int H)
            => new PointF(p.X * W, p.Y * H);

        // ── Bitmap → BitmapSource ────────────────────────────────
        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            DrawingRect rect = new DrawingRect(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, DrawingPF.Format32bppArgb);
            try
            {
                return BitmapSource.Create(
                    bmp.Width, bmp.Height,
                    96, 96,
                    WpfPF.Bgra32,
                    null,
                    data.Scan0,
                    data.Stride * bmp.Height,
                    data.Stride);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        // ── UI ───────────────────────────────────────────────────
        private void UpdateUI(double accuracy)
        {
            ScoreLabel.Text = _gameEngine.Score.ToString();
            RoundLabel.Text = string.Format("Ronda {0}", _gameEngine.Round);

            Heart1.Foreground = _gameEngine.Lives >= 1 ? WpfBrushes.HotPink : WpfBrushes.DimGray;
            Heart2.Foreground = _gameEngine.Lives >= 2 ? WpfBrushes.HotPink : WpfBrushes.DimGray;
            Heart3.Foreground = _gameEngine.Lives >= 3 ? WpfBrushes.HotPink : WpfBrushes.DimGray;

            // Timer bar
            FrameworkElement timerParent = (FrameworkElement)TimerBar.Parent;
            double timerWidth = _gameEngine.State == GameState.Playing
                ? Math.Max(0, timerParent.ActualWidth * (_gameEngine.TimeLeft / 6.5))
                : 0;
            TimerBar.Width = timerWidth;
            TimerBar.Background = _gameEngine.TimeLeft > 2.5
                ? new SolidColorBrush(WpfColor.FromRgb(123, 97, 255))
                : new SolidColorBrush(WpfColor.FromRgb(255, 75, 106));

            // Accuracy
            AccuracyBar.Value = accuracy;
            AccuracyLabel.Text = string.Format("{0:F0}%", accuracy);
            AccuracyBar.Foreground = accuracy > 72
                ? new SolidColorBrush(WpfColor.FromRgb(0, 200, 83))
                : accuracy > 50
                    ? new SolidColorBrush(WpfColor.FromRgb(255, 193, 7))
                    : new SolidColorBrush(WpfColor.FromRgb(123, 97, 255));

            // Overlays
            if (_gameEngine.State == GameState.PoseSuccess)
            {
                SuccessScore.Text = string.Format("Score: {0}", _gameEngine.Score);
                SuccessOverlay.Visibility = Visibility.Visible;
                GameOverOverlay.Visibility = Visibility.Collapsed;

                DispatcherTimer t = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    SuccessOverlay.Visibility = Visibility.Collapsed;
                    _gameEngine.NextRound();
                    DrawRefPose(_gameEngine.CurrentTarget);
                    PoseName.Text = _gameEngine.CurrentTarget.Name;
                };
                t.Start();
            }
            else if (_gameEngine.State == GameState.GameOver)
            {
                FinalScore.Text = string.Format("Puntuación final: {0}", _gameEngine.Score);
                GameOverOverlay.Visibility = Visibility.Visible;
                SuccessOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                SuccessOverlay.Visibility = Visibility.Collapsed;
                GameOverOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // ── Pose referencial ─────────────────────────────────────
        private void DrawRefPose(PoseSnapshot pose)
        {
            RefPoseCanvas.Children.Clear();
            double cw = 180, ch = 170;

            string[,] bones =
            {
                { "left_shoulder",  "right_shoulder" },
                { "left_shoulder",  "left_elbow"     },
                { "left_elbow",     "left_wrist"     },
                { "right_shoulder", "right_elbow"    },
                { "right_elbow",    "right_wrist"    },
                { "left_shoulder",  "left_hip"       },
                { "right_shoulder", "right_hip"      },
                { "left_hip",       "right_hip"      },
                { "nose",           "left_shoulder"  },
                { "nose",           "right_shoulder" }
            };

            for (int i = 0; i < bones.GetLength(0); i++)
            {
                string a = bones[i, 0];
                string b = bones[i, 1];
                if (!pose.Keypoints.ContainsKey(a) || !pose.Keypoints.ContainsKey(b)) continue;

                var kpA = pose.Keypoints[a];
                var kpB = pose.Keypoints[b];

                Line line = new Line
                {
                    X1 = kpA.X * cw,
                    Y1 = kpA.Y * ch,
                    X2 = kpB.X * cw,
                    Y2 = kpB.Y * ch,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(123, 97, 255)),
                    StrokeThickness = 3
                };
                RefPoseCanvas.Children.Add(line);
            }

            foreach (KeyValuePair<string, (double X, double Y)> kvp in pose.Keypoints)
            {
                string name = kvp.Key;
                double px = kvp.Value.X;
                double py = kvp.Value.Y;

                WpfColor c = name == "nose"
                    ? WpfColor.FromRgb(255, 193, 7)
                    : (name.Contains("wrist") || name.Contains("elbow"))
                        ? WpfColor.FromRgb(33, 150, 243)
                        : name.Contains("hip")
                            ? WpfColor.FromRgb(156, 39, 176)
                            : WpfColor.FromRgb(255, 255, 255);

                Ellipse dot = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(c),
                    Stroke = WpfBrushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(dot, px * cw - 7);
                Canvas.SetTop(dot, py * ch - 7);
                RefPoseCanvas.Children.Add(dot);
            }
        }

        // ── Botones ──────────────────────────────────────────────
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _gameEngine.StartGame();
            DrawRefPose(_gameEngine.CurrentTarget);
            PoseName.Text = _gameEngine.CurrentTarget.Name;
            SuccessOverlay.Visibility = Visibility.Collapsed;
            GameOverOverlay.Visibility = Visibility.Collapsed;
            StatusLabel.Text = "¡Imita la pose!";
        }

        private void BtnCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (_videoSource == null || !_videoSource.IsRunning)
            {
                StatusLabel.Text = "⚠ Activa la cámara primero";
                return;
            }

            _isCalibrating = true;
            _calibrationFrameCount = 0;
            StatusLabel.Text = "📏 Ponte en posición neutra con brazos relajados...";
            BtnCalibrate.IsEnabled = false;
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
            => BtnStart_Click(sender, e);

        // ── Calibración ──────────────────────────────────────────
        private static PointF AveragePoint(PointF p1, PointF p2)
        {
            return new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f);
        }

        private SkinDetector.BodyPoints ApplyCalibration(SkinDetector.BodyPoints detected)
        {
            if (_calibrationReference == null) return detected;

            // Calcular offset entre calibración y detección
            float headOffsetX = detected.Head.X - _calibrationReference.Head.X;
            float headOffsetY = detected.Head.Y - _calibrationReference.Head.Y;

            // Aplicar ese offset a todos los puntos para normalizar
            var result = new SkinDetector.BodyPoints
            {
                Head = detected.Head,
                ShoulderL = new PointF(detected.ShoulderL.X - headOffsetX * 0.3f, detected.ShoulderL.Y - headOffsetY * 0.3f),
                ShoulderR = new PointF(detected.ShoulderR.X - headOffsetX * 0.3f, detected.ShoulderR.Y - headOffsetY * 0.3f),
                HandL = new PointF(detected.HandL.X - headOffsetX * 0.5f, detected.HandL.Y - headOffsetY * 0.5f),
                HandR = new PointF(detected.HandR.X - headOffsetX * 0.5f, detected.HandR.Y - headOffsetY * 0.5f),
                HipCenter = new PointF(detected.HipCenter.X - headOffsetX * 0.2f, detected.HipCenter.Y - headOffsetY * 0.2f),
                IsValid = detected.IsValid
            };

            return result;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
            SkeletonPen.Dispose();
            GoodPen.Dispose();
            HeadBrush.Dispose();
            HandBrush.Dispose();
            HipBrush.Dispose();
            ShoulderBrush.Dispose();
            WhitePen.Dispose();
        }
    }
}