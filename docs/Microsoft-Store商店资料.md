# CameraTracking Microsoft Store 商店资料

本文内容对应 Partner Center 的“Store 一览 - 中文（中国）”页面，可直接复制到产品名称、说明、版本新增功能、产品功能和屏幕截图字段。

## 1. 产品名称

```text
CameraTracking
```

建议保持与 Partner Center 中预留的产品名称一致，不要在此处使用项目内部名称 `OpenCVCameraTracking`。

## 2. 说明

复制下面的完整描述到“说明”字段：

```text
CameraTracking 是一款基于 OpenCV 的 Windows 实时摄像头与网络视频分析工具，支持 USB/内置摄像头、RTSP、HTTP 视频流和本地视频文件。应用提供人脸、动物及“人 + 动物”复合检测，并通过稳定的多目标跟踪在画面中显示目标框、置信度和跟踪编号。

应用内置 YuNet ONNX 人脸检测模型和 YOLOX INT8 动物检测模型，也支持导入自定义 YOLOv5/YOLOv8 ONNX 模型。RTSP 模式提供 TCP 连接、读取超时、断线重连和低延迟最新帧处理，适合监控摄像头和局域网视频源。

CameraTracking 还提供本地人脸与猫白名单。用户可以从当前画面自动抓取目标，或在预览窗口中拖拽圈选区域录入样本；已录入目标以绿色标识，陌生人或未录入的猫以红色标识并记录事件。人脸使用 YuNet 五点对齐和 SFace 特征向量匹配，猫使用 OpenCV LBPH 轻量级匹配；白名单样本仅保存在本机用户目录中。

应用支持多个网络视频源配置、检测阈值调节、中英文界面切换，以及适配深色主题的 WPF 控件。所有视频处理均在本地完成，适合开发调试、摄像头测试、家庭宠物观察和轻量级视觉分析场景。
```

## 3. 此版本的新增功能

当前版本 `1.0.1.0` 可填写：

```text
新增白名单样本管理体验：支持在独立预览窗口中拖拽圈选人脸或猫，并输入名称后录入白名单。白名单成员按最近录入时间倒序显示，样本列表新增缩略图，双击缩略图可查看大图。修复 MSIX 安装后应用不显示在开始菜单的问题，并完善 Microsoft Store MSIX 打包配置。
```

如果以后只修复问题，可使用：

```text
优化视频流连接稳定性和白名单识别体验，修复若干界面与安装问题。
```

## 4. 产品功能

“产品功能”最多建议填写 20 项。以下 15 项可以逐条添加：

```text
USB 与内置摄像头枚举
RTSP / HTTP 网络视频播放
本地视频文件播放
RTSP TCP 连接与断线重连
低延迟最新帧处理
YuNet ONNX 人脸检测
Haar 人脸检测兼容模式
YOLOX INT8 动物检测
自定义 YOLOv5 / YOLOv8 ONNX 模型
人脸与动物复合检测
IoU 多目标跟踪与位置平滑
人脸与猫白名单管理
画面圈选录入白名单
陌生目标红色告警与事件记录
中英文界面与深色主题
```

## 5. 补充字段

以下内容对应 Partner Center 的“补充字段”区域。

### 短标题

```text
CameraTracking
```

该名称简短、易识别，并与产品名称保持一致。

### 语音标题

如果暂时不需要 Xbox 或 Kinect 等语音场景，可以留空。若 Partner Center 要求填写，可使用：

```text
Camera Tracking
```

### 简短描述

复制到“简短描述”字段：

```text
实时摄像头、RTSP 视频流、人脸和动物检测跟踪工具。
```

备选版本：

```text
基于 OpenCV 的摄像头、人脸与动物实时检测跟踪应用。
```

## 6. 其他信息

### 关键词

Partner Center 最多允许 7 个关键词。建议逐个输入并按 Enter 确认：

```text
camera
RTSP
OpenCV
face tracking
animal detection
video analysis
摄像头跟踪
```

如果当前商店页面仅面向中文（中国），也可以使用中文关键词：

```text
摄像头
RTSP
人脸识别
人脸跟踪
动物识别
视频分析
白名单
```

不要在关键词中填写与产品无关的品牌、竞品名称或无法实际提供的功能。

### 版权和商标信息

如果应用及图标由个人开发者 `wutangyuan` 发布，可填写：

```text
© 2026 wutangyuan. OpenCVCameraTracking and CameraTracking are trademarks of wutangyuan, where applicable.
```

中文版本：

```text
© 2026 wutangyuan。OpenCVCameraTracking 与 CameraTracking 为 wutangyuan 使用的项目名称或商标（如适用）。
```

如果你有注册公司或正式商标，应将 `wutangyuan` 替换为实际权利主体名称。

