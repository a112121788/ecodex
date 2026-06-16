# 浏览器 API

ECodex 集成浏览器基于 WebView2。命令行可以打开网页、读取可访问快照、定位元素、执行点击/输入/键盘/脚本和截图，适合本地开发 smoke、登录表单检查、页面状态读取与发布前验证。

## 推荐流程

| 步骤 | 命令 | 目的 |
| --- | --- | --- |
| 1. 打开页面 | `ecodex browser open https://example.com` | 创建或复用集成浏览器。 |
| 2. 记录引用 | 读取返回的 `surfaceRef` | 后续命令稳定定位目标 Surface。 |
| 3. 读取快照 | `ecodex browser snapshot --surfaceRef surface:1` | 查看 role、name、text、testid。 |
| 4. 执行动作 | `ecodex browser click --role button --name Submit` | 用 locator 驱动页面。 |
| 5. 验证结果 | `ecodex browser eval "document.title"` / `ecodex browser screenshot` | 读取状态或保存截图。 |

## 打开集成浏览器

```powershell
ecodex browser open https://example.com
ecodex browser new https://example.com
ecodex browser open-split https://example.com --direction right
```

| 命令 | 行为 |
| --- | --- |
| `open` | 优先复用当前集成浏览器；没有可用 Surface 时创建新的。 |
| `new` | 总是创建新的集成浏览器。 |
| `open-split` | 兼容入口；当前实现会创建新的集成浏览器，并返回 `fallbackMode: "new-surface"`。 |

返回值会包含 `surfaceId`、`surfaceRef`、`workspaceName`、`surfaceName`、`url` 等字段。

## Surface 引用

浏览器命令通过 `surfaceRef` 定位目标。常用格式是 `surface:<id>`，可以直接使用 `open` / `new` 的返回值：

```powershell
$response = ecodex --json browser open https://example.com | ConvertFrom-Json
$surfaceRef = $response.surfaceRef

ecodex browser snapshot --surfaceRef $surfaceRef
ecodex browser eval "document.title" --surfaceRef $surfaceRef
```

如果不传 `surfaceRef`，命令行会尝试使用当前选中的集成浏览器；如果当前 Surface 不是集成浏览器，则使用当前 Workspace 中第一个集成浏览器。

## browser snapshot 工作流

```powershell
ecodex browser snapshot --surfaceRef surface:1
```

`browser snapshot` 返回页面可访问树，节点包含：

| 字段 | 说明 |
| --- | --- |
| `nodeId` | ECodex 注入的临时节点 id，用于动作执行。 |
| `role` | 由显式 `role` 或 HTML 标签推导，例如 `button`、`link`、`textbox`。 |
| `name` | 来自 `aria-label`、`alt`、`title`、`placeholder`、value 或文本。 |
| `text` | 节点文本，最多保留一段摘要。 |
| `testId` | `data-testid` 或 `data-test-id`。 |
| `visible` | 是否可见。 |

推荐先 snapshot，再根据输出选择 `--testid`、`--text` 或 `--role`。

## Locator 定位器

命令行动作支持三种主定位方式：

```powershell
# 最稳定：推荐给可控页面加 data-testid
ecodex browser click --testid save-button --surfaceRef surface:1

# 适合文案稳定的按钮或链接
ecodex browser click --text "登录" --surfaceRef surface:1

# 适合语义明确的控件，可额外指定 name
ecodex browser click --role button --name Submit --surfaceRef surface:1
```

| 参数 | 匹配规则 |
| --- | --- |
| `--testid` / `--test-id` | 精确匹配 `data-testid` 或 `data-test-id`。 |
| `--text` | 在节点 `text` 或 `name` 中做不区分大小写的包含匹配。 |
| `--role` + `--name` | `role` 不区分大小写匹配；`name` 可选。 |

Core 契约中还保留 `find.first`、`find.last`、`find.nth` 组合定位；命令行当前动作默认取第一个匹配节点执行。

## 动作命令

