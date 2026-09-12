# OpenCVCameraTracking

<p align="center">
  <img src="src/OpenCVCameraTracking/Assets/AppIcon.png" alt="OpenCVCameraTracking icon" width="180" />
</p>

基于 `.NET 8 + WPF + OpenCvSharp` 的实时摄像头组件。核心采集、检测和跟踪逻辑位于
`OpenCVCameraTracking.Core`，WPF 界面与设置管理位于 `OpenCVCameraTracking`。

## 功能

- 枚举并打开 Windows USB/内置摄像头。
- 播放 RTSP、HTTP 视频流和本地视频文件。
- RTSP/TCP 打开与读取超时、断线重连。
- RTSP 低延迟模式：采集和推理解耦，始终处理最新帧，主动丢弃积压旧帧。
- 默认使用 OpenCV YuNet ONNX 进行人脸检测，保留 Haar 兼容模式。
- 内置 YOLOX INT8 ONNX 动物检测模型，无需另外下载模型即可使用。
- 支持自定义 YOLOv5/YOLOv8 ONNX 模型。
- "人 + 动物"复合检测：YuNet 人脸与动物检测并发运行，两类目标同时显示并独立跟踪。
- IoU 多目标关联、位置平滑和短时丢失保留，画面中显示稳定目标编号。
- 本地人脸/猫白名单：支持从当前画面自动抓取或拖拽圈选区域录入样本；人脸使用 YuNet 五点对齐与 SFace ONNX 特征向量匹配，猫保留 OpenCV LBPH 轻量级匹配。
- 白名单成员按最近录入时间倒序显示，样本列表提供缩略图，双击缩略图可查看大图。
- 已知目标与陌生目标事件记录；陌生人或未录入的猫出现时显示红色提示。
- 设置窗口可管理多个网络视频流并记住默认选择。
- 保存来源类型、检测模式、模型、检测阈值、语言和上次输入的网络地址。
- 简体中文与 English 运行时切换。
- 自定义深色 ComboBox、ComboBoxItem、按钮、输入框和列表样式。
- 自定义窄型深色滚动条和青绿色 Slider 样式，适配 WPF 深色主题。
- 内置摄像头识别主题图标，支持 PNG 预览和多尺寸 Windows ICO。

## 运行

要求：Windows 10/11、x64、.NET 8 SDK 或更高版本。

```powershell
dotnet restore OpenCVCameraTracking.slnx
dotnet run --project src/OpenCVCameraTracking/OpenCVCameraTracking.csproj
```

也可以在 Visual Studio 中打开 `OpenCVCameraTracking.slnx`，将 `OpenCVCameraTracking` 设为启动项目。

## Microsoft Store MSIX 打包

项目包含 `src/OpenCVCameraTracking.Package/OpenCVCameraTracking.Package.wapproj`，用于生成 x64 Microsoft Store 上传包。商店发布标识从本机的 `StoreIdentity.props` 读取，该文件不会提交到 Git；详细配置与构建命令见 [打包工程说明](src/OpenCVCameraTracking.Package/README.md)、[完整发布指南](docs/MSIX-打包与微软商店发布指南.md)、[中文商店页面资料](docs/Microsoft-Store商店资料.md) 和 [美国区英文商店资料](docs/Microsoft-Store-Listing-US.md)。

运行日志由 log4net 在代码中配置，默认写入 `%LocalAppData%\\OpenCVCameraTracking\\Logs\\application.log`；识别事件另存为 `recognition-events.jsonl`。日志仅记录操作和诊断信息，不记录完整 RTSP 地址或商店发布证书内容。

## 版本与更新提醒

主窗口左侧显示当前程序版本。启动时会读取仓库根目录的 `update-manifest.json`，当清单版本高于当前版本时，在预览区右上角显示更新卡片；点击按钮会打开 Microsoft Store。发布新版本时，将 `StorePackageVersion`、WPF 工程版本和 `update-manifest.json` 保持一致，再提交商店包即可。更新检查失败会静默跳过，不影响摄像头使用。

## 人脸检测

默认的“人脸（YuNet，推荐）”比旧 Haar 模型更适合以下场景：

- RTSP 高清画面中的相对较小人脸；
- 佩戴眼镜；
- 轻度侧脸或姿态变化；
- 光照变化。

设置窗口可调整人脸置信度。数值降低会更灵敏，但误检可能增加；默认值为 `0.55`。

“人脸（Haar，兼容）”仍可选择，主要用于不希望执行 ONNX DNN 的兼容场景。

## 人脸与猫白名单

1. 先启动摄像头、RTSP 或视频文件，并选择能检测目标的模式。
2. 点击主窗口中的“录入 / 管理白名单”。
3. 选择“人脸”或“猫脸”，填写姓名或宠物名称。
4. 保持目标清晰可见，点击“抓取当前目标并录入”。
5. 对同一名称从不同角度重复录入 3～5 次，可以提高匹配稳定性。

也可以在白名单弹窗中点击“圈选录入”，打开独立的圈选画面，在预览中拖拽框选人脸或猫，并填写名称；名称和有效选区完成后“确定”按钮才可用，确认后返回白名单窗口并保存样本。