### 其他许可条款

建议填写：

```text
本应用按应用商店提供的标准许可条款提供。应用内置的 OpenCV、OpenCvSharp、ONNX Runtime 及相关模型组件分别遵循其各自的开源许可证或模型许可证。用户仅可在遵守适用法律、第三方许可证和模型使用条款的前提下使用本应用。不得将本应用用于侵犯隐私、未经授权的监控或违法用途。
```

如果后续加入闭源模型、商业数据或第三方 SDK，应在此处补充对应的许可和归属信息。

### 开发者

```text
wutangyuan
```

## 7. 屏幕截图规划

Partner Center 要求至少上传一张桌面截图，建议准备 4 张，全部使用应用真实运行画面，不要使用设计稿或带调试信息的截图。

| 建议文件名 | 截图内容 | 建议说明文字 |
| --- | --- | --- |
| `01-face-tracking.png` | 主窗口 + 摄像头画面 + 人脸框 | 实时人脸检测与稳定跟踪 |
| `02-rtsp-animal.png` | RTSP 画面 + 动物检测框 | RTSP 网络视频与动物识别 |
| `03-whitelist.png` | 白名单窗口 + 样本缩略图 | 白名单录入、样本管理与预览 |
| `04-region-enroll.png` | 独立圈选窗口 + 名称输入 | 圈选画面区域录入人脸或猫 |
| `05-settings.png` | 设置窗口 + 网络视频源配置 | 多视频源、阈值和语言设置 |

截图建议：

- 使用 16:9 桌面截图，推荐 `1920 × 1080` 或 `1366 × 768`。
- 保证标题栏、主画面和关键控件完整可见。
- 不要显示个人姓名、局域网账号、密码、完整 RTSP 用户名或敏感地址。
- 白名单截图应使用演示名称，例如 `Demo User` 或 `Demo Cat`。
- 截图中的识别框、样本缩略图和按钮应处于清晰可辨状态。
- Partner Center 页面提示最多支持 30 张桌面截图，至少上传 1 张；建议首批上传 4～5 张。

## 8. Microsoft Store 徽标资源

打包工程已包含用于 MSIX 清单的基础图标资源：

```text
src/OpenCVCameraTracking.Package/Images/Square44x44Logo.png
src/OpenCVCameraTracking.Package/Images/Square150x150Logo.png
src/OpenCVCameraTracking.Package/Images/StoreLogo.png
src/OpenCVCameraTracking.Package/Images/Wide310x150Logo.png
```

这些文件用于包清单和应用入口。Partner Center 的“Microsoft Store 徽标”上传区域可能还会要求更大尺寸的独立商店素材，建议从同一套图标源导出并保持视觉一致：

- 9:16 竖版招贴图：`720 × 1080` 或 `1440 × 2160`。
- 1:1 方形徽图：`1080 × 1080` 或 `2160 × 2160`。
- 使用 PNG 格式，避免透明边缘过大或主体过小。
- 不要在徽标中加入未经授权的 Windows 或 Microsoft Store 标志。

## 9. 隐私与数据说明建议

如果 Partner Center 要求填写隐私政策 URL，应提供一个可公开访问的隐私政策页面。说明中建议保持以下事实一致：

```text
CameraTracking 默认在本地处理摄像头和视频流。白名单样本、白名单元数据和识别事件日志保存在当前 Windows 用户的本地应用数据目录，不会由本应用自动上传到开发者服务器。用户可以通过删除本地白名单目录清理已录入样本。
```

本地数据目录：

```text
%LocalAppData%\OpenCVCameraTracking\Whitelist
%LocalAppData%\OpenCVCameraTracking\recognition-events.jsonl
```

如果后续接入云端服务、远程日志、在线模型下载或第三方分析 SDK，需要同步更新隐私政策和商店说明。

## 10. 提交前检查

- [ ] 产品名称使用 `CameraTracking`。
- [ ] 说明内容与应用实际功能一致。
- [ ] “此版本的新增功能”描述对应当前版本。
- [ ] 产品功能不超过 20 项。
- [ ] 至少上传 1 张桌面截图，建议上传 4～5 张。
- [ ] 截图不含密码、私人 RTSP 地址或个人隐私信息。
- [ ] 包中的 `Identity Name` 已替换为 Partner Center 分配的值（不要将真实值提交到公开仓库）。
- [ ] 包中的 `Publisher` 已替换为 Partner Center 分配的 CN（不要将真实 CN 提交到公开仓库）。
- [ ] 版本号使用 `主版本.次版本.修订.0` 格式，例如 `1.0.1.0`。
- [ ] 上传最新的 `.msixupload` 文件。
- [ ] 已提供隐私政策 URL（如 Partner Center 页面要求）。
