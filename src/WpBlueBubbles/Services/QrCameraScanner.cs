using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using ZXing;
using ZXing.Common;

namespace WpBlueBubbles.Services
{
    public sealed class QrCameraScanner : IDisposable
    {
        private readonly BarcodeReaderGeneric _reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, TryHarder = true } };
        private MediaCapture _capture;

        public async Task StartAsync(CaptureElement preview)
        {
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Video });
            preview.Source = _capture;
            await _capture.StartPreviewAsync();
        }

        public async Task<string> TryReadAsync()
        {
            if (_capture == null) return null;
            using (var frame = new VideoFrame(BitmapPixelFormat.Bgra8, 640, 480))
            using (var previewFrame = await _capture.GetPreviewFrameAsync(frame))
            using (var bitmap = SoftwareBitmap.Convert(previewFrame.SoftwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore))
            {
                var buffer = new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
                bitmap.CopyToBuffer(buffer);
                var result = _reader.Decode(buffer.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
                return result == null ? null : result.Text;
            }
        }

        public async Task StopAsync(CaptureElement preview)
        {
            if (_capture == null) return;
            try { await _capture.StopPreviewAsync(); } catch { }
            preview.Source = null;
            _capture.Dispose();
            _capture = null;
        }

        public void Dispose()
        {
            _capture?.Dispose();
            _capture = null;
        }
    }
}