YuNet 模式会保留双眼、鼻尖和嘴角五个关键点，将人脸对齐后交给 SFace ONNX（随应用内置，无需额外下载）提取特征向量，再使用余弦相似度匹配；连续 5 次结果中至少 3 次一致才确认身份或陌生人。检测框会分别显示 `det`（人脸检测置信度）和 `match`（与白名单最接近样本的匹配度）。猫使用 YOLO 检测框上半部分和 `LBPHFaceRecognizer` 进行轻量级外观匹配。该功能不是高安全等级的生物认证，门禁等高风险场景仍应使用经过认证的专用方案。

升级前由 LBPH 保存的人脸样本缺少原始颜色和五点关键点信息，应用仍会尝试读取，但建议删除旧人脸样本，并重新录入正面、左侧、右侧、轻微抬头和低头等 3～5 个清晰样本。

白名单样本和元数据保存在：

```text
%LocalAppData%\OpenCVCameraTracking\Whitelist
```

其中 `profiles.json` 保存成员名称、类型和创建时间，`samples\<profile-id>\*.png` 保存对齐后的人脸或归一化猫脸样本图片。白名单窗口底部会显示当前实际存储目录。

识别事件以 JSON Lines 格式记录在：

```text
%LocalAppData%\OpenCVCameraTracking\recognition-events.jsonl
```

首次检测到一个跟踪目标或其身份状态发生变化时会写入记录。画面中的已知目标使用绿色边框并显示名称；陌生目标使用红色边框和 `unknown` 标签，主窗口顶部同时显示告警横幅。

## 动物检测

选择“动物（ONNX）”后可选择：

1. `内置 YOLOX INT8（COCO 动物）`：默认可用，无需浏览模型文件。
2. `自定义 YOLOv5 / YOLOv8 ONNX`：选择自己的 ONNX 文件。

内置模型过滤以下 COCO 类别：

```text
bird, cat, dog, horse, sheep, cow, elephant, bear, zebra, giraffe
```

内置模型文件约 8.7 MB，来源于 OpenCV Zoo。动物置信度默认是 `0.35`，可在设置窗口调整。

自定义模型支持常见输出：

- YOLOv5：`[1, 25200, 85]`
- YOLOv8：`[1, 84, 8400]`
- 带 NMS：`[1, N, 6]`

## 人 + 动物检测

选择“人 + 动物（ONNX）”后，人脸与动物检测同时运行：

1. `YuNet` 检测人脸，标注为 `face`，使用人脸置信度阈值；
2. 动物检测复用 `内置 YOLOX INT8（COCO 动物）` 或 `自定义 YOLOv5 / YOLOv8 ONNX`，标注为对应动物类别。

两个检测器在同一帧内并发推理，结果合并后按类别去重，跟踪器按标签独立关联，
因此同一个人脸和动物不会互相干扰。该模式全部使用内置模型，无需额外下载。

## 保存网络视频流

点击主窗口的“设置”：

1. 在“网络视频流”区域填写名称和完整地址。
2. 点击“添加 / 更新”。
3. 在“默认视频源”中选择需要默认使用的项目。
4. 点击“保存”。

重新启动后，主窗口“已保存的视频源”下拉框会恢复该选择及地址。切换主窗口中的已保存视频源时，
当前选择也会立即保存。

配置文件位置：

```text
%LocalAppData%\OpenCVCameraTracking\settings.json
```

注意：网络地址会按原文保存在当前 Windows 用户的本地配置中。如果 URL 包含摄像头用户名和密码，
请限制该配置文件的访问权限；生产环境可进一步将凭据迁移到 Windows Credential Manager。

## RTSP 低延迟

低延迟模式默认开启，包含：

- FFmpeg `rtsp_transport=tcp`；
- `nobuffer` 与 `low_delay`；
- 视频缓冲区请求为 1；
- 独立采集线程持续读取流；
- 推理线程只取最新的一帧。

因此即使动物 ONNX 推理速度低于摄像头帧率，画面也不会因为排队处理旧帧而不断增加延迟。
少数摄像头固件若不兼容低延迟 FFmpeg 参数，可在设置窗口关闭低延迟模式。

如果地址省略协议，例如：

```text
user:password@192.168.1.10:554/stream1
```

程序会自动补为：

```text
rtsp://user:password@192.168.1.10:554/stream1
```

## 多语言

设置窗口支持：

- 简体中文 `zh-CN`
- English `en-US`

保存后立即切换，不需要重新启动。资源位于：

```text
src/OpenCVCameraTracking/Languages/
```

增加语言时复制任意现有 `Strings.*.xaml`，翻译值并在 `LocalizationManager` 与语言下拉框中注册语言代码。

## 界面主题与应用图标

控件主题位于：

```text
src/OpenCVCameraTracking/Themes/Controls.xaml
```

其中自定义了 `ComboBox`、`ComboBoxItem`、`Button`、`TextBox`、`ScrollBar`、`Slider` 和 `ListBox` 样式。滚动条采用 10px 窄轨道和圆角滑块，避免 WPF 默认滚动条在深色界面中出现白色箭头区域；设置窗口的阈值 Slider 使用青绿色进度轨道和圆形拖动点。

应用图标文件位于：

```text
src/OpenCVCameraTracking/Assets/AppIcon.png
src/OpenCVCameraTracking/Assets/AppIcon.ico
```

`AppIcon.ico` 包含 `16、24、32、48、64、128、256px` 多个尺寸，并通过项目文件的 `ApplicationIcon` 配置到 Windows 可执行文件，同时应用到主窗口、设置窗口和白名单窗口。

## 集成到现有 WPF 项目

引用 `OpenCVCameraTracking.Core`，然后创建检测器和引擎：

```csharp
var detector = new YuNetFaceDetector(
    "face_detection_yunet_2023mar.onnx",
    confidenceThreshold: 0.55f);

var tracker = new IouMultiObjectTracker(
    minimumIou: 0.18f,
    maximumMisses: 12,
    smoothing: 0.72f);

var engine = new CameraTrackingEngine(detector, detectionInterval: 1, tracker);

engine.FrameReady += (_, frame) =>
{
    // frame.Pixels: BGRA32
    // frame.Objects: 目标 ID、边框、类别、置信度
};

await engine.StartAsync(new CameraSourceOptions
{
    Kind = CameraSourceKind.Stream,
    Address = "rtsp://user:password@camera/stream",
    PreferTcpForRtsp = true,
    LowLatencyMode = true
});
```

同时检测多类目标（例如人脸与动物）时，可传入多个检测器，引擎会自动组合执行：

```csharp
var engine = new CameraTrackingEngine(
    new IObjectDetector[]
    {
        new YuNetFaceDetector("face_detection_yunet_2023mar.onnx"),
        new YoloXOnnxDetector("object_detection_yolox_2022nov_int8.onnx")
    },
    detectionInterval: 1,
    tracker);
```

窗口关闭或切换视频源时：

```csharp
await engine.DisposeAsync();
```

## 主要代码

- `Camera/DirectShowCameraEnumerator.cs`：Windows 视频设备枚举。
- `CameraTrackingEngine.cs`：视频采集、最新帧缓冲、RTSP 重连、绘制与帧事件。
- `Detection/YuNetFaceDetector.cs`：默认 YuNet 人脸检测。
- `Detection/HaarFaceDetector.cs`：Haar 兼容检测。
- `Detection/YoloXOnnxDetector.cs`：内置动物模型解析。
- `Detection/YoloOnnxDetector.cs`：自定义 YOLOv5/YOLOv8 模型解析。
- `Detection/CompositeObjectDetector.cs`：多检测器组合，并发推理并按标签去重。
- `Tracking/IouMultiObjectTracker.cs`：目标关联、编号和边框平滑。
- `Recognition/WhitelistRecognitionService.cs`：白名单样本管理、SFace/LBPH 匹配和多帧投票。
- `Recognition/SFaceEmbeddingExtractor.cs`：五点相似变换对齐、SFace ONNX 推理和余弦相似度计算。
- `WhitelistWindow.xaml`：人脸/猫白名单录入、追加样本和删除。
- `Configuration/RecognitionEventStore.cs`：识别事件 JSONL 记录。
- `Configuration/SettingsStore.cs`：JSON 设置持久化。
- `SettingsWindow.xaml`：语言、阈值和网络流管理。
- `Themes/Controls.xaml`：下拉框等控件模板。

## 模型来源

- YuNet 人脸检测：<https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet>
- SFace 人脸识别（白名单特征向量）：<https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface>
- YOLOX 动物检测：<https://github.com/opencv/opencv_zoo/tree/main/models/object_detection_yolox>
- Haar cascade：<https://github.com/opencv/opencv/tree/4.x/data/haarcascades>

内置模型位于 `src/OpenCVCameraTracking/Assets/Models/`：

| 文件 | 用途 | 大小 |
| --- | --- | --- |
| `face_detection_yunet_2023mar.onnx` | 人脸检测 | 约 0.2 MB |
| `face_recognition_sface_2021dec.onnx` | 人脸识别特征向量 | 约 37 MB |
| `object_detection_yolox_2022nov_int8.onnx` | 动物检测 | 约 8.7 MB |
| `haarcascade_frontalface_default.xml` | Haar 兼容检测 | 约 0.9 MB |

这些模型会随应用一起分发，无需额外下载。模型目录同时包含对应许可证文本，来源与 SHA-256 见 `Assets/Models/README.txt`。

## 验证

已执行：

```text
dotnet build OpenCVCameraTracking.slnx -c Release
0 个警告，0 个错误
```

并完成：

- YuNet 对用户提供的 RTSP 截图执行真实推理，检测到 1 张人脸；
- 内置 YOLOX INT8 完成真实 ONNX 前向推理与输出解析；
- 主窗口和设置窗口启动烟雾测试；
- `dotnet format --verify-no-changes` 格式检查。
