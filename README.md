# 说明

**这并非录播姬官方项目，官方项目地址见 [BililiveRecorder](https://github.com/BililiveRecorder)**

# 简介

本项目的目的是使用录播姬的引擎来实现任意 flv 流的录制，最初版本由 [@kira1928](https://github.com/kira1928/BililiveRecorder) 完成

# 安装

在 [release](https://github.com/renmu123/BililiveRecorder/releases) 选择对应的系统版本

# 使用

## Downloader 模式

Downloader 模式允许你直接录制任意 FLV 流,无需配置文件。

### 基本用法

```bash
BililiveRecorder downloader <url> <output-path>
```

或使用简写:

```bash
BililiveRecorder d <url> <output-path>
```

### 参数说明

#### 必需参数

-   `url`: 要录制的 FLV 流地址
-   `output-path`: 输出文件路径

#### 可选参数

-   `--loglevel`, `--log`, `-l`: 控制台日志最低级别 (Verbose|Debug|Information|Warning|Error|Fatal)
    -   默认: Information
-   `--cookie`, `-c`: API 请求的 Cookie 字符串
-   `--download-headers`, `-h`: 下载器使用的 HTTP 头
-   `--progress`, `-p`: 显示录制进度
-   `--max-size`, `-m`: 最大文件大小 (MB)
-   `--max-duration`, `-d`: 最大录制时长 (分钟)
-   `--disable-log-file`: 禁用日志文件输出
-   `--use-system-proxy`: 使用系统代理设置
-   `--proxy`: 手动指定代理地址 (例如: http://127.0.0.1:7890)
-   `--timing-watchdog-timeout`, `-t`: 超时时间(毫秒),默认: 10000
-   `--split-on-script-tag`: 在 script tag 处分割输出文件
-   `--disable-split-on-h264-annex-b`: 禁用在 H264 Annex B 处分割

### 使用示例

#### 基本录制

```bash
BililiveRecorder d "https://example.com/stream.flv" "D:\录制\output.flv"
```

#### 显示进度并限制大小

```bash
BililiveRecorder d --progress --max-size 1024 "https://example.com/stream.flv" "D:\录制\output.flv"
```

#### 使用代理录制

```bash
BililiveRecorder d --proxy "http://127.0.0.1:7890" "https://example.com/stream.flv" "D:\录制\output.flv"
```

#### 限制录制时长

```bash
BililiveRecorder d --max-duration 60 "https://example.com/stream.flv" "D:\录制\output.flv"
```

### 交互控制

录制过程中支持以下控制命令:

-   **按 `Q` 键**: 停止录制并退出
-   **按 `S` 键**: 手动分割当前文件(生成新文件继续录制)

如果在非 TTY 环境(如管道或脚本)中运行,可以通过标准输入发送命令:

-   输入 `q\n` : 停止录制
-   输入 `s\n` : 分割文件

# 更新记录

![更新记录](./CHANEGLOG.md)
