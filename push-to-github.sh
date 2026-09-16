#!/usr/bin/env bash
#
# 一键推送到 GitHub
# ------------------------------------------------------------------
# 用法：
#   1. 先在 GitHub 网页上创建一个空仓库（不要勾选 README / .gitignore / License）
#   2. 在仓库页面复制 HTTPS 地址，形如：
#        https://github.com/<你的用户名>/zzz-auto-sign.git
#   3. 运行本脚本：
#        bash push-to-github.sh https://github.com/<你的用户名>/zzz-auto-sign.git
#
# 说明：首次推送会要求输入 GitHub 用户名与 Personal Access Token（不是密码）。
#       若未配置过，建议改用 SSH 地址，或先执行 gh auth login。
# ------------------------------------------------------------------

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

REPO_URL="${1:-}"

if [ -z "$REPO_URL" ]; then
    echo -e "${RED}错误：请提供仓库地址${NC}"
    echo
    echo "用法: bash push-to-github.sh <仓库地址>"
    echo "示例: bash push-to-github.sh https://github.com/yourname/zzz-auto-sign.git"
    exit 1
fi

# 切到脚本所在目录（即仓库根目录）
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo -e "${YELLOW}[1/4] 检查 git 仓库状态…${NC}"

if [ ! -d .git ]; then
    echo -e "${RED}当前目录不是 git 仓库。${NC}"
    echo "请确认本脚本位于项目根目录（与 ZzzAutoSign.sln 同级）。"
    exit 1
fi

echo "当前分支: $(git rev-parse --abbrev-ref HEAD)"
echo "提交数量: $(git rev-list --count HEAD)"
echo "跟踪文件: $(git ls-files | wc -l)"

# 安全检查：确认没有把凭证提交进去
echo
echo -e "${YELLOW}[2/4] 安全检查：确认未包含敏感文件…${NC}"

LEAK=0
for p in credential.bin credential.json state.json settings.json; do
    if git ls-files | grep -qE "(^|/)$p$"; then
        echo -e "${RED}  ✗ 发现敏感文件: $p${NC}"
        LEAK=1
    fi
done

if git ls-files | grep -qE "^(bin|obj|publish)/|/(bin|obj|publish)/"; then
    echo -e "${RED}  ✗ 发现构建产物（bin/obj/publish）${NC}"
    LEAK=1
fi

if [ "$LEAK" -eq 1 ]; then
    echo
    echo -e "${RED}安全检查未通过，已中止推送。${NC}"
    echo "请先把上述文件加入 .gitignore 并从索引中移除："
    echo "  git rm --cached <文件路径>"
    exit 1
fi

echo -e "${GREEN}  ✓ 未发现敏感文件${NC}"

# 配置远程
echo
echo -e "${YELLOW}[3/4] 配置远程仓库…${NC}"

if git remote get-url origin >/dev/null 2>&1; then
    echo "已存在 origin，更新为: $REPO_URL"
    git remote set-url origin "$REPO_URL"
else
    git remote add origin "$REPO_URL"
    echo "已添加 origin: $REPO_URL"
fi

# 推送
echo
echo -e "${YELLOW}[4/4] 推送到 GitHub…${NC}"

BRANCH="$(git rev-parse --abbrev-ref HEAD)"

if git push -u origin "$BRANCH"; then
    echo
    echo -e "${GREEN}  ✓ 推送成功${NC}"
    echo
    echo "下一步："
    echo "  1. 打开仓库页面，切到 Actions 标签，查看构建是否通过"
    echo "  2. 构建完成后，在该次运行底部下载 artifact（含两个 exe）"
    echo "  3. 若要发布正式版本，打 tag 即可自动创建 Release："
    echo "       git tag v1.0.0 && git push origin v1.0.0"
else
    echo
    echo -e "${RED}  ✗ 推送失败${NC}"
    echo
    echo "常见原因："
    echo "  - 认证失败：GitHub 已不支持密码推送，请使用 Personal Access Token"
    echo "    生成地址: https://github.com/settings/tokens （勾选 repo 权限）"
    echo "  - 远程仓库非空：若创建仓库时勾选了 README，先执行"
    echo "      git pull --rebase origin $BRANCH"
    echo "  - 网络问题：检查代理设置或改用 SSH 地址"
    exit 1
fi
