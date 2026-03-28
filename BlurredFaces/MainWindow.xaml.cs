using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BlurredFaces
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds = 20;

        private readonly List<ImageItem> _images = new();
        private int _currentIndex = 0;

        // Anzahl der Stufen: 20 Sekunden => 20 Stufen (jede Sekunde eine Stufe)
        private const int MaxLevel = 20;

        // Basis-Maximum für Blockgröße, abhängig von geladenem Bild
        private int _baseMaxBlock = 48;

        // Original geladenes Bild der aktuellen Position
        private BitmapSource? _originalBitmap;

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadImages();
            if (_images.Count > 0)
            {
                ShuffleImages();
                _currentIndex = 0;
                ShowCurrentImage();
            }
            else
            {
                MessageBox.Show("Keine Bilder gefunden in 'Data/fotos'.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateCountLabel();
            }

            UpdateTimerDisplay();
            _timer.Start();
        }

        private void LoadImages()
        {
            _images.Clear();

            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "fotos");

            if (!Directory.Exists(baseDir))
            {
                string alt = Path.Combine(Directory.GetCurrentDirectory(), "Data", "fotos");
                if (Directory.Exists(alt))
                {
                    baseDir = alt;
                }
                else
                {
                    return;
                }
            }

            var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

            foreach (var file in Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories))
            {
                if (allowedExt.Contains(Path.GetExtension(file)))
                {
                    var folderName = new DirectoryInfo(Path.GetDirectoryName(file) ?? baseDir).Name;
                    _images.Add(new ImageItem { FilePath = file, FolderName = folderName });
                }
            }
        }

        private void ShuffleImages()
        {
            var rnd = new Random();
            for (int i = _images.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = _images[i];
                _images[i] = _images[j];
                _images[j] = tmp;
            }
        }

        private void ShowCurrentImage()
        {
            if (_images.Count == 0)
            {
                _originalBitmap = null;
                MainImage.Source = null;
                UpdateCountLabel();
                return;
            }

            var item = _images[_currentIndex];
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                bi.EndInit();

                // Speichere Original als BitmapSource für Pixelation
                _originalBitmap = bi;

                // Berechne basis MaxBlock abhängig von Bildgröße
                int maxDim = Math.Max(_originalBitmap.PixelWidth, _originalBitmap.PixelHeight);
                _baseMaxBlock = Math.Clamp(maxDim / 20, 4, 400);

                // Setze initial pixelierte Variante (UpdatePixelation berücksichtigt Timer)
                UpdatePixelation(force: true);
            }
            catch
            {
                MainImage.Source = null;
                _originalBitmap = null;
            }

            DescriptionLabel.Content = "???";
            UpdateCountLabel();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                UpdateTimerDisplay();

                if (_remainingSeconds == 0)
                {
                    HandleTimerZero();
                }
            }
        }

        private void HandleTimerZero()
        {
            if (_images.Count > 0)
            {
                DescriptionLabel.Content = _images[_currentIndex].FolderName;
            }
            else
            {
                DescriptionLabel.Content = "Name";
            }

            // bei 0: Original scharf anzeigen
            UpdatePixelation(force: true);
            _timer.Stop();
        }

        private void UpdateTimerDisplay()
        {
            TimerTextBlock.Text = TimeSpan.FromSeconds(_remainingSeconds).ToString(@"mm\:ss");

            // Update nur alle 5 Sekunden (oder bei 0) um Rechenlast zu verringern
            if (_remainingSeconds == 0 || _remainingSeconds % 2 == 0)
            {
                UpdatePixelation();
            }
        }

        private void UpdatePixelation(bool force = false)
        {
            if (_originalBitmap == null)
            {
                MainImage.Source = null;
                return;
            }

            // Level entspricht verbleibenden Sekunden (20..1). Bei 0: kein Pixeln (original).
            int level = _remainingSeconds <= 0 ? 0 : _remainingSeconds; // 20..1
            int blockSize = level == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(_baseMaxBlock * level / (double)MaxLevel));

            if (blockSize <= 1)
            {
                // Keine Pixelation — Original anzeigen
                MainImage.Source = _originalBitmap;
                return;
            }

            try
            {
                var pixelated = CreatePixelatedBitmap(_originalBitmap, blockSize);
                MainImage.Source = pixelated;
            }
            catch
            {
                MainImage.Source = _originalBitmap;
            }
        }

        private BitmapSource CreatePixelatedBitmap(BitmapSource source, int blockSize)
        {
            var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = conv.PixelWidth;
            int height = conv.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            conv.CopyPixels(pixels, stride, 0);

            byte[] outPixels = new byte[pixels.Length];
            Array.Copy(pixels, outPixels, pixels.Length);

            for (int by = 0; by < height; by += blockSize)
            {
                for (int bx = 0; bx < width; bx += blockSize)
                {
                    long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    int count = 0;

                    int maxY = Math.Min(by + blockSize, height);
                    int maxX = Math.Min(bx + blockSize, width);

                    for (int y = by; y < maxY; y++)
                    {
                        int row = y * stride;
                        for (int x = bx; x < maxX; x++)
                        {
                            int idx = row + x * 4;
                            sumB += pixels[idx + 0];
                            sumG += pixels[idx + 1];
                            sumR += pixels[idx + 2];
                            sumA += pixels[idx + 3];
                            count++;
                        }
                    }

                    if (count == 0) continue;

                    byte avgB = (byte)(sumB / count);
                    byte avgG = (byte)(sumG / count);
                    byte avgR = (byte)(sumR / count);
                    byte avgA = (byte)(sumA / count);

                    for (int y = by; y < maxY; y++)
                    {
                        int row = y * stride;
                        for (int x = bx; x < maxX; x++)
                        {
                            int idx = row + x * 4;
                            outPixels[idx + 0] = avgB;
                            outPixels[idx + 1] = avgG;
                            outPixels[idx + 2] = avgR;
                            outPixels[idx + 3] = avgA;
                        }
                    }
                }
            }

            var wb = new WriteableBitmap(width, height, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, width, height), outPixels, stride, 0);
            wb.Freeze();
            return wb;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + _images.Count) % _images.Count;
            ShowCurrentImage();
            ResetTimer();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _images.Count;
            ShowCurrentImage();
            ResetTimer();
        }

        private void RevealButton_Click(object sender, RoutedEventArgs e)
        {
            _remainingSeconds = 0;
            UpdateTimerDisplay();
            HandleTimerZero();
        }

        private void DecreaseButton_Click(object sender, RoutedEventArgs e)
        {
            _remainingSeconds = Math.Max(0, _remainingSeconds - 5);
            UpdateTimerDisplay();

            // Sofort Pixelation aktualisieren (z. B. bei nicht-5-Sekunden-Grenzen)
            UpdatePixelation(force: true);

            if (_remainingSeconds == 0)
            {
                HandleTimerZero();
            }
            else
            {
                if (!_timer.IsEnabled) _timer.Start();
            }
        }

        private void IncreaseButton_Click(object sender, RoutedEventArgs e)
        {
            _remainingSeconds += 5;
            UpdateTimerDisplay();

            // Sofort Pixelation aktualisieren
            UpdatePixelation(force: true);

            if (!_timer.IsEnabled && _remainingSeconds > 0)
            {
                _timer.Start();
            }
        }

        private void ResetTimer()
        {
            _remainingSeconds = 20;
            UpdateTimerDisplay();
            _timer.Start();
        }

        private void UpdateCountLabel()
        {
            if (CountLabel == null) return;
            CountLabel.Content = _images.Count == 0 ? "0 von 0" : $"{_currentIndex + 1} von {_images.Count}";
        }

        private class ImageItem
        {
            public string FilePath { get; set; } = string.Empty;
            public string FolderName { get; set; } = string.Empty;
        }
    }
}