using SkiaSharp;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui.ApplicationModel;

namespace MauiJohnWick1
{
    public partial class MainPage : ContentPage
    {
        // Selection and display defaults.
        private const double MinimumSelectionSize = 24;
        private const double DefaultSelectionSize = 72;
        private byte[]? _imageBytes;
        private SKBitmap? _bitmap;
        private bool _samplePending;
        private bool _sampleTimerRunning;
        private bool _sampleInProgress;
        private bool _singlePixelMode = true;
        private bool _displayLabValues;
        private ColorSample? _lastSample;
        private ColorSample? _whiteStandard;
        private ColorSample? _comparisonA;
        private ColorSample? _comparisonB;
        private Point? _comparisonAPosition;
        private Point? _comparisonBPosition;
        private bool _comparisonASinglePixel;
        private bool _comparisonBSinglePixel;
        private double _comparisonAWidth;
        private double _comparisonAHeight;
        private double _comparisonBWidth;
        private double _comparisonBHeight;
        private bool _cameraPreviewStarted;
        private double _left = 0;
        private double _top = 0;
        private double _selectionWidth = 140;
        private double _selectionHeight = 140;
        private double _startLeft;
        private double _startTop;
        private SamplingMode Mode => _singlePixelMode ? SamplingMode.SinglePixel : SamplingMode.AverageArea;

        // Page setup and camera lifecycle.
        public MainPage()
        {
            InitializeComponent();
            _displayLabValues = Preferences.Get(AppShell.DisplayLabPreferenceKey, false);
            UpdateDisplayMode();
            UpdateModeVisual();
            ClearWhiteStandard();
        }

        private async void OnTakePhotoClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_imageBytes is not null)
                {
                    ReleaseCapturedImage();
                    await EnsureCameraPermissionAndStartPreviewAsync();
                    return;
                }

                if (!await EnsureCameraPermissionAndStartPreviewAsync())
                    return;

                await CameraPreview.CaptureImage(CancellationToken.None);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Camera error", ex.Message, "OK");
            }
        }

        private async Task<bool> EnsureCameraPermissionAndStartPreviewAsync()
        {
            PermissionStatus permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.Camera>();

            if (permission != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("Camera permission required", "Allow camera access in the app permissions to use the camera.", "OK");
                return false;
            }

            CameraPreview.IsVisible = true;
            if (!_cameraPreviewStarted)
            {
                await CameraPreview.StartCameraPreview(CancellationToken.None);
                _cameraPreviewStarted = true;
            }

            return true;
        }

        public async Task LoadTestCardAsync()
        {
            try
            {
                using Stream stream = await FileSystem.OpenAppPackageFileAsync("testcard.png");
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                await LoadImageAsync(memory.ToArray());
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Test card error", ex.Message, "OK");
            }
        }

        public async Task PickImageAsync()
        {
            try
            {
                IReadOnlyList<FileResult> files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
                {
                    Title = "Select an image"
                });
                FileResult? file = files.FirstOrDefault();

                if (file is null)
                    return;

                using Stream stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                await LoadImageAsync(memory.ToArray());
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Image error", ex.Message, "OK");
            }
        }

        private async void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
        {
            try
            {
                using var memory = new MemoryStream();
                await e.Media.CopyToAsync(memory);
                await LoadImageAsync(memory.ToArray());
            }

            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        // Image loading keeps the original bytes for the UI and a decoded bitmap for sampling.
        private async Task LoadImageAsync(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
            _bitmap?.Dispose();
            _bitmap = ColorSampler.Decode(_imageBytes);
            _lastSample = null;
            _comparisonA = null;
            _comparisonB = null;
            _comparisonAPosition = null;
            _comparisonBPosition = null;
            ComparisonAMarker.IsVisible = false;
            ComparisonBMarker.IsVisible = false;
            WhiteStandardMarker.IsVisible = false;
            UpdateDisplayMode();

            byte[] capturedImageBytes = _imageBytes;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CameraPreview.StopCameraPreview();
                _cameraPreviewStarted = false;
                CameraPreview.IsVisible = false;
                CapturedImage.ZIndex = 1;
                CapturedImage.Source = null;
                CapturedImage.Source = ImageSource.FromStream(() => new MemoryStream(capturedImageBytes, writable: false));
                CapturedImage.IsVisible = true;
                SelectionBox.IsVisible = true;
                TakePhotoButton.Text = "Release Image";
                ResetSelection();
            });
        }

        private void ReleaseCapturedImage()
        {
            CameraPreview.StopCameraPreview();
            _cameraPreviewStarted = false;
            if (!PhotoCanvas.Children.Contains(CameraPreview))
                PhotoCanvas.Children.Insert(0, CameraPreview);

            CameraPreview.IsVisible = false;
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
            UpdateDisplayMode();
        }

        private async void OnPageAppearing(object? sender, EventArgs e)
        {
            RefreshDisplayMode();

            if (_imageBytes is not null)
                return;

            try
            {
                await EnsureCameraPermissionAndStartPreviewAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Camera error", ex.Message, "OK");
            }
        }

        public void RefreshDisplayMode()
        {
            _displayLabValues = Preferences.Get(AppShell.DisplayLabPreferenceKey, false);
            UpdateDisplayMode();
        }

        private void OnPageDisappearing(object? sender, EventArgs e)
        {
            CameraPreview.StopCameraPreview();
            _cameraPreviewStarted = false;
        }

        // White-standard calibration is persisted as a value, but its marker belongs to one image only.
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
            SetWhiteStandardMarker(GetSelectionCenter(), _lastSample);
            WhiteStandardValueLabel.Text = $"RGB {_whiteStandard.Red}, {_whiteStandard.Green}, {_whiteStandard.Blue}";
            ColorSample calibratedStandard = ColorSampler.ApplyWhiteStandard(_whiteStandard, _whiteStandard);
            _lastSample = calibratedStandard;
            RgbValueLabel.Text = $"{calibratedStandard.Red}, {calibratedStandard.Green}, {calibratedStandard.Blue}";
            UpdateLabDisplay(calibratedStandard);
            UpdateTitleBarColor(calibratedStandard);
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
            _comparisonASinglePixel = Mode == SamplingMode.SinglePixel;
            _comparisonAWidth = _comparisonASinglePixel ? 24 : _selectionWidth;
            _comparisonAHeight = _comparisonASinglePixel ? 24 : _selectionHeight;
            UpdateComparisonPointDisplays();
            SetComparisonMarker(ComparisonAMarker, _comparisonAPosition.Value, _comparisonASinglePixel, _comparisonAWidth, _comparisonAHeight, ComparisonAAreaVisual, ComparisonASingleVisual);
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
            _comparisonBSinglePixel = Mode == SamplingMode.SinglePixel;
            _comparisonBWidth = _comparisonBSinglePixel ? 24 : _selectionWidth;
            _comparisonBHeight = _comparisonBSinglePixel ? 24 : _selectionHeight;
            UpdateComparisonPointDisplays();
            SetComparisonMarker(ComparisonBMarker, _comparisonBPosition.Value, _comparisonBSinglePixel, _comparisonBWidth, _comparisonBHeight, ComparisonBAreaVisual, ComparisonBSingleVisual);
            UpdateComparisonDisplay();
        }

        private void UpdateComparisonDisplay()
        {
            if (_comparisonA is null && _comparisonB is null)
            {
                ComparisonValueLabel.Text = "Set A and Set B to compare";
                return;
            }

            string comparison = _comparisonA is not null && _comparisonB is not null
                ? $"A→B: {ColorSampler.DeltaE2000(_comparisonA, _comparisonB):F2} | {ColorSampler.DeltaC(_comparisonA, _comparisonB):F2}"
                : "A→B: — | —";
            ComparisonValueLabel.Text = comparison;
        }

        private void ClearWhiteStandard()
        {
            _whiteStandard = null;
            WhiteStandardValueLabel.Text = "Not set";
            WhiteStandardMarker.IsVisible = false;
        }

        // Sampling mode and selection interaction.
        private void OnSinglePixelClicked(object? sender, EventArgs e)
        {
            _singlePixelMode = true;
            UpdateModeVisual();
            if (_imageBytes is not null)
                RequestSample();
        }

        private void OnAverageAreaClicked(object? sender, EventArgs e)
        {
            _singlePixelMode = false;
            UpdateModeVisual();
            if (_imageBytes is not null)
                RequestSample();
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
            Rect imageBounds = GetDisplayedImageBounds();
            double selectionSize = Math.Min(DefaultSelectionSize, Math.Min(imageBounds.Width, imageBounds.Height));
            selectionSize = Math.Max(Math.Min(MinimumSelectionSize, Math.Min(imageBounds.Width, imageBounds.Height)), selectionSize);
            _selectionWidth = selectionSize;
            _selectionHeight = selectionSize;
            SelectionSizeSlider.Value = DefaultSelectionSize;
            SelectionSizeLabel.Text = $"Selection: {(int)DefaultSelectionSize} × {(int)DefaultSelectionSize} px";
            _left = imageBounds.Left + (imageBounds.Width - _selectionWidth) / 2;
            _top = imageBounds.Top + (imageBounds.Height - _selectionHeight) / 2;
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
            Rect imageBounds = GetDisplayedImageBounds();
            _selectionWidth = Math.Max(MinimumSelectionSize, _selectionWidth);
            _selectionHeight = Math.Max(MinimumSelectionSize, _selectionHeight);

            double centerX = Math.Clamp(_left + _selectionWidth / 2, imageBounds.Left, imageBounds.Right);
            double centerY = Math.Clamp(_top + _selectionHeight / 2, imageBounds.Top, imageBounds.Bottom);
            _left = centerX - _selectionWidth / 2;
            _top = centerY - _selectionHeight / 2;
        }

        private Rect GetDisplayedImageBounds()
        {
            if (_bitmap is null || _bitmap.Width <= 0 || _bitmap.Height <= 0 || PhotoCanvas.Width <= 0 || PhotoCanvas.Height <= 0)
                return new Rect(0, 0, PhotoCanvas.Width, PhotoCanvas.Height);

            double scale = Math.Min(PhotoCanvas.Width / _bitmap.Width, PhotoCanvas.Height / _bitmap.Height);
            double displayedWidth = _bitmap.Width * scale;
            double displayedHeight = _bitmap.Height * scale;
            return new Rect(
                (PhotoCanvas.Width - displayedWidth) / 2,
                (PhotoCanvas.Height - displayedHeight) / 2,
                displayedWidth,
                displayedHeight);
        }

        private void UpdateSelectionLayout()
        {
            AbsoluteLayout.SetLayoutBounds(SelectionBox, new Rect(_left, _top, _selectionWidth, _selectionHeight));
        }

        private Point GetSelectionCenter() => new(
            _left + _selectionWidth / 2,
            _top + _selectionHeight / 2);

        // Convert the displayed AspectFit selection into bitmap coordinates and sample off the UI thread.
        private async void UpdateSample()
        {
            if (_bitmap is null || PhotoCanvas.Width <= 0 || PhotoCanvas.Height <= 0 || _sampleInProgress)
                return;

            _sampleInProgress = true;
            try
            {
                SKBitmap bitmap = _bitmap;
                (int imageWidth, int imageHeight) = ColorSampler.GetImageSize(bitmap);
                Rect imageBounds = GetDisplayedImageBounds();
                double scale = imageBounds.Width / imageWidth;
                double imageLeft = imageBounds.Left;
                double imageTop = imageBounds.Top;

                double centerX = _left + _selectionWidth / 2;
                double centerY = _top + _selectionHeight / 2;
                SamplingMode mode = Mode;
                ColorSample? whiteStandard = _whiteStandard;
                int centerPixelX = (int)Math.Round((centerX - imageLeft) / scale);
                int centerPixelY = (int)Math.Round((centerY - imageTop) / scale);
                int x;
                int y;
                int width;
                int height;

                if (mode == SamplingMode.SinglePixel)
                {
                    x = Math.Clamp(centerPixelX, 0, imageWidth - 1);
                    y = Math.Clamp(centerPixelY, 0, imageHeight - 1);
                    width = 1;
                    height = 1;
                }
                else
                {
                    // Average-area samples are clipped to the bitmap instead of being shifted inward.
                    int requestedWidth = Math.Max(1, (int)Math.Round(_selectionWidth / scale));
                    int requestedHeight = Math.Max(1, (int)Math.Round(_selectionHeight / scale));
                    int requestedX = centerPixelX - requestedWidth / 2;
                    int requestedY = centerPixelY - requestedHeight / 2;
                    int right = Math.Min(imageWidth, requestedX + requestedWidth);
                    int bottom = Math.Min(imageHeight, requestedY + requestedHeight);
                    x = Math.Max(0, requestedX);
                    y = Math.Max(0, requestedY);
                    width = right - x;
                    height = bottom - y;

                    if (width <= 0 || height <= 0)
                        return;
                }

                ColorSample sample = await Task.Run(() =>
                {
                    ColorSample result = ColorSampler.Sample(bitmap, x, y, width, height, mode);
                    return whiteStandard is null ? result : ColorSampler.ApplyWhiteStandard(result, whiteStandard);
                });
                RgbValueLabel.Text = $"{sample.Red}, {sample.Green}, {sample.Blue}";
                _lastSample = sample;
                UpdateTitleBarColor(sample);
                UpdateLabDisplay(sample);
                UpdateComparisonDisplay();
            }

            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _sampleInProgress = false;
            }
        }

        private void UpdateLabDisplay(ColorSample sample)
        {
            LabValueLabel.Text = FormatLab(sample);
        }

        private static void UpdateTitleBarColor(ColorSample sample)
        {
            Color color = Color.FromRgb(sample.Red, sample.Green, sample.Blue);
            Color foreground = (0.299 * sample.Red + 0.587 * sample.Green + 0.114 * sample.Blue) >= 160
                ? Colors.Black
                : Colors.White;

            if (Shell.Current is Shell shell)
            {
                Shell.SetBackgroundColor(shell, color);
                Shell.SetForegroundColor(shell, foreground);
                Shell.SetTitleColor(shell, foreground);
            }
        }

        private void UpdateDisplayMode()
        {
            bool showLab = _displayLabValues;
            CurrentRgbLabel.IsVisible = RgbValueLabel.IsVisible = !showLab;
            CurrentLabLabel.IsVisible = LabValueLabel.IsVisible = showLab;

            if (_lastSample is not null)
            {
                RgbValueLabel.Text = FormatRgb(_lastSample);
                LabValueLabel.Text = FormatLab(_lastSample);
            }

            UpdateComparisonPointDisplays();
        }

        private void UpdateComparisonPointDisplays()
        {
            LabAValueLabel.Text = _comparisonA is null
                ? "—"
                : _displayLabValues ? FormatLab(_comparisonA) : FormatRgb(_comparisonA);
            LabBValueLabel.Text = _comparisonB is null
                ? "—"
                : _displayLabValues ? FormatLab(_comparisonB) : FormatRgb(_comparisonB);
        }

        private static string FormatRgb(ColorSample sample) =>
            $"{sample.Red}, {sample.Green}, {sample.Blue}";

        private static string FormatLab(ColorSample sample) =>
            $"L* {sample.Lightness:F2}, a* {sample.A:F2}, b* {sample.B:F2}";

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

        private static void SetComparisonMarker(View marker, Point position, bool singlePixel, double width, double height, View areaVisual, View singleVisual)
        {
            AbsoluteLayout.SetLayoutBounds(marker, new Rect(
                position.X - width / 2,
                position.Y - height / 2,
                width,
                height));
            areaVisual.IsVisible = !singlePixel;
            singleVisual.IsVisible = singlePixel;
            marker.IsVisible = true;
        }

        private void SetWhiteStandardMarker(Point position, ColorSample sample)
        {
            Color markerColor = (0.299 * sample.Red + 0.587 * sample.Green + 0.114 * sample.Blue) >= 160
                ? Colors.Black
                : Colors.White;
            WhiteStandardHorizontal.Color = markerColor;
            WhiteStandardVertical.Color = markerColor;
            AbsoluteLayout.SetLayoutBounds(WhiteStandardMarker, new Rect(
                position.X - 9,
                position.Y - 9,
                18,
                18));
            WhiteStandardMarker.IsVisible = true;
        }
    }
}
