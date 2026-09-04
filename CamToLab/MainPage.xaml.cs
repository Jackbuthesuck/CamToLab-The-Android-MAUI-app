using SkiaSharp;
using CommunityToolkit.Maui.Core;

namespace MauiJohnWick1
{
    public partial class MainPage : ContentPage
    {
        private const double MinimumSelectionSize = 24;
        private const double DefaultSelectionSize = 72;
        private const string WhiteStandardPrefix = "WhiteStandard.";
        private byte[]? _imageBytes;
        private string? _capturedImagePath;
        private SKBitmap? _bitmap;
        private bool _samplePending;
        private bool _sampleTimerRunning;
        private bool _sampleInProgress;
        private bool _singlePixelMode = true;
        private ColorSample? _lastSample;
        private ColorSample? _whiteStandard;
        private ColorSample? _comparisonA;
        private ColorSample? _comparisonB;
        private Point? _comparisonAPosition;
        private Point? _comparisonBPosition;
        private double _left = 0;
        private double _top = 0;
        private double _selectionWidth = 140;
        private double _selectionHeight = 140;
        private double _startLeft;
        private double _startTop;
        private SamplingMode Mode => _singlePixelMode ? SamplingMode.SinglePixel : SamplingMode.AverageArea;

        public MainPage()
        {
            InitializeComponent();
            UpdateModeVisual();
            LoadWhiteStandard();
        }

        private async void OnTakePhotoClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_imageBytes is not null)
                {
                    ReleaseCapturedImage();
                    await CameraPreview.StartCameraPreview(CancellationToken.None);
                    return;
                }

                if (CameraPreview.IsCameraBusy)
                    return;

