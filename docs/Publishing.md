# 维护与发布

仓库：<https://github.com/CH4ACKO3/MouseShortcuts>。游戏 App ID：`4348910`。模组 ID 保持 `ch4acko3_right_click_close`，程序集和安装目录保持 `RightClickClose`。

## 日常更新

1. 修改源码，同步 `source/RightClickClose.csproj` 和 `manifest.xml` 中的版本号，添加 `releases/版本号.md`。
2. 用自己的游戏安装运行 `Tools/Build.ps1`，在游戏里确认行为，再推送 `main`。重复本地打包前移走旧的 `artifacts/package`。
3. 等待 Actions 构建通过，再推送相同版本的标签，例如 `v1.6.6`。工作流使用同一个构建产物生成 GitHub Release 和工坊更新。

工坊介绍分别维护在 `docs/Description.zh-CN.bbcode`、`docs/Description.zh-TW.bbcode` 和 `docs/Description.en.bbcode`。对应 README 为 `README.md`、`README.zh-TW.md` 和 `README.en.md`，更新功能介绍时同步三种语言。当前上传脚本使用简体中文介绍；首次发布时需另外设置工坊的繁体中文和英文本地化介绍。游戏内简短描述在 `manifest.xml`。发布包包含模组 DLL、清单、使用说明和许可证。

## 首次工坊发布

封面使用 `artwork/preview.png`（1024×1024），像素原稿为 `artwork/mouse-cover-128.png`。构建时将封面加入上传包，发布时同步更新。

首次创建条目时，将 `workshop.json` 中的 `publishedfileid` 设为 `0`，选择 `create-private`。Actions 的 `Build and release` 工作流提供三个手动操作：

- `build`：构建和检查发布包。
- `verify-steam`：验证 Steam 登录。
- `create-private`：首次发布时，从 `main` 创建一个**私有**工坊条目，上传后重新下载核对文件。

创建操作会输出 `created-workshop-id` artifact。任务后续失败时，先检查这个文件及 Steam 个人工坊列表，确认条目的创建状态。将取得的 ID 写回 `workshop.json` 并提交，确认私有条目内容后，在 Steam 条目页面调整封面和可见性。

公开发布确认完成后，将仓库变量 `STEAM_PUBLISH_ENABLED` 设为 `true`。之后推送版本标签自动更新已绑定条目；更新保留条目现有可见性。

## CI 配置

仓库 Secret `BUILD_REFERENCES_KEY` 是随机生成的 32 字节 Base64 密钥，解密 `.ci/references.bin`。依赖包包含项目引用的 10 个游戏程序集，源自 Steam build `26337366`。工作流由 `main` 提交、版本标签或手动操作触发。

游戏更新后，用新的游戏目录执行 `Tools/References.ps1 -Mode Seal -ManagedDir '游戏/Probably Stolen_Data/Managed'`，通过环境变量提供 `BUILD_REFERENCES_KEY`；如需更换密钥，必须同时更新 GitHub Secret 和加密依赖包。提交加密文件前重新解密并构建一次，更新这里记录的游戏版本。

`steam-workshop` Environment 保存：

| Secret | 内容 |
| --- | --- |
| `STEAM_USERNAME` | 有游戏及目标工坊条目权限的 Steam 账号名 |
| `STEAM_CONFIG_VDF_BASE64` | 已登录 SteamCMD 的 `config/config.vdf`，以 Base64 编码 |
| `STEAM_SESSION_KEY` | 随机 32 字节 Base64 密钥，用于加密会话缓存 |

脚本使用已有的 Steam 登录凭据。登录失效时在本机 SteamCMD 完成登录和 Steam Guard，再通过标准输入更新配置 Secret，运行 `verify-steam`。

每次运行将刷新的会话加密成 `steam-session-state` artifact，保留 90 天；下次优先恢复此缓存。更换种子登录配置时同时轮换 `STEAM_SESSION_KEY`，使旧缓存失效。

GitHub Release 已成功但工坊上传失败时，先确认目标条目的实际状态，再重跑失败任务。
