using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Nio;
using ZXing;
using ZXing.Common;

namespace ErganiManager.Android;

[Activity(Label = "Scan QR code", Theme = "@style/MyTheme.NoActionBar", Exported = false,
    ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class BarcodeScannerActivity : Activity
{
    private const int CameraPermissionRequest = 5101;
    private const int FrameWidth = 640;
    private const int FrameHeight = 480;

    private TextureView? _preview;
    private TextView? _status;
    private CameraDevice? _camera;
    private CameraCaptureSession? _session;
    private ImageReader? _imageReader;
    private HandlerThread? _cameraThread;
    private Handler? _cameraHandler;
    private Surface? _previewSurface;
    private volatile bool _decoded;
    private volatile bool _processing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildScannerUi();

        if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
            RequestPermissions(new[] { Manifest.Permission.Camera }, CameraPermissionRequest);
        else
            StartCameraWhenReady();
    }

    private void BuildScannerUi()
    {
        var root = new FrameLayout(this) { LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
        root.SetBackgroundColor(Color.Black);

        _preview = new TextureView(this);
        root.AddView(_preview, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var panel = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Gravity = GravityFlags.Center,
            SetPadding = { }
        };
        panel.SetPadding(24, 28, 24, 28);
        panel.SetBackgroundColor(Color.Argb(190, 0, 0, 0));

        _status = new TextView(this)
        {
            Text = "Point the camera at the employee QR code",
            TextSize = 18,
            Gravity = GravityFlags.Center
        };
        _status.SetTextColor(Color.White);
        panel.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var cancel = new Button(this) { Text = "Cancel" };
        cancel.Click += (_, _) => Finish();
        panel.AddView(cancel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        var panelParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom);
        root.AddView(panel, panelParams);
        SetContentView(root);

        _preview.SurfaceTextureListener = new PreviewListener(this);
    }

    private void StartCameraWhenReady()
    {
        if (_preview?.IsAvailable == true)
            OpenCamera();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != CameraPermissionRequest) return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            StartCameraWhenReady();
        else
        {
            _status!.Text = "Camera permission is required to scan QR codes.";
            _status.PostDelayed(() => Finish(), 1800);
        }
    }

    private void OpenCamera()
    {
        try
        {
            _cameraThread = new HandlerThread("ErganiQrCamera");
            _cameraThread.Start();
            _cameraHandler = new Handler(_cameraThread.Looper);

            var manager = (CameraManager?)GetSystemService(CameraService);
            if (manager is null) throw new InvalidOperationException("Android camera service is unavailable.");

            var cameraId = manager.GetCameraIdList().FirstOrDefault();
            if (cameraId is null) throw new InvalidOperationException("No camera was found.");

            _imageReader = ImageReader.NewInstance(FrameWidth, FrameHeight, ImageFormatType.Yuv420888, 2);
            _imageReader.SetOnImageAvailableListener(new FrameListener(this), _cameraHandler);

            if (CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
                manager.OpenCamera(cameraId, new CameraStateListener(this), _cameraHandler);
        }
        catch (Exception ex)
        {
            _status!.Text = "Unable to start camera: " + ex.Message;
        }
    }

    private void CreatePreview(SurfaceTexture texture)
    {
        try
        {
            texture.SetDefaultBufferSize(FrameWidth, FrameHeight);
            _previewSurface = new Surface(texture);
            var outputs = new List<Surface> { _previewSurface, _imageReader!.Surface };
            _camera!.CreateCaptureSession(outputs, new PreviewSessionListener(this), _cameraHandler);
        }
        catch (Exception ex)
        {
            _status!.Text = "Unable to configure camera: " + ex.Message;
        }
    }

    private void ProcessFrame(ImageReader reader)
    {
        if (_decoded || _processing) return;
        _processing = true;
        Image? image = null;
        try
        {
            image = reader.AcquireLatestImage();
            if (image is null) return;

            var plane = image.GetPlanes()?[0];
            if (plane is null) return;

            var width = image.Width;
            var height = image.Height;
            var rowStride = plane.RowStride;
            var pixelStride = plane.PixelStride;
            var buffer = plane.Buffer;
            var raw = new byte[buffer.Remaining()];
            buffer.Get(raw);

            // ZXing expects tightly packed luminance. Android's Y plane can contain row padding.
            var luminance = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var sourceRow = y * rowStride;
                var targetRow = y * width;
                for (var x = 0; x < width; x++)
                {
                    var sourceIndex = sourceRow + x * pixelStride;
                    if (sourceIndex < raw.Length)
                        luminance[targetRow + x] = raw[sourceIndex];
                }
            }

            var source = new PlanarYUVLuminanceSource(
                luminance, width, height, 0, 0, width, height, false);
            var readerEngine = new MultiFormatReader();
            var hints = new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.POSSIBLE_FORMATS] = new[] { BarcodeFormat.QR_CODE },
                [DecodeHintType.TRY_HARDER] = true
            };

            Result? result = null;
            try
            {
                result = readerEngine.decode(new BinaryBitmap(new HybridBinarizer(source)), hints);
            }
            catch (ReaderException)
            {
                // Try the other orientation; portrait phones often deliver landscape sensor frames.
                try
                {
                    var rotated = source.RotateCounterClockwise();
                    result = readerEngine.decode(new BinaryBitmap(new HybridBinarizer(rotated)), hints);
                }
                catch (ReaderException) { }
            }

            if (!string.IsNullOrWhiteSpace(result?.Text))
            {
                _decoded = true;
                RunOnUiThread(() =>
                {
                    _status!.Text = "QR code detected. Sending to Ergani...";
                    var data = new Intent();
                    data.PutExtra("barcode", result.Text);
                    SetResult(Result.Ok, data);
                    Finish();
                });
            }
        }
        catch
        {
            // Ignore individual camera frames; scanning continues with the next frame.
        }
        finally
        {
            image?.Close();
            _processing = false;
        }
    }

    private void CloseCamera()
    {
        try { _session?.StopRepeating(); } catch { }
        try { _session?.Close(); } catch { }
        _session = null;
        try { _camera?.Close(); } catch { }
        _camera = null;
        try { _imageReader?.Close(); } catch { }
        _imageReader = null;
        try { _previewSurface?.Release(); } catch { }
        _previewSurface = null;
        try { _cameraThread?.QuitSafely(); } catch { }
        _cameraThread = null;
        _cameraHandler = null;
    }

    protected override void OnPause()
    {
        CloseCamera();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        CloseCamera();
        base.OnDestroy();
    }

    private sealed class PreviewListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly BarcodeScannerActivity _owner;
        public PreviewListener(BarcodeScannerActivity owner) => _owner = owner;
        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            => _owner.StartCameraWhenReady();
        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;
        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }
        public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
    }

    private sealed class CameraStateListener : CameraDevice.StateCallback
    {
        private readonly BarcodeScannerActivity _owner;
        public CameraStateListener(BarcodeScannerActivity owner) => _owner = owner;
        public override void OnOpened(CameraDevice camera)
        {
            _owner._camera = camera;
            var texture = _owner._preview?.SurfaceTexture;
            if (texture is not null) _owner.CreatePreview(texture);
        }
        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _owner._camera = null;
            _owner.RunOnUiThread(() => _owner._status!.Text = "Camera disconnected.");
        }
        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            _owner._camera = null;
            _owner.RunOnUiThread(() => _owner._status!.Text = "Camera error: " + error);
        }
    }

    private sealed class PreviewSessionListener : CameraCaptureSession.StateCallback
    {
        private readonly BarcodeScannerActivity _owner;
        public PreviewSessionListener(BarcodeScannerActivity owner) => _owner = owner;
        public override void OnConfigured(CameraCaptureSession session)
        {
            if (_owner._camera is null) { session.Close(); return; }
            _owner._session = session;
            try
            {
                var request = _owner._camera.CreateCaptureRequest(CameraTemplate.Preview);
                request.AddTarget(_owner._previewSurface);
                request.AddTarget(_owner._imageReader!.Surface);
                session.SetRepeatingRequest(request.Build(), null, _owner._cameraHandler);
            }
            catch (Exception ex)
            {
                _owner.RunOnUiThread(() => _owner._status!.Text = "Camera preview failed: " + ex.Message);
            }
        }
        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            session.Close();
            _owner.RunOnUiThread(() => _owner._status!.Text = "Could not start camera preview.");
        }
    }

    private sealed class FrameListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly BarcodeScannerActivity _owner;
        public FrameListener(BarcodeScannerActivity owner) => _owner = owner;
        public void OnImageAvailable(ImageReader reader) => _owner.ProcessFrame(reader);
    }
}
