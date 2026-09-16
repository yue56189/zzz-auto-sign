# 推送到 GitHub 的说明

> **更新（2026-09-16）**：推送已直接完成，仓库地址
> <https://github.com/yue56189/zzz-auto-sign>（public，`main` 分支）。
> 当前环境**可以**访问 github.com，不再需要手动导出压缩包搬运；本机也已有
> 可用的 git 凭据（`git credential-manager` 里存着 GitHub 账号 `yue56189`）。
> 下文保留为「换机器 / 想知道原理」时的参考。

沙箱环境**无法访问 github.com** 时，推送这一步需要你在本地完成。

仓库已经在本地建好并提交完毕，你只需补一条推送命令。

---

## 一、导出哪些文件

| 文件 | 说明 | 适用场景 |
|---|---|---|
| `zzz-auto-sign.tar.gz` | 项目完整目录，含 `.git` 历史 | **推荐**，解压即用 |
| `zzz-auto-sign.bundle` | git bundle 包，含全部提交历史 | 想要干净的 git 导入时使用 |

---

## 二、方式一：用压缩包（推荐）

### 1. 下载并解压

把 `zzz-auto-sign.tar.gz` 下载到本地，解压：

```bash
tar xzf zzz-auto-sign.tar.gz
cd zzz-auto-sign
```

解压后 `.git` 目录已存在，包含两次提交：

```
7218f5f chore: 添加一键推送脚本与安全自检
342224a feat: 绝区零每日自动签到工具
```

### 2. 在 GitHub 上创建空仓库

到 <https://github.com/new> 创建仓库：

- **仓库名**：`zzz-auto-sign`（或你喜欢的名字）
- **不要**勾选 `Add a README file`
- **不要**勾选 `.gitignore` 或 `License`

> 如果勾了 README，推送时会因远程非空而失败。补救办法见下文「常见问题」。

### 3. 推送

解压后的目录里有我准备好的脚本：

```bash
bash push-to-github.sh https://github.com/<你的用户名>/zzz-auto-sign.git
```

脚本会自动完成：

1. 检查 git 仓库状态
2. **安全自检** —— 确认没有把凭证、构建产物提交进去（不通过就中止）
3. 配置 remote
4. 推送并在成功后提示下一步

---

## 三、方式二：用 git bundle

适合想把历史干净地导入到已有仓库或新克隆的情况。

```bash
# 1. 从 bundle 克隆出一个工作目录
git clone zzz-auto-sign.bundle zzz-auto-sign
cd zzz-auto-sign

# 2. 指向你的远程仓库
git remote set-url origin https://github.com/<你的用户名>/zzz-auto-sign.git

# 3. 推送
git push -u origin main
```

---

## 四、方式三：手动推送

不想用脚本的话，标准流程：

```bash
cd zzz-auto-sign

git remote add origin https://github.com/<你的用户名>/zzz-auto-sign.git
git branch -M main
git push -u origin main
```

---

## 五、认证问题

GitHub **已不支持用账号密码推送**。两种选择：

### 使用 Personal Access Token（HTTPS）

1. 访问 <https://github.com/settings/tokens>
2. 生成 token（classic），勾选 **`repo`** 权限
3. 推送时：
   - 用户名：你的 GitHub 用户名
   - 密码：**粘贴 token**，不是账号密码

### 使用 SSH

```bash
# 若还没有密钥
ssh-keygen -t ed25519 -C "your@email.com"

# 添加公钥到 GitHub: https://github.com/settings/keys
cat ~/.ssh/id_ed25519.pub

# 改用 SSH 地址推送
git remote set-url origin git@github.com:<你的用户名>/zzz-auto-sign.git
git push -u origin main
```

### 使用 gh CLI

```bash
gh auth login
gh repo create zzz-auto-sign --public --source=. --push
```

---

## 六、推送后：拿到编译好的 exe

推送成功后，GitHub Actions 会自动开始编译。

### 查看构建

1. 打开仓库页面，点 **Actions** 标签
2. 看到 `build` 工作流正在运行（约 3–5 分钟）
3. 等它变绿后点进去
4. 页面底部 **Artifacts** 区域下载 `zzz-auto-sign-<commit-sha>`

解压后得到：

```
ZzzAutoSign/ZzzAutoSign.exe            主程序
CredentialGrabber/CredentialGrabber.exe  凭证提取工具
```

### 发布正式版本

```bash
git tag v1.0.0
git push origin v1.0.0
```

会触发 `release.yml`，自动创建 draft Release，包含：

- `ZzzAutoSign-win-x64.zip`
- `CredentialGrabber-win-x64.zip`
- `SHA256SUMS.txt`

到 Releases 页面检查无误后点 **Publish** 即可。

---

## 七、常见问题

### 推送被拒：远程仓库非空

创建仓库时勾选了 README。先合并远程内容：

```bash
git pull --rebase origin main
git push -u origin main
```

或强制覆盖（**确认远程没有你需要的内容**）：

```bash
git push -u origin main --force
```

### 安全自检不通过

脚本检测到敏感文件被提交。处理：

```bash
git rm --cached <文件路径>
echo "<文件路径>" >> .gitignore
git commit -m "chore: 移除敏感文件"
git push -u origin main
```

> 如果凭证**曾经**被推送过，仅删除文件是不够的 —— git 历史里仍留有记录。需要 `git filter-repo` 重写历史，并且**立刻作废那些凭证**（重新登录米游社使旧 cookie 失效）。

### Actions 构建失败

工作流配置在 `.github/workflows/build.yml`。常见原因：

| 报错 | 原因 | 处理 |
|---|---|---|
| `NETSDK1045` | SDK 版本不匹配 | 确认 `dotnet-version` 为 `8.0.x` |
| `NU1101` (找不到包) | 包名或版本有误 | 检查 `.csproj` 里的 `PackageReference` |
| 编译错误 | 源码问题 | 把报错发我，我来修 |

### 想改成私有仓库

直接创建 private 仓库即可，Actions 在私有仓库同样免费（有额度限制）。注意私有仓库的 artifact 只有你自己能下载。

---

## 八、推送前请自行确认

推送是**公开操作**，请在你的机器上执行前确认这几点：

- [ ] 已解压到本地，且解压目录正确
- [ ] GitHub 仓库已创建，且为空仓库
- [ ] 清楚这个仓库将设为 public 还是 private
- [ ] 已确认 `.gitignore` 覆盖了 `credential.bin`、`state.json`、`settings.json`
- [ ] 已确认没有把个人凭证误提交（推送脚本会帮你再查一次）

我可以帮你准备仓库内容、推送并排查构建错误。推送用的是本机 git 凭据管理器里已有的
凭据（不读取也不落盘明文 token）；如果你更希望自己执行，按上面的方式一/二/三任选即可。
