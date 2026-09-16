# 绝区零每日自动签到

每天**首次**启动 `zenlesszonezero.exe` 时，自动完成米游社（CN 服）绝区零每日签到，并弹出 Windows 通知。

- 成功 → 「绝区零签到完成」
- 失败 → 「绝区零签到失败：原因」

同一天内多次启动游戏不会重复签到。进程名匹配**不区分大小写**。

---

## 它是怎么工作的

```
开机自启（后台常驻，无窗口，仅托盘图标）
        │
        │  WMI 事件订阅（非轮询）
        ▼
检测到 zenlesszonezero.exe 启动
        │
        ▼
每日闸门：今天已签到？ ── 是 ──▶ 什么都不做
        │ 否
        ▼
拉取账号下全部绝区零角色 ──▶ 逐角色查状态 ──▶ 未签则签到
        │
        ▼
汇总结果 ──▶ Windows Toast 通知 ──▶ 记录今日已完成
```

技术栈：**C# / .NET 8 + WinForms**，编译为单文件免安装 exe。

---

## 下载与安装

从 [Releases](../../releases) 页面下载：

| 文件 | 用途 |
|---|---|
| `ZzzAutoSign-win-x64.zip` | 主程序（后台常驻） |
| `CredentialGrabber-win-x64.zip` | 凭证提取工具（首次配置用一次即可） |

解压到任意目录（例如 `D:\Tools\ZzzAutoSign`），**无需安装 .NET 运行时**。

> **需要管理员权限。** 主程序通过 WMI 监听进程启动事件，这需要提权。首次运行会弹 UAC，点「是」即可。

---

## 首次配置（三步）

### 1. 提取凭证

运行 `CredentialGrabber.exe`，两种方式任选：

**方式 A — 打开浏览器登录（推荐）**

1. 点「打开浏览器登录」
2. 在弹出的浏览器窗口里**正常登录**米游社（`www.miyoushe.com`）
3. 登录完成后，工具会自动检测到登录态并读取凭证
4. 确认后凭证自动写入主程序配置

**方式 B — F12 控制台脚本**

1. 点「复制控制台脚本」
2. 在你**已经登录**米游社的浏览器里按 `F12`，切到 `Console` 标签
3. 粘贴脚本并回车
4. 脚本会把凭证 JSON 复制到剪贴板
5. 回到工具，粘贴到输入框，点「解析并保存」

> **关于账号密码**：本工具**不会**索取、保存或传输你的账号密码。米游社的密码登录接口强制要求极验行为验证码，纯程序化登录无法通过，因此我们选择让你在真实浏览器里亲自登录。

### 2. 启动主程序

运行 `ZzzAutoSign.exe`（右键 → 以管理员身份运行）。

启动后没有主窗口，只在托盘出现图标。右键可见菜单：

| 菜单项 | 说明 |
|---|---|
| 立即签到 | 手动触发一次（绕过每日限制，用于验证） |
| 状态：… | 显示今日签到情况与监听状态 |
| 设置… | 修改凭证与参数 |
| 打开日志 | 查看运行日志 |
| 开机自启 | 勾选后开机自动常驻 |
| 测试通知 | 验证通知能否正常弹出 |
| 退出 | 关闭程序 |

### 3. 验证

先点「测试通知」确认通知能弹出来，再点「立即签到」验证接口连通。

看到「绝区零签到完成」就说明配置成功了。之后每天首次启动游戏就会自动签到。

---

## 通知策略

| 场景 | 是否通知 |
|---|---|
| 全部角色签到成功 | ✅ 「绝区零签到完成」 |
| 全部角色今日已签到 | ❌ 静默（仅记日志） |
| 部分成功、部分失败 | ✅ 汇总一条，含失败明细 |
| 全部失败 | ✅ 「绝区零签到失败：原因」 |
| 凭证缺失 / 已失效 | ✅ 提示重新获取凭证 |

多角色结果**汇总为一条通知**，不会连弹多条。

---

## 常见问题

### 通知不弹出

按顺序排查：

