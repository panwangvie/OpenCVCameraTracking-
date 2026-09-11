# CameraTracking — Microsoft Store Listing (English - United States)

This document contains copy-ready English text for the United States Microsoft Store listing in Partner Center.

## Product name

```text
CameraTracking
```

## Description

```text
CameraTracking is a Windows real-time camera and video analysis tool built with OpenCV. It supports USB and built-in cameras, RTSP and HTTP network streams, and local video files. Detect and track faces, animals, or both at the same time, with bounding boxes, confidence scores, and tracking IDs displayed on the video.

The app includes a YuNet ONNX face detection model and a YOLOX INT8 animal detection model. You can also import compatible YOLOv5 or YOLOv8 ONNX models. RTSP mode includes TCP transport, read timeouts, automatic reconnect, and low-latency latest-frame processing for surveillance cameras and local network video sources.

CameraTracking provides a local face and cat whitelist. Capture a detected target from the current frame or drag to select a region in the enrollment window, then save it with a name. Known targets are shown in green; unknown people or cats are shown in red and can be recorded as events. Human faces use YuNet five-point alignment and SFace embeddings, while cats use lightweight OpenCV LBPH matching. Whitelist samples are stored locally.

All video processing is performed locally. Configure multiple video sources, detection thresholds, language, and a dark WPF interface for camera testing, development, home pet monitoring, and lightweight visual analysis.
```

## What's new in this version

For version `1.0.1.0`:

```text
Improved whitelist enrollment with a dedicated preview window for selecting a face or cat region and entering a name. Whitelist members are sorted by enrollment time, samples now include thumbnails, and double-clicking a thumbnail opens a larger preview. Also improved MSIX packaging and fixed an issue where the installed app could be missing from the Start menu.
```

## Product features

Add these as individual features in Partner Center (up to 20):

```text
USB and built-in camera enumeration
RTSP and HTTP network video playback
Local video file playback
RTSP TCP transport and automatic reconnect
Low-latency latest-frame processing
YuNet ONNX face detection
Haar cascade face detection compatibility mode
YOLOX INT8 animal detection
Custom YOLOv5 and YOLOv8 ONNX models
Combined face and animal detection
IoU-based multi-object tracking
Face and cat whitelist management
Region selection for whitelist enrollment
Unknown-target alerts and event logging
English and Chinese UI with dark theme
```

## Additional fields

### Short title

```text
CameraTracking
```

### Voice title

Optional. Leave blank if voice/Xbox scenarios are not supported. If required:

```text
Camera Tracking
```

### Short description

```text
Real-time camera, RTSP, face, and animal detection and tracking.
```

Alternative:

```text
An OpenCV-powered app for real-time camera, face, and animal tracking.
```

## Other information

### Keywords (maximum 7)

Enter each keyword separately:

```text
camera
RTSP
OpenCV
face tracking
animal detection
video analysis
pet monitoring
```

### Copyright and trademark information

Replace `wutangyuan` with the legal rights holder if different:

```text
© 2026 wutangyuan. OpenCVCameraTracking and CameraTracking are trademarks of wutangyuan, where applicable.
```

### Additional license terms

```text
This app is provided under the Microsoft Store Standard Application License Terms. OpenCV, OpenCvSharp, ONNX Runtime, and included model components are distributed under their respective open-source or model licenses. Use of the app is subject to applicable law, third-party licenses, and model usage terms. Do not use this app for privacy violations, unauthorized surveillance, or unlawful activities.
```

### Developer

```text
wutangyuan
```

## Screenshot captions

| File | Caption |
| --- | --- |
| `01-face-tracking.png` | Real-time face detection and tracking |
| `02-rtsp-animal.png` | RTSP network video and animal detection |
| `03-whitelist.png` | Whitelist enrollment and sample preview |
| `04-region-enroll.png` | Select a region to enroll a face or cat |
| `05-settings.png` | Video sources, thresholds, and language settings |

Before uploading, hide passwords, private RTSP URLs, personal names, and other sensitive information in screenshots.

## Privacy statement text

```text
CameraTracking processes camera and video streams locally by default. Whitelist samples, whitelist metadata, and recognition event logs are stored in the current Windows user's local application data folder. The app does not automatically upload this data to the developer's servers. Users can remove enrolled samples by deleting the local whitelist folder.
```

Local data locations:

```text
%LocalAppData%\OpenCVCameraTracking\Whitelist
%LocalAppData%\OpenCVCameraTracking\recognition-events.jsonl
```

## Submission checklist

- [ ] Select the `English (United States)` Store listing.
- [ ] Confirm that the product name is `CameraTracking`.
- [ ] Paste the description and short description above.
- [ ] Add no more than seven keywords.
- [ ] Add at least one desktop screenshot; four or five are recommended.
- [ ] Replace the copyright holder if the legal entity is not `wutangyuan`.
- [ ] Provide a public privacy policy URL if Partner Center requires one.
- [ ] Upload the latest `.msixupload` package.
