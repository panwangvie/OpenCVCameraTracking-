# .NET 8 + WPF + OpenCvSharp 实现摄像头、RTSP 人脸与动物检测跟踪

## 前言

在桌面端视频监控、门禁识别、宠物监控等项目中，经常需要处理以下需求：

- 枚举并打开本地 USB 摄像头；
- 播放 RTSP、HTTP 网络视频流；
- 播放本地视频文件；
- 实时检测人脸或动物；
- 为目标分配稳定编号并持续跟踪；
- 降低 RTSP 视频流的累积延迟；
- 在 WPF 界面中流畅显示处理后的画面；
- 保存网络视频源、检测阈值和语言设置。

本文介绍一个基于 `.NET 8 + WPF + OpenCvSharp` 实现的摄像头检测跟踪项目：

[OpenCVCameraTracking GitHub 仓库](https://github.com/wutangyuan/OpenCVCameraTracking-)

项目内置 YuNet 人脸模型和 YOLOX 动物检测模型，下载代码后不需要另外准备模型即可运行。

## 一、项目功能

项目目前支持：

- Windows USB、内置和虚拟摄像头枚举；
- DirectShow、Media Foundation 摄像头采集；
- RTSP、HTTP 视频流；
- 本地视频文件播放；
- YuNet ONNX 人脸检测；
- Haar Cascade 人脸检测；
- YOLOX INT8 动物检测；
- 自定义 YOLOv5、YOLOv8 ONNX 模型；
- 人脸和动物组合检测；
- IoU 多目标跟踪；
- 目标编号和边框平滑；
- RTSP 断线自动重连；
- RTSP TCP 和低延迟播放；
- 网络视频源保存；
- 简体中文和英文动态切换；
- WPF 深色界面和自定义控件样式。

## 二、项目结构

解决方案主要分为两个项目：

```text
OpenCVCameraTracking
├─ src
│  ├─ OpenCVCameraTracking.Core
│  │  ├─ Camera
│  │  ├─ Detection
│  │  ├─ Tracking
│  │  └─ CameraTrackingEngine.cs
│  │
│  └─ OpenCVCameraTracking
│     ├─ Assets
│     │  └─ Models
│     ├─ Configuration
│     ├─ Languages
│     ├─ Localization
│     ├─ Themes
│     ├─ MainWindow.xaml
│     └─ SettingsWindow.xaml
│
└─ OpenCVCameraTracking.slnx
```

其中：

- `OpenCVCameraTracking.Core` 负责视频采集、检测和跟踪；
- `OpenCVCameraTracking` 负责 WPF 界面、配置保存和多语言；
- UI 与核心算法分离，核心组件可以复用到其他 WPF 项目中。

核心库通过 NuGet 引用 OpenCvSharp：

```xml
<ItemGroup>
  <PackageReference
      Include="OpenCvSharp4.Windows"
      Version="4.13.0.20260627" />
</ItemGroup>
```

## 三、整体处理流程

一帧视频从采集到显示，主要经过以下步骤：

```text
摄像头或 RTSP 视频流
        ↓
OpenCV VideoCapture
        ↓
读取为 Mat
        ↓
YuNet / Haar / YOLOX 检测
        ↓
生成 Detection 集合
        ↓
IoU 多目标关联
        ↓
分配或保持目标 ID
        ↓
绘制边框、标签、置信度
        ↓
BGR 转 BGRA
        ↓
WPF WriteableBitmap 显示
```

为了统一不同检测模型，项目定义了一个检测器接口：

```csharp
public interface IObjectDetector : IDisposable
{
    IReadOnlyList<Detection> Detect(Mat bgrFrame);
}
```

检测结果和跟踪结果的数据结构如下：

```csharp
public sealed record Detection(
    Rect Box,
    string Label,
    float Confidence,
    int ClassId = -1);

public sealed record TrackedObject(
    int Id,
    Rect Box,
    string Label,
    float Confidence);
```

这样 YuNet、Haar 和 YOLO 都可以通过相同接口接入跟踪引擎。

## 四、枚举 Windows 摄像头

OpenCV 可以根据设备编号打开摄像头，但不能方便地获取 Windows 摄像头的友好名称。

因此项目先通过 DirectShow COM 枚举视频设备：

```csharp
private static readonly Guid VideoInputDeviceCategory =
    new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

public static IReadOnlyList<CameraDeviceInfo> GetVideoInputDevices()
{
    var devices = new List<CameraDeviceInfo>();
    var deviceEnumerator =
        (ICreateDevEnum)(object)new SystemDeviceEnum();

    var category = VideoInputDeviceCategory;
    var result = deviceEnumerator.CreateClassEnumerator(
        ref category,
        out var monikerEnumerator,
        0);

    if (result != 0 || monikerEnumerator is null)
    {
        return devices;
    }

    var monikers = new IMoniker[1];
    var index = 0;

    while (monikerEnumerator.Next(1, monikers, IntPtr.Zero) == 0)
    {
        var moniker = monikers[0];
        var propertyBagId = typeof(IPropertyBag).GUID;

        moniker.BindToStorage(
            null!,
            null,
            ref propertyBagId,
            out var bagObject);

        var propertyBag = (IPropertyBag)bagObject;
        object value = string.Empty;

        var name = propertyBag.Read(
            "FriendlyName",
            ref value,
            IntPtr.Zero) == 0
                ? Convert.ToString(value) ?? $"Camera {index}"
                : $"Camera {index}";

        devices.Add(new CameraDeviceInfo(index++, name));
    }

    return devices;
}
```

DirectShow 负责获取摄像头名称，OpenCV 根据设备索引打开摄像头。

## 五、通过 OpenCV 打开视频源

项目将视频源分为三类：

```csharp
public enum CameraSourceKind
{
    Device,
    Stream,
    File
}
```

对应配置：

```csharp
public sealed record CameraSourceOptions
{
    public CameraSourceKind Kind { get; init; }
        = CameraSourceKind.Device;

    public int DeviceIndex { get; init; }
    public string? Address { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FramesPerSecond { get; init; }
    public int OpenTimeoutMilliseconds { get; init; } = 5_000;
    public int ReadTimeoutMilliseconds { get; init; } = 3_000;
    public bool PreferTcpForRtsp { get; init; } = true;
    public bool LowLatencyMode { get; init; } = true;
}
```

本地摄像头优先使用 DirectShow，打开失败后回退到 Media Foundation：

```csharp
private static VideoCapture OpenCapture(CameraSourceOptions options)
{
    VideoCapture capture;

    if (options.Kind == CameraSourceKind.Device)
    {
        capture = new VideoCapture(
            options.DeviceIndex,
            VideoCaptureAPIs.DSHOW);

        if (!capture.IsOpened())
        {
            capture.Dispose();
            capture = new VideoCapture(
                options.DeviceIndex,
                VideoCaptureAPIs.MSMF);
        }
    }
    else
    {
        var address = options.Address!;
        capture = OpenFfmpeg(address, options);

        if (!capture.IsOpened())
        {
            capture.Dispose();
            capture = new VideoCapture(
                address,
                VideoCaptureAPIs.ANY);
        }
    }

    if (!capture.IsOpened())
    {
        capture.Dispose();
        throw new InvalidOperationException(
            "无法打开视频源，请检查设备、地址和网络。");
    }

    capture.Set(VideoCaptureProperties.BufferSize, 1);

    if (options.Width is > 0)
        capture.Set(VideoCaptureProperties.FrameWidth, options.Width.Value);

    if (options.Height is > 0)
        capture.Set(VideoCaptureProperties.FrameHeight, options.Height.Value);

    if (options.FramesPerSecond is > 0)
        capture.Set(VideoCaptureProperties.Fps, options.FramesPerSecond.Value);

    return capture;
}
```

这里主要使用了 OpenCV 的以下功能：

- `VideoCapture`；
- `VideoCaptureAPIs.DSHOW`；
- `VideoCaptureAPIs.MSMF`；
- `VideoCaptureAPIs.FFMPEG`；
- `VideoCaptureAPIs.ANY`；
- `VideoCaptureProperties.BufferSize`；
- `VideoCaptureProperties.FrameWidth`；
- `VideoCaptureProperties.FrameHeight`；
- `VideoCaptureProperties.Fps`。

## 六、RTSP 低延迟处理

使用 RTSP 时，一个比较常见的问题是：模型推理速度低于摄像头帧率，视频帧不断排队，导致画面延迟越来越大。

项目从两个层面降低延迟。

### 1. 设置 FFmpeg 低延迟参数

```csharp
private static VideoCapture OpenFfmpeg(
    string address,
    CameraSourceOptions options)
{
    const int openTimeoutProperty = 53;
    const int readTimeoutProperty = 54;

    var parameters = new[]
    {
        openTimeoutProperty, options.OpenTimeoutMilliseconds,
        readTimeoutProperty, options.ReadTimeoutMilliseconds
    };

    var isRtsp = address.StartsWith(
        "rtsp://",
        StringComparison.OrdinalIgnoreCase);

    if (!isRtsp || !options.PreferTcpForRtsp)
    {
        return new VideoCapture(
            address,
            VideoCaptureAPIs.FFMPEG,
            parameters);
    }

    const string variableName =
        "OPENCV_FFMPEG_CAPTURE_OPTIONS";

    var previous = Environment.GetEnvironmentVariable(variableName);

    try
    {
        var ffmpegOptions = options.LowLatencyMode
            ? "rtsp_transport;tcp|fflags;nobuffer|flags;low_delay"
            : "rtsp_transport;tcp";

        Environment.SetEnvironmentVariable(variableName, ffmpegOptions);

        return new VideoCapture(
            address,
            VideoCaptureAPIs.FFMPEG,
            parameters);
    }
    finally
    {
        Environment.SetEnvironmentVariable(variableName, previous);
    }
}
```

这里使用了：

```text
rtsp_transport=tcp
fflags=nobuffer
flags=low_delay
```

同时将 OpenCV 视频缓冲区设置为 1：

```csharp
capture.Set(VideoCaptureProperties.BufferSize, 1);
```

### 2. 始终处理最新帧

采集线程持续读取 RTSP，检测线程只获取最新的一帧：

```csharp
public void Publish(Mat source)
{
    var copy = source.Clone();
    Mat? previous;

    lock (_gate)
    {
        if (_completed)
        {
            copy.Dispose();
            return;
        }

        previous = _latest;
        _latest = copy;
    }

    // 丢弃尚未处理的旧帧
    previous?.Dispose();
    _frameAvailable.Set();
}
```

如果推理速度跟不上摄像头帧率，尚未处理的旧帧会被覆盖。这样虽然可能跳过部分帧，但不会因为处理历史帧而持续增加延迟，更适合实时监控场景。

## 七、Haar 人脸检测

Haar 是 OpenCV 传统的人脸检测方式，优点是速度快，不依赖 ONNX 推理。

```csharp
public IReadOnlyList<Detection> Detect(Mat bgrFrame)
{
    using var gray = new Mat();

    Cv2.CvtColor(
        bgrFrame,
        gray,
        ColorConversionCodes.BGR2GRAY);

    Cv2.EqualizeHist(gray, gray);

    var faces = _classifier.DetectMultiScale(
        gray,
        scaleFactor: 1.1,
        minNeighbors: 5,
        flags: HaarDetectionTypes.ScaleImage,
        minSize: _minimumSize);

    return faces
        .Select(box => new Detection(box, "face", 1.0f))
        .ToArray();
}
```

使用到的 OpenCV API：

- `CascadeClassifier`；
- `Cv2.CvtColor`；
- `Cv2.EqualizeHist`；
- `DetectMultiScale`。

Haar 对正脸效果较好，但面对眼镜、侧脸、遮挡和光照变化时，效果通常不如 YuNet。

## 八、YuNet 人脸检测

项目默认使用 OpenCV YuNet ONNX 人脸检测模型。

初始化检测器：

```csharp
_detector = FaceDetectorYN.Create(
    modelFile,
    string.Empty,
    new Size(inputWidth, inputHeight),
    confidenceThreshold,
    0.3f,
    5_000,
    Backend.OPENCV,
    Target.CPU)
    ?? throw new InvalidOperationException(
        "无法加载 YuNet 人脸模型。");
```

其中：

- `confidenceThreshold` 是置信度阈值；
- `0.3f` 是 NMS 阈值；
- `Backend.OPENCV` 使用 OpenCV DNN 后端；
- `Target.CPU` 表示使用 CPU 推理。

检测实现：

```csharp
public IReadOnlyList<Detection> Detect(Mat bgrFrame)
{
    using var input = Letterbox(bgrFrame, out var scale);
    using var faces = new Mat();

    if (_detector.Detect(input, faces) == 0 || faces.Empty())
    {
        return [];
    }

    var detections = new List<Detection>(faces.Rows);

    for (var row = 0; row < faces.Rows; row++)
    {
        var x = faces.At<float>(row, 0) / scale;
        var y = faces.At<float>(row, 1) / scale;
        var width = faces.At<float>(row, 2) / scale;
        var height = faces.At<float>(row, 3) / scale;
        var confidence = faces.At<float>(row, 14);

        var box = new Rect(
            (int)MathF.Round(x),
            (int)MathF.Round(y),
            (int)MathF.Round(width),
            (int)MathF.Round(height));

        detections.Add(new Detection(box, "face", confidence));
    }

    return detections;
}
```

为了降低 CPU 推理压力，原始画面会缩放到固定尺寸：

```csharp
private Mat Letterbox(Mat source, out float scale)
{
    scale = Math.Min(
        (float)_inputWidth / source.Width,
        (float)_inputHeight / source.Height);

    var resizedWidth = Math.Max(
        1,
        (int)MathF.Round(source.Width * scale));

    var resizedHeight = Math.Max(
        1,
        (int)MathF.Round(source.Height * scale));

    var result = new Mat(
        new Size(_inputWidth, _inputHeight),
        MatType.CV_8UC3,
        new Scalar(114, 114, 114));

    using var resized = new Mat();
    Cv2.Resize(
        source,
        resized,
        new Size(resizedWidth, resizedHeight),
        interpolation: InterpolationFlags.Linear);

    using var target = new Mat(
        result,
        new Rect(0, 0, resizedWidth, resizedHeight));

    resized.CopyTo(target);
    return result;
}
```

这种预处理方式称为 Letterbox，能够在保持原始宽高比的情况下适配神经网络输入尺寸。

## 九、YOLOX 动物检测

项目内置 YOLOX INT8 ONNX 模型，并通过 OpenCV DNN 执行推理。

加载模型：

```csharp
_network = CvDnn.ReadNetFromOnnx(modelFile);

if (_network.Empty())
{
    throw new InvalidOperationException(
        "无法加载 YOLOX ONNX 模型。");
}

_network.SetPreferableBackend(Backend.OPENCV);
_network.SetPreferableTarget(Target.CPU);
```

推理过程：

```csharp
public IReadOnlyList<Detection> Detect(Mat bgrFrame)
{
    using var letterboxed = Letterbox(bgrFrame, out var scale);

    using var blob = CvDnn.BlobFromImage(
        letterboxed,
        scaleFactor: 1.0,
        size: new Size(640, 640),
        mean: Scalar.All(0),
        swapRB: true,
        crop: false);

    _network.SetInput(blob);
    using var output = _network.Forward();

    return Parse(output, bgrFrame.Size(), scale);
}
```

主要使用到：

- `CvDnn.ReadNetFromOnnx`；
- `CvDnn.BlobFromImage`；
- `Net.SetInput`；
- `Net.Forward`；
- `CvDnn.NMSBoxes`。

模型推理可能产生多个重叠框，因此还需要进行非极大值抑制：

```csharp
CvDnn.NMSBoxes(
    boxes,
    confidences,
    _confidenceThreshold,
    _nmsThreshold,
    out var keptIndices,
    eta: 1f,
    topK: 100);
```

项目默认保留 COCO 中的十类动物：

```csharp
public static readonly string[] CocoAnimalLabels =
[
    "bird",
    "cat",
    "dog",
    "horse",
    "sheep",
    "cow",
    "elephant",
    "bear",
    "zebra",
    "giraffe"
];
```

## 十、自定义 YOLOv5 和 YOLOv8 模型

除了内置 YOLOX，项目还支持导入自定义 ONNX 模型。

当前解析器兼容以下常见输出：

```text
YOLOv5：[1, 25200, 85]
YOLOv8：[1, 84, 8400]
带 NMS：[1, N, 6]
```

创建自定义检测器：

```csharp
return new YoloOnnxDetector(
    modelPath,
    labels: YoloOnnxDetector.CocoLabels,
    allowedLabels: YoloOnnxDetector.CocoAnimalLabels,
    confidenceThreshold: settings.AnimalConfidence);
```

通过 `allowedLabels` 可以过滤不需要的类别，只输出动物检测结果。

需要注意的是，自定义模型的类别顺序必须与传入的标签集合一致，否则类别名称可能出现错位。

## 十一、人脸和动物组合检测

最新版本增加了“人脸 + 动物”组合检测模式。

```csharp
private IObjectDetector CreateDetector()
{
    var modelDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Models");

    return SelectedTag(DetectionModeBox) switch
    {
        "Face" => new YuNetFaceDetector(
            Path.Combine(
                modelDirectory,
                "face_detection_yunet_2023mar.onnx"),
            _settings.FaceConfidence),

        "Haar" => new HaarFaceDetector(
            Path.Combine(
                modelDirectory,
                "haarcascade_frontalface_default.xml")),

        "Animal" => CreateAnimalDetector(),

        "PersonAnimal" => new CompositeObjectDetector(
        [
            new YuNetFaceDetector(
                Path.Combine(
                    modelDirectory,
                    "face_detection_yunet_2023mar.onnx"),
                _settings.FaceConfidence),

            CreateAnimalDetector()
        ]),

        _ => throw new InvalidOperationException(
            "Unknown detection mode.")
    };
}
```

`CompositeObjectDetector` 会并发执行多个检测器：

```csharp
public IReadOnlyList<Detection> Detect(Mat bgrFrame)
{
    if (_detectors.Count == 1)
    {
        return _detectors[0].Detect(bgrFrame);
    }

    var merged = Task
        .WhenAll(
            _detectors.Select(
                detector => Task.Run(
                    () => detector.Detect(bgrFrame))))
        .GetAwaiter()
        .GetResult()
        .SelectMany(detections => detections)
        .ToArray();

    return RemoveDuplicates(merged);
}
```

合并结果后，按照类别执行 NMS 去重：

```csharp
foreach (var group in detections.GroupBy(
             item => item.Label,
             StringComparer.OrdinalIgnoreCase))
{
    var boxes = group.Select(item => item.Box).ToArray();
    var scores = group.Select(item => item.Confidence).ToArray();

    CvDnn.NMSBoxes(
        boxes,
        scores,
        0f,
        _nmsThreshold,
        out var indices);

    foreach (var index in indices)
    {
        kept.Add(group.ElementAt(index));
    }
}
```

跟踪器会按照标签进行关联，因此 `face` 和 `dog` 等目标不会互相匹配。

## 十二、IoU 多目标跟踪

需要注意：项目的跟踪功能没有调用 OpenCV 的 KCF、CSRT 等单目标跟踪器，而是实现了一套轻量级 Tracking-by-Detection 算法。

每一帧执行检测，然后根据检测框的 IoU 匹配历史目标。

### 1. 计算 IoU

```csharp
private static float IntersectionOverUnion(Rect first, Rect second)
{
    var intersection = first & second;

    if (intersection.Width <= 0 || intersection.Height <= 0)
    {
        return 0;
    }

    var intersectionArea =
        intersection.Width * intersection.Height;

    var unionArea =
        first.Width * first.Height +
        second.Width * second.Height -
        intersectionArea;

    return unionArea <= 0
        ? 0
        : (float)intersectionArea / unionArea;
}
```

### 2. 将检测框与已有目标匹配

```csharp
foreach (var candidate in candidates.OrderByDescending(item => item.Iou))
{
    if (!usedTracks.Add(candidate.Track) ||
        !usedDetections.Add(candidate.Detection))
    {
        continue;
    }

    var detection = detections[candidate.Detection];
    var track = _tracks[candidate.Track];

    track.Box = Smooth(track.Box, detection.Box);
    track.Confidence = detection.Confidence;
    track.Misses = 0;
}
```

### 3. 创建新目标

没有匹配到历史目标的检测结果会分配一个新 ID：

```csharp
foreach (var (detection, index) in
         detections.Select((value, index) => (value, index)))
{
    if (!usedDetections.Contains(index))
    {
        _tracks.Add(new TrackState(
            _nextId++,
            detection.Box,
            detection.Label,
            detection.Confidence));
    }
}
```

### 4. 短时间漏检保留

```csharp
_tracks.RemoveAll(track => track.Misses > _maximumMisses);
```

当人脸短时间被手或其他物体遮挡时，不会立即删除跟踪目标。

### 5. 边框平滑

```csharp
private Rect Smooth(Rect previous, Rect current)
{
    var oldWeight = 1f - _smoothing;

    return new Rect(
        (int)MathF.Round(
            previous.X * oldWeight + current.X * _smoothing),
        (int)MathF.Round(
            previous.Y * oldWeight + current.Y * _smoothing),
        (int)MathF.Round(
            previous.Width * oldWeight + current.Width * _smoothing),
        (int)MathF.Round(
            previous.Height * oldWeight + current.Height * _smoothing));
}
```

WPF 示例中的跟踪参数：

```csharp
var tracker = new IouMultiObjectTracker(
    minimumIou: 0.18f,
    maximumMisses: 12,
    smoothing: 0.72f);

var engine = new CameraTrackingEngine(
    detector,
    detectionInterval: 1,
    tracker);
```

参数说明：

- `minimumIou`：匹配需要达到的最小 IoU；
- `maximumMisses`：允许连续丢失的最大帧数；
- `smoothing`：当前检测结果在平滑计算中的权重；
- `detectionInterval: 1`：每帧执行一次检测。

## 十三、如何确认跟踪正在工作

检测和跟踪并不是同一个概念：

- 检测：当前帧中发现了一张人脸；
- 跟踪：目标移动后仍保持同一个编号。

项目在画面中绘制类似下面的文本：

```text
face #1 96%
dog #2 83%
```

其中：

- `face`、`dog` 是类别；
- `#1`、`#2` 是跟踪 ID；
- `96%`、`83%` 是检测置信度。

确认跟踪是否成功的方法：

1. 让人脸在画面中缓慢移动；
2. 观察人脸边框旁边的编号；
3. 如果移动过程中一直显示 `#1`，说明跟踪成功；
4. 如果频繁从 `#1` 变成 `#2`、`#3`，说明目标关联不稳定。

## 十四、绘制检测框和标签

项目使用 `Cv2.Rectangle`、`Cv2.GetTextSize` 和 `Cv2.PutText` 绘制检测结果：

```csharp
private static void DrawTracks(
    Mat frame,
    IReadOnlyList<TrackedObject> objects)
{
    foreach (var item in objects)
    {
        var color = ColorForId(item.Id);

        Cv2.Rectangle(
            frame,
            item.Box,
            color,
            2,
            LineTypes.AntiAlias);

        var caption =
            $"{item.Label} #{item.Id} {item.Confidence:P0}";

        Cv2.PutText(
            frame,
            caption,
            new Point(item.Box.X + 4, item.Box.Y - 6),
            HersheyFonts.HersheySimplex,
            0.55,
            Scalar.White,
            1,
            LineTypes.AntiAlias);
    }
}
```

不同目标根据 ID 生成不同颜色，方便区分多个目标。

## 十五、将 OpenCV Mat 显示到 WPF

OpenCV 视频帧默认为 BGR 格式，而 WPF `WriteableBitmap` 使用 BGRA32。

首先将 BGR 转换为 BGRA：

```csharp
private void RaiseFrame(
    Mat bgrFrame,
    IReadOnlyList<TrackedObject> objects,
    double framesPerSecond)
{
    using var bgra = new Mat();

    Cv2.CvtColor(
        bgrFrame,
        bgra,
        ColorConversionCodes.BGR2BGRA);

    var stride = checked((int)bgra.Step());
    var pixels = new byte[checked(stride * bgra.Height)];

    Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);

    FrameReady?.Invoke(
        this,
        new FrameReadyEventArgs(
            pixels,
            bgra.Width,
            bgra.Height,
            stride,
            objects,
            framesPerSecond));
}
```

WPF 收到像素后更新 `WriteableBitmap`：

```csharp
_previewBitmap = new WriteableBitmap(
    e.Width,
    e.Height,
    96,
    96,
    PixelFormats.Bgra32,
    null);

PreviewImage.Source = _previewBitmap;

_previewBitmap.WritePixels(
    new Int32Rect(0, 0, e.Width, e.Height),
    e.Pixels,
    e.Stride,
    0);
```

这里没有将每一帧编码成 JPEG 或 PNG，而是直接复制像素，可以降低实时预览的额外开销。

## 十六、配置保存

程序会保存：

- 当前语言；
- 来源类型；
- 最近一次 RTSP 地址；
- 已保存的视频源列表；
- 默认视频源；
- 人脸检测阈值；
- 动物检测阈值；
- 动物模型选择；
- 自定义模型路径；
- RTSP 低延迟开关。

配置文件路径：

```text
%LocalAppData%\OpenCVCameraTracking\settings.json
```

保存时先写入临时文件，然后覆盖正式配置，降低配置写入中断导致文件损坏的风险：

```csharp
public static void Save(ApplicationSettings settings)
{
    Normalize(settings);

    var directory = Path.GetDirectoryName(SettingsPath)!;
    Directory.CreateDirectory(directory);

    var temporaryPath = SettingsPath + ".tmp";

    File.WriteAllText(
        temporaryPath,
        JsonSerializer.Serialize(settings, JsonOptions));

    File.Move(
        temporaryPath,
        SettingsPath,
        overwrite: true);
}
```

需要注意，如果 RTSP URL 中包含用户名和密码，它会随地址一起保存在本地配置文件中。生产环境可以进一步迁移到 Windows Credential Manager。

## 十七、多语言切换

WPF 多语言通过动态切换 `ResourceDictionary` 实现。

```csharp
public static void Apply(string language)
{
    CurrentLanguage = language is "en-US" ? "en-US" : "zh-CN";

    var dictionaries =
        Application.Current.Resources.MergedDictionaries;

    var oldDictionary = dictionaries.FirstOrDefault(dictionary =>
        dictionary.Source?.OriginalString.Contains(
            "Languages/Strings.",
            StringComparison.OrdinalIgnoreCase) == true);

    if (oldDictionary is not null)
    {
        dictionaries.Remove(oldDictionary);
    }

    dictionaries.Add(new ResourceDictionary
    {
        Source = new Uri(
            $"Languages/Strings.{CurrentLanguage}.xaml",
            UriKind.Relative)
    });

    var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
}
```

资源文件：

```text
Languages/Strings.zh-CN.xaml
Languages/Strings.en-US.xaml
```

界面使用动态资源：

```xml
<TextBlock Text="{DynamicResource DetectionMode}" />
```

切换资源字典后，界面文本可以立即刷新，无需重新启动程序。

## 十八、在其他 WPF 项目中使用

创建 YuNet 检测器：

```csharp
var detector = new YuNetFaceDetector(
    "face_detection_yunet_2023mar.onnx",
    confidenceThreshold: 0.55f);
```

创建跟踪器：

```csharp
var tracker = new IouMultiObjectTracker(
    minimumIou: 0.18f,
    maximumMisses: 12,
    smoothing: 0.72f);
```

创建检测跟踪引擎：

```csharp
var engine = new CameraTrackingEngine(
    detector,
    detectionInterval: 1,
    tracker);
```

订阅画面事件：

```csharp
engine.FrameReady += (_, frame) =>
{
    // frame.Pixels：BGRA32 像素
    // frame.Width：画面宽度
    // frame.Height：画面高度
    // frame.Stride：每行字节数
    // frame.Objects：跟踪目标集合
    // frame.FramesPerSecond：处理帧率
};
```

打开 RTSP：

```csharp
await engine.StartAsync(new CameraSourceOptions
{
    Kind = CameraSourceKind.Stream,
    Address = "rtsp://user:password@192.168.1.10:554/stream1",
    PreferTcpForRtsp = true,
    LowLatencyMode = true,
    OpenTimeoutMilliseconds = 5_000,
    ReadTimeoutMilliseconds = 3_000
});
```

同时执行人脸和动物检测：

```csharp
var engine = new CameraTrackingEngine(
    new IObjectDetector[]
    {
        new YuNetFaceDetector(
            "face_detection_yunet_2023mar.onnx"),

        new YoloXOnnxDetector(
            "object_detection_yolox_2022nov_int8.onnx")
    },
    detectionInterval: 1,
    tracker);
```

停止并释放资源：

```csharp
await engine.DisposeAsync();
```

## 十九、运行项目

运行环境：

```text
Windows 10/11
.NET 8 SDK 或更高版本
x64
```

还原依赖：

```powershell
dotnet restore OpenCVCameraTracking.slnx
```

编译：

```powershell
dotnet build OpenCVCameraTracking.slnx -c Release
```

运行 WPF 程序：

```powershell
dotnet run --project src/OpenCVCameraTracking/OpenCVCameraTracking.csproj
```

也可以使用 Visual Studio 打开 `OpenCVCameraTracking.slnx`，然后将 `OpenCVCameraTracking` 设置为启动项目。

## 二十、可以继续优化的方向

### 1. 使用 GPU 推理

当前使用：

```csharp
Backend.OPENCV
Target.CPU
```

后续可以根据 OpenCV 构建环境接入 CUDA、OpenVINO 或 DirectML，提高多个 ONNX 模型同时运行时的性能。

### 2. 增加更高级的跟踪算法

当前 IoU 跟踪器结构简单、速度快，但目标交叉或快速移动时可能发生 ID 切换。可以升级为：

- SORT；
- DeepSORT；
- ByteTrack；
- BoT-SORT。

### 3. 检测和跟踪进一步解耦

可以每隔数帧执行一次神经网络检测，中间帧使用卡尔曼滤波等运动预测算法，以降低 CPU 推理压力。

### 4. RTSP 凭据安全

将 RTSP 用户名和密码从 JSON 配置迁移到：

- Windows Credential Manager；
- DPAPI 加密存储；
- 企业密钥管理服务。

### 5. 增加事件功能

可以在现有目标结果基础上扩展：

- 人脸或动物进入区域告警；
- 越界检测；
- 停留时间统计；
- 自动截图；
- 视频录像；
- WebSocket 或 MQTT 事件推送。

## 总结

本文介绍了如何使用 `.NET 8 + WPF + OpenCvSharp` 实现一个完整的摄像头检测跟踪组件。

项目中使用到的主要 OpenCV 功能包括：

| 功能 | OpenCV API |
|---|---|
| 视频采集 | `VideoCapture` |
| 摄像头后端 | `DSHOW`、`MSMF` |
| 网络视频后端 | `FFMPEG` |
| 图像容器 | `Mat` |
| 图像缩放 | `Cv2.Resize` |
| 颜色转换 | `Cv2.CvtColor` |
| 直方图均衡 | `Cv2.EqualizeHist` |
| Haar 检测 | `CascadeClassifier` |
| YuNet 检测 | `FaceDetectorYN` |
| ONNX 模型加载 | `CvDnn.ReadNetFromOnnx` |
| DNN 输入转换 | `CvDnn.BlobFromImage` |
| 模型推理 | `Net.Forward` |
| 检测框去重 | `CvDnn.NMSBoxes` |
| 绘制边框 | `Cv2.Rectangle` |
| 绘制文本 | `Cv2.PutText` |
| WPF 像素转换 | `BGR2BGRA` |

项目地址：

[https://github.com/wutangyuan/OpenCVCameraTracking-](https://github.com/wutangyuan/OpenCVCameraTracking-)

如果这篇文章对你有帮助，欢迎在 GitHub 仓库中提交 Issue、Pull Request 或 Star。
