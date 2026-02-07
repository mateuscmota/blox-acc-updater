using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace RBX_Alt_Manager.Classes
{
    public class VideoWallpaperService : IDisposable
    {
        private static VideoWallpaperService _instance;
        public static VideoWallpaperService Instance => _instance ?? (_instance = new VideoWallpaperService());

        private Thread _wpfThread;
        private Dispatcher _dispatcher;
        private MediaPlayer _player;
        private DispatcherTimer _frameTimer;
        private Form _targetForm;
        private string _currentVideoPath;
        private int _overlayAlpha;
        private System.Drawing.Color _overlayColor;
        private volatile bool _isRunning;
        private volatile int _formWidth, _formHeight;

        // Frame thread-safe
        private readonly object _frameLock = new object();
        private Bitmap _currentFrame;

        // Resolução reduzida para performance
        private const int MaxRenderWidth = 960;

        private VideoWallpaperService() { }

        public bool IsPlaying => _isRunning;
        public string CurrentVideoPath => _currentVideoPath;

        /// <summary>
        /// Desenha o frame atual diretamente no Graphics, com lock thread-safe.
        /// Retorna true se desenhou, false se não há frame.
        /// </summary>
        public bool DrawFrame(Graphics g, Rectangle destRect)
        {
            lock (_frameLock)
            {
                if (_currentFrame == null) return false;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                g.DrawImage(_currentFrame, destRect);
                return true;
            }
        }

        public void Start(Form targetForm, string videoPath, System.Drawing.Color overlayColor, int overlayOpacity)
        {
            if (_isRunning && _currentVideoPath == videoPath)
            {
                UpdateOverlay(overlayColor, overlayOpacity);
                return;
            }

            Stop();

            _targetForm = targetForm;
            _currentVideoPath = videoPath;
            _overlayColor = overlayColor;
            _overlayAlpha = (int)(overlayOpacity * 2.55f);
            _formWidth = targetForm.ClientSize.Width;
            _formHeight = targetForm.ClientSize.Height;
            _isRunning = true;

            // Atualizar tamanho quando form redimensiona
            targetForm.Resize += OnFormResize;

            _wpfThread = new Thread(WpfThreadProc);
            _wpfThread.SetApartmentState(ApartmentState.STA);
            _wpfThread.IsBackground = true;
            _wpfThread.Name = "VideoWallpaper";
            _wpfThread.Start();
        }

        private void OnFormResize(object sender, EventArgs e)
        {
            if (_targetForm != null && !_targetForm.IsDisposed)
            {
                _formWidth = _targetForm.ClientSize.Width;
                _formHeight = _targetForm.ClientSize.Height;
            }
        }

        private void WpfThreadProc()
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;

                _player = new MediaPlayer();
                _player.Volume = 0;
                _player.MediaEnded += (s, e) =>
                {
                    _player.Position = TimeSpan.Zero;
                    _player.Play();
                };
                _player.Open(new Uri(_currentVideoPath));
                _player.Play();

                Thread.Sleep(500);

                _frameTimer = new DispatcherTimer(DispatcherPriority.Background);
                _frameTimer.Interval = TimeSpan.FromMilliseconds(100); // 10fps
                _frameTimer.Tick += CaptureFrame;
                _frameTimer.Start();

                Dispatcher.Run();
            }
            catch
            {
                _isRunning = false;
            }
        }

        private void CaptureFrame(object sender, EventArgs e)
        {
            if (!_isRunning || _targetForm == null || _targetForm.IsDisposed)
            {
                _frameTimer?.Stop();
                return;
            }

            try
            {
                // Ler tamanho cacheado (sem cross-thread Invoke)
                int fw = _formWidth;
                int fh = _formHeight;
                if (fw <= 0 || fh <= 0) return;

                // Renderizar em resolução reduzida para performance
                float ratio = Math.Min(1.0f, (float)MaxRenderWidth / fw);
                int w = (int)(fw * ratio);
                int h = (int)(fh * ratio);

                int vw = _player.NaturalVideoWidth;
                int vh = _player.NaturalVideoHeight;

                var drawingVisual = new DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    if (vw > 0 && vh > 0)
                    {
                        float scale = Math.Max((float)w / vw, (float)h / vh);
                        int sw = (int)(vw * scale);
                        int sh = (int)(vh * scale);
                        int sx = (w - sw) / 2;
                        int sy = (h - sh) / 2;
                        dc.DrawVideo(_player, new System.Windows.Rect(sx, sy, sw, sh));
                    }
                    else
                    {
                        dc.DrawVideo(_player, new System.Windows.Rect(0, 0, w, h));
                    }
                }

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);

                var frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                var bitmapData = frame.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppPArgb);

                rtb.CopyPixels(
                    new System.Windows.Int32Rect(0, 0, w, h),
                    bitmapData.Scan0,
                    bitmapData.Stride * h,
                    bitmapData.Stride);

                frame.UnlockBits(bitmapData);

                // Overlay semi-transparente
                if (_overlayAlpha > 0)
                {
                    using (var g = Graphics.FromImage(frame))
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(_overlayAlpha, _overlayColor)))
                    {
                        g.FillRectangle(brush, 0, 0, w, h);
                    }
                }

                // Trocar frame sob lock
                Bitmap oldFrame;
                lock (_frameLock)
                {
                    oldFrame = _currentFrame;
                    _currentFrame = frame;
                }

                // Dispose do frame antigo + repaint via BeginInvoke na UI thread
                // Garante que o frame antigo não é descartado durante OnPaintBackground
                if (_targetForm != null && !_targetForm.IsDisposed)
                {
                    _targetForm.BeginInvoke((Action)(() =>
                    {
                        oldFrame?.Dispose();
                        if (_targetForm != null && !_targetForm.IsDisposed)
                            _targetForm.Invalidate(false);
                    }));
                }
                else
                {
                    oldFrame?.Dispose();
                }
            }
            catch { }
        }

        public void UpdateOverlay(System.Drawing.Color color, int opacity)
        {
            _overlayColor = color;
            _overlayAlpha = (int)(opacity * 2.55f);
        }

        public void Stop()
        {
            _isRunning = false;

            if (_targetForm != null)
            {
                try { _targetForm.Resize -= OnFormResize; } catch { }
            }

            if (_dispatcher != null)
            {
                try
                {
                    _dispatcher.BeginInvoke((Action)(() =>
                    {
                        _frameTimer?.Stop();
                        _player?.Stop();
                        _player?.Close();
                        _dispatcher.InvokeShutdown();
                    }));

                    _wpfThread?.Join(2000);
                }
                catch { }
            }

            lock (_frameLock)
            {
                _currentFrame?.Dispose();
                _currentFrame = null;
            }

            _wpfThread = null;
            _dispatcher = null;
            _player = null;
            _frameTimer = null;
            _currentVideoPath = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