1. **点「测试通知」** —— 如果测试通知也没有，是系统层面的问题
2. **检查系统通知设置**：设置 → 系统 → 通知 → 找到「绝区零自动签到」，确认已开启
3. **关闭专注助手 / 勿扰模式**：专注助手开启时通知会被静默
4. **检查「通知和操作」**中是否关闭了「允许通知」
5. 程序在 Toast 失败时会**自动降级为托盘气泡提示**，留意托盘图标

### 提示「进程监听启动失败」

没有以管理员身份运行。右键 exe → 以管理员身份运行。若使用开机自启，程序会创建计划任务来规避每次 UAC 弹窗。

### 提示「登录凭证已失效」

`cookie_token` 有效期约 1 天。若配置了 `stoken`（约 30 天有效），程序会**自动刷新**。

如果 stoken 也过期了：
1. 重新运行 `CredentialGrabber.exe`
2. 重新提取凭证
3. 重启主程序

### `retcode=-1`（签名无效）

米哈游升级客户端后可能更换签名 salt。处理方式：

1. 打开「设置」→ 切换**签名模式**（0 / 1 / 2 逐个试）
2. 若都不行，用浏览器抓包获取当前的 salt 与版本号，写入 `%LOCALAPPDATA%\ZzzAutoSign\endpoints.json`：

```json
{
  "saltWeb": "新的salt值",
  "appVersion": "新的版本号"
}
```

不需要重新下载程序。

### 提示「触发了米游社风险校验」

米游社检测到异常登录行为，要求验证码。请手动打开米游社 App 或网页完成一次签到，之后通常会自动恢复。**程序不会重试**这种行为，以免加重风控。

### 杀毒软件报警

程序同时具备「单文件自解压 + 写注册表/计划任务 + 订阅 WMI 事件」这几个特征，容易被启发式规则误判。源码完全公开，可自行审计或编译。

### 签到成功但游戏里看不到奖励

奖励需要在游戏内或米游社 App 中**手动领取**。签到只是占位，不会自动发放。

---

## 从源码构建

需要 .NET 8 SDK。

```powershell
git clone <repo-url>
cd zzz-auto-sign

# 编译
dotnet build ZzzAutoSign.sln -c Release

# 打包为单文件 exe
dotnet publish src/ZzzAutoSign/ZzzAutoSign.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/ZzzAutoSign
dotnet publish src/CredentialGrabber/CredentialGrabber.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/CredentialGrabber

# 跑测试
dotnet test tests/ZzzAutoSign.Tests/ZzzAutoSign.Tests.csproj
```

推送到 GitHub 后，Actions 会自动编译并上传产物（见 `.github/workflows/build.yml`）；打 `v1.0.0` 形式的 tag 会创建 Release（`release.yml`）。

---

## 文件位置

| 内容 | 路径 |
|---|---|
| 加密凭证 | `%LOCALAPPDATA%\ZzzAutoSign\credential.bin`（DPAPI 加密，仅当前用户可解密） |
| 非敏感配置 | `%LOCALAPPDATA%\ZzzAutoSign\settings.json` |
| 每日状态 | `%LOCALAPPDATA%\ZzzAutoSign\state.json` |
| 日志 | `%LOCALAPPDATA%\ZzzAutoSign\logs\`（按天滚动，保留 14 天） |
| 端点覆盖 | `%LOCALAPPDATA%\ZzzAutoSign\endpoints.json`（可选） |

日志中的凭证会被自动脱敏为 `abcd***wxyz` 形式。

---

## 更多文档

- [接口说明](docs/接口说明.md) —— 端点、DS 签名算法、返回码
- [凭证获取指南](docs/凭证获取指南.md) —— 图文步骤
- [排障手册](docs/排障手册.md) —— 常见失败对照表

---

## 免责声明

本工具仅用于个人账号的自动化签到，未使用任何破解或绕过安全机制的手段。使用第三方工具操作米游社账号**存在被风控的固有风险**，请自行评估。请勿用于批量账号或其他违反《米游社用户协议》的场景。