                await CameraPreview.CaptureImage(CancellationToken.None);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Camera error", ex.Message, "OK");
            }
        }

        private async void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
        {
            try
            {
                using var memory = new MemoryStream();
                await e.Media.CopyToAsync(memory);
                _imageBytes = memory.ToArray();
                _bitmap?.Dispose();
                _bitmap = ColorSampler.Decode(_imageBytes);
                _lastSample = null;
                _comparisonA = null;
                _comparisonB = null;
                _comparisonAPosition = null;
                _comparisonBPosition = null;
                ComparisonAMarker.IsVisible = false;
                ComparisonBMarker.IsVisible = false;
                if (_capturedImagePath is not null && File.Exists(_capturedImagePath))
                    File.Delete(_capturedImagePath);

                string capturedImagePath = Path.Combine(FileSystem.CacheDirectory, $"captured-frame-{Guid.NewGuid():N}.png");
                using (SKImage decodedImage = SKImage.FromBitmap(_bitmap))
                using (SKData encodedImage = decodedImage.Encode(SKEncodedImageFormat.Png, 100))
                    await File.WriteAllBytesAsync(capturedImagePath, encodedImage.ToArray());
                _capturedImagePath = capturedImagePath;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CameraPreview.IsVisible = false;
                    CapturedImage.ZIndex = 1;
                    CapturedImage.Source = null;
                    CapturedImage.Source = ImageSource.FromFile(capturedImagePath);
                    CapturedImage.IsVisible = true;
                    SelectionBox.IsVisible = true;
                    TakePhotoButton.Text = "Release Image";
                PixelCountValueLabel.Text = "Drag to move or resize with slider";
                    ResetSelection();

                    Dispatcher.StartTimer(TimeSpan.FromMilliseconds(250), () =>
                    {
                        CameraPreview.StopCameraPreview();
                        PhotoCanvas.Children.Remove(CameraPreview);
                        return false;
                    });
                });
            }

            catch (Exception ex)
            {
                PixelCountValueLabel.Text = ex.Message;
            }
        }

        private void ReleaseCapturedImage()
        {
            CameraPreview.StopCameraPreview();
            if (!PhotoCanvas.Children.Contains(CameraPreview))
                PhotoCanvas.Children.Insert(0, CameraPreview);

            CameraPreview.IsVisible = true;
            CapturedImage.Source = null;
            CapturedImage.IsVisible = false;
            SelectionBox.IsVisible = false;
            TakePhotoButton.Text = "Take Photo";
            _bitmap?.Dispose();
            _bitmap = null;
            _imageBytes = null;
            _lastSample = null;
            _comparisonA = null;
            _comparisonB = null;
            _comparisonAPosition = null;
            _comparisonBPosition = null;
            ComparisonAMarker.IsVisible = false;
            ComparisonBMarker.IsVisible = false;
            _samplePending = false;
            if (_capturedImagePath is not null && File.Exists(_capturedImagePath))
                File.Delete(_capturedImagePath);
            _capturedImagePath = null;
        }

        private async void OnPageAppearing(object? sender, EventArgs e)
        {
            if (_imageBytes is not null)
                return;

            try
            {
                PermissionStatus permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (permission != PermissionStatus.Granted)
                    permission = await Permissions.RequestAsync<Permissions.Camera>();

                if (permission == PermissionStatus.Granted)
                    await CameraPreview.StartCameraPreview(CancellationToken.None);
                else
                    PixelCountValueLabel.Text = "Camera permission is required";
            }
            catch (Exception ex)
            {
                PixelCountValueLabel.Text = $"Camera unavailable: {ex.Message}";
            }
        }

        private void OnPageDisappearing(object? sender, EventArgs e)
        {
            CameraPreview.StopCameraPreview();
        }

        private void OnPhotoCanvasSizeChanged(object? sender, EventArgs e)
        {
            if (CapturedImage.IsVisible)
            {
                ConstrainSelection();
                UpdateSelectionLayout();
                UpdateSample();
            }
        }

        private void OnSetWhiteStandardClicked(object? sender, EventArgs e)
        {
            if (_lastSample is null)
            {
                WhiteStandardValueLabel.Text = "Sample an image first";
                return;
            }

            _whiteStandard = _lastSample;
            Preferences.Set($"{WhiteStandardPrefix}Red", _whiteStandard.Red);
            Preferences.Set($"{WhiteStandardPrefix}Green", _whiteStandard.Green);
            Preferences.Set($"{WhiteStandardPrefix}Blue", _whiteStandard.Blue);
            Preferences.Set($"{WhiteStandardPrefix}Lightness", _whiteStandard.Lightness);
            Preferences.Set($"{WhiteStandardPrefix}A", _whiteStandard.A);
            Preferences.Set($"{WhiteStandardPrefix}B", _whiteStandard.B);
            WhiteStandardValueLabel.Text = $"RGB {_whiteStandard.Red}, {_whiteStandard.Green}, {_whiteStandard.Blue}";
            ColorSample calibratedStandard = ColorSampler.ApplyWhiteStandard(_whiteStandard, _whiteStandard);
            _lastSample = calibratedStandard;
            RgbValueLabel.Text = $"{calibratedStandard.Red}, {calibratedStandard.Green}, {calibratedStandard.Blue}";
            UpdateLabDisplay(calibratedStandard);
        }

        private void OnSetComparisonAClicked(object? sender, EventArgs e)
        {
            if (_lastSample is null)
            {
                ComparisonValueLabel.Text = "Sample an image before setting A";
                return;
            }

            _comparisonA = _lastSample;
            _comparisonAPosition = GetSelectionCenter();
            SetComparisonMarker(ComparisonAMarker, _comparisonAPosition.Value);
            UpdateComparisonDisplay();
        }

        private void OnSetComparisonBClicked(object? sender, EventArgs e)
        {
            if (_lastSample is null)
            {
                ComparisonValueLabel.Text = "Sample an image before setting B";
                return;
            }

            _comparisonB = _lastSample;
            _comparisonBPosition = GetSelectionCenter();
            SetComparisonMarker(ComparisonBMarker, _comparisonBPosition.Value);
            UpdateComparisonDisplay();
        }

        private void UpdateComparisonDisplay()
        {
            if (_comparisonA is null && _comparisonB is null)
            {
                ComparisonValueLabel.Text = "Set A and Set B to compare";
                return;
            }

            string aToB = _comparisonA is not null && _comparisonB is not null
                ? $"A→B: {ColorSampler.DeltaE2000(_comparisonA, _comparisonB):F2}"
                : "A→B: —";
            string aToCursor = _comparisonA is not null && _lastSample is not null
                ? $"A→cursor: {ColorSampler.DeltaE2000(_comparisonA, _lastSample):F2}"
                : "A→cursor: —";
            ComparisonValueLabel.Text = $"{aToB} | {aToCursor}";
        }

        private void LoadWhiteStandard()
        {
            if (!Preferences.ContainsKey($"{WhiteStandardPrefix}Red"))
                return;

            _whiteStandard = new ColorSample(
                Preferences.Get($"{WhiteStandardPrefix}Red", 255),
                Preferences.Get($"{WhiteStandardPrefix}Green", 255),
                Preferences.Get($"{WhiteStandardPrefix}Blue", 255),
                Preferences.Get($"{WhiteStandardPrefix}Lightness", 100d),
                Preferences.Get($"{WhiteStandardPrefix}A", 0d),
                Preferences.Get($"{WhiteStandardPrefix}B", 0d),
                1);
            WhiteStandardValueLabel.Text = $"RGB {_whiteStandard.Red}, {_whiteStandard.Green}, {_whiteStandard.Blue}";
        }

        private void OnSinglePixelClicked(object? sender, EventArgs e)
        {
            _singlePixelMode = true;
            UpdateModeVisual();
            if (_imageBytes is not null)
                UpdateSample();
        }

        private void OnAverageAreaClicked(object? sender, EventArgs e)
        {
            _singlePixelMode = false;
            UpdateModeVisual();
            if (_imageBytes is not null)
                UpdateSample();
        }

        private void OnSelectionSizeChanged(object? sender, ValueChangedEventArgs e)
        {
            if (_imageBytes is null || PhotoCanvas.Width <= 0 || PhotoCanvas.Height <= 0)
                return;

            double centerX = _left + _selectionWidth / 2;
            double centerY = _top + _selectionHeight / 2;
            _selectionWidth = e.NewValue;
            _selectionHeight = e.NewValue;
            SelectionSizeLabel.Text = $"Selection: {(int)Math.Round(e.NewValue)} × {(int)Math.Round(e.NewValue)} px";
            _left = centerX - _selectionWidth / 2;
            _top = centerY - _selectionHeight / 2;
            ConstrainSelection();
            UpdateSelectionLayout();
            RequestSample();
        }

        private void ResetSelection()
        {
            _selectionWidth = Math.Min(DefaultSelectionSize, Math.Max(MinimumSelectionSize, PhotoCanvas.Width * 0.25));
            _selectionHeight = Math.Min(DefaultSelectionSize, Math.Max(MinimumSelectionSize, PhotoCanvas.Height * 0.25));
            SelectionSizeSlider.Value = DefaultSelectionSize;
            SelectionSizeLabel.Text = $"Selection: {(int)DefaultSelectionSize} × {(int)DefaultSelectionSize} px";
            _left = Math.Max(0, (PhotoCanvas.Width - _selectionWidth) / 2);
            _top = Math.Max(0, (PhotoCanvas.Height - _selectionHeight) / 2);
            UpdateSelectionLayout();
            UpdateSample();
        }

        private void OnSelectionPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            if (e.StatusType == GestureStatus.Started)
            {
                SaveSelectionStart();
            }
            else if (e.StatusType == GestureStatus.Running)
            {
                _left = _startLeft + e.TotalX;
                _top = _startTop + e.TotalY;
                ConstrainSelection();
                UpdateSelectionLayout();
                RequestSample();
            }
        }

        private void SaveSelectionStart()
        {
            _startLeft = _left;
            _startTop = _top;
        }

        private void ConstrainSelection()
        {
            _selectionWidth = Math.Clamp(_selectionWidth, MinimumSelectionSize, Math.Max(MinimumSelectionSize, PhotoCanvas.Width));
            _selectionHeight = Math.Clamp(_selectionHeight, MinimumSelectionSize, Math.Max(MinimumSelectionSize, PhotoCanvas.Height));
            _left = Math.Clamp(_left, 0, Math.Max(0, PhotoCanvas.Width - _selectionWidth));
            _top = Math.Clamp(_top, 0, Math.Max(0, PhotoCanvas.Height - _selectionHeight));
        }

        private void UpdateSelectionLayout()
        {
            AbsoluteLayout.SetLayoutBounds(SelectionBox, new Rect(_left, _top, _selectionWidth, _selectionHeight));
        }

        private Point GetSelectionCenter() => new(
            _left + _selectionWidth / 2,
            _top + _selectionHeight / 2);

        private async void UpdateSample()
        {
            if (_bitmap is null || PhotoCanvas.Width <= 0 || PhotoCanvas.Height <= 0 || _sampleInProgress)
                return;

            _sampleInProgress = true;
            try
            {
                SKBitmap bitmap = _bitmap;
                (int imageWidth, int imageHeight) = ColorSampler.GetImageSize(bitmap);
                double scale = Math.Min(PhotoCanvas.Width / imageWidth, PhotoCanvas.Height / imageHeight);
                double displayedWidth = imageWidth * scale;
                double displayedHeight = imageHeight * scale;
                double imageLeft = (PhotoCanvas.Width - displayedWidth) / 2;
                double imageTop = (PhotoCanvas.Height - displayedHeight) / 2;

                double centerX = _left + _selectionWidth / 2;
                double centerY = _top + _selectionHeight / 2;
                SamplingMode mode = Mode;
                ColorSample? whiteStandard = _whiteStandard;
                int width = Math.Max(1, (int)Math.Round(_selectionWidth / scale));
                int height = Math.Max(1, (int)Math.Round(_selectionHeight / scale));
                width = Math.Min(width, imageWidth);
                height = Math.Min(height, imageHeight);

                int centerPixelX = (int)Math.Round((centerX - imageLeft) / scale);
                int centerPixelY = (int)Math.Round((centerY - imageTop) / scale);
                int x = mode == SamplingMode.AverageArea ? centerPixelX - width / 2 : centerPixelX;
                int y = mode == SamplingMode.AverageArea ? centerPixelY - height / 2 : centerPixelY;
                x = Math.Clamp(x, 0, imageWidth - width);
                y = Math.Clamp(y, 0, imageHeight - height);

                ColorSample sample = await Task.Run(() =>
                {
                    ColorSample result = ColorSampler.Sample(bitmap, x, y, width, height, mode);
                    return whiteStandard is null ? result : ColorSampler.ApplyWhiteStandard(result, whiteStandard);
                });
                RgbValueLabel.Text = $"{sample.Red}, {sample.Green}, {sample.Blue}";
                _lastSample = sample;
                UpdateLabDisplay(sample);
                UpdateComparisonDisplay();
                PixelCountValueLabel.Text = mode == SamplingMode.SinglePixel ? "1 pixel at the center" : $"Average of {sample.PixelCount:N0} pixels";
            }

            catch (Exception ex)
            {
                PixelCountValueLabel.Text = ex.Message;
            }
            finally
            {
                _sampleInProgress = false;
            }
        }

        private void UpdateLabDisplay(ColorSample sample)
        {
            LabValueLabel.Text = $"L* {sample.Lightness:F2}, a* {sample.A:F2}, b* {sample.B:F2}";
        }

        private void RequestSample()
        {
            _samplePending = true;
            if (_sampleTimerRunning)
                return;

            _sampleTimerRunning = true;
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(80), () =>
            {
                if (!_samplePending)
                {
                    _sampleTimerRunning = false;
                    return false;
                }

                _samplePending = false;
                UpdateSample();
                return true;
            });
        }

        private void UpdateModeVisual()
        {
            if (AreaSelectionVisual is null || SinglePixelVisual is null)
                return;

            bool singlePixel = Mode == SamplingMode.SinglePixel;
            SinglePixelButton.BackgroundColor = singlePixel ? Colors.DodgerBlue : Colors.LightGray;
            SinglePixelButton.TextColor = singlePixel ? Colors.White : Colors.Black;
            AverageAreaButton.BackgroundColor = singlePixel ? Colors.LightGray : Colors.DodgerBlue;
            AverageAreaButton.TextColor = singlePixel ? Colors.Black : Colors.White;
            AreaSelectionVisual.Opacity = singlePixel ? 0.35 : 1;
            SinglePixelVisual.Opacity = singlePixel ? 1 : 0.35;
        }

        private void SetComparisonMarker(View marker, Point position)
        {
            AbsoluteLayout.SetLayoutBounds(marker, new Rect(
                position.X - 12,
                position.Y - 12,
                24,
                24));
            marker.IsVisible = true;
        }
    }
}