| 命令行 | Pipe 命令 | 说明 |
| --- | --- | --- |
| `ecodex browser click` | `BROWSER.CLICK` | 点击第一个匹配元素。 |
| `ecodex browser fill` | `BROWSER.FILL` | 输入文本；空字符串会清空 input。 |
| `ecodex browser hover` | `BROWSER.HOVER` | 触发 `mouseover` / `mouseenter`。 |
| `ecodex browser press` | `BROWSER.PRESS` | 对匹配元素发送键盘事件；默认 `Enter`。 |
| `ecodex browser eval` | `BROWSER.EVAL` | 执行 JavaScript 并返回 JSON 值。 |
| `ecodex browser screenshot` | `BROWSER.SCREENSHOT` | 返回 PNG 的 base64 JSON。 |

示例：

```powershell
ecodex browser fill --testid email --value user@example.com --surfaceRef surface:1
ecodex browser fill password "secret" --surfaceRef surface:1
ecodex browser press --testid password --key Enter --surfaceRef surface:1
ecodex browser eval "document.location.href" --surfaceRef surface:1
ecodex browser screenshot --surfaceRef surface:1
```

`screenshot` 返回 base64 JSON。如需落盘，可在 PowerShell 中解码：

```powershell
$shot = ecodex --json browser screenshot --surfaceRef surface:1 | ConvertFrom-Json
[IO.File]::WriteAllBytes("browser.png", [Convert]::FromBase64String($shot.result.value.data))
```

## 自动化脚本示例

```powershell
$open = ecodex --json browser new https://example.com/login | ConvertFrom-Json
$ref = $open.surfaceRef

ecodex browser snapshot --surfaceRef $ref
ecodex browser fill --testid email --value user@example.com --surfaceRef $ref
ecodex browser fill --testid password --value "secret" --surfaceRef $ref
ecodex browser click --role button --name "登录" --surfaceRef $ref

$title = ecodex --json browser eval "document.title" --surfaceRef $ref | ConvertFrom-Json
$title.result.value
```

## 状态与控制契约

核心浏览器契约 预留了状态与控制类方法；如果当前运行时没有接线，会稳定返回 `not_supported` 或对应错误码。

| 契约方法 | 说明 |
| --- | --- |
| `browser.cookies.get` / `browser.cookies.set` / `browser.cookies.clear` | 读取、写入、清理 cookie。 |
| `browser.storage.get` / `browser.storage.set` / `browser.storage.clear` | 操作 local/session storage。 |
| `browser.console.list` / `browser.console.clear` | 读取或清理 console 事件。 |
| `browser.dialog.accept` / `browser.dialog.dismiss` | 处理 dialog。 |
| `browser.download.wait` | 等待下载完成。 |
| `browser.highlight` | 高亮目标元素，辅助调试。 |
| `browser.addinitscript` / `browser.addscript` / `browser.addstyle` | 注入脚本或样式。 |

## not_supported 矩阵

当前阶段明确返回 `not_supported` 的能力包括：

- `browser.viewport.*`
- `browser.geolocation.*`
- `browser.offline.*`
- `browser.trace.*`
- `browser.network.route`
- `browser.screencast.*`
- `browser.input_mouse`
- `browser.input_keyboard`
- `browser.input_touch`

稳定错误码：`invalid_ref`、`not_found`、`stale_ref`、`not_supported`、`timeout`、`internal_error`。

## 排查建议

| 现象 | 建议 |
| --- | --- |
| 找不到集成浏览器 | 先运行 `ecodex browser open <url>`，或显式传 `--surfaceRef`。 |
| locator 找不到元素 | 运行 `ecodex browser snapshot --surfaceRef <ref>`，确认 `--testid`、`--text`、`--role` 是否存在。 |
| 动作报 `stale_ref` | 页面刷新后节点 id 失效；重新 snapshot 后再执行。 |
| `eval` 失败 | 检查脚本是否能在页面上下文中执行；严格 CSP 页面可能限制脚本注入。 |
| 截图无法直接打开 | `ecodex browser screenshot` 返回 base64 JSON，需要先解码保存。 |
