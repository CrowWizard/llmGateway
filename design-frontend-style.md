# Yaak 前端样式架构设计参考

> 本文档详细描述 Yaak 前端样式系统的设计，供构建类似桌面应用时参考。

---

## 1. 技术选型

| 项目 | 选型 | 版本 |
|------|------|------|
| CSS 框架 | Tailwind CSS（CSS-first 配置） | v4 |
| PostCSS 插件 | `@tailwindcss/postcss` | - |
| 类名合并 | `classnames`（非 cn/clsx/twMerge） | v2.5.1 |
| 动画库 | Framer Motion（`motion/react-m`） | - |
| 图标库 | lucide-react | - |
| 色彩空间 | OKLCH（感知均匀） | - |
| 主题注入 | 动态 `<style>` + `data-theme` 属性 | - |

**核心原则：** 不使用 CSS Modules、不使用 CSS-in-JS、不使用 `tailwind-merge`。所有样式通过 Tailwind 工具类 + `classnames` 组合实现。

---

## 2. Tailwind CSS v4 配置

### 2.1 入口 CSS（CSS-first，无 JS 配置）

```css
/* apps/yaak-client/main.css */
@import "tailwindcss";
@import "../../packages/tailwind-config/index.css";
@source "../../packages/ui/src";
```

- `@import "tailwindcss"` — v4 入口（替代 v3 的 `@tailwind base/components/utilities`）
- `@source` — 告诉 Tailwind 扫描哪些目录的类使用
- 无 `tailwind.config.js`，全部通过 CSS `@theme` 定义

### 2.2 PostCSS 配置

```js
// apps/yaak-client/postcss.config.cjs
module.exports = { plugins: [require("@tailwindcss/postcss")] };
```

### 2.3 设计令牌（packages/tailwind-config/index.css）

```css
/* 自定义变体 */
@custom-variant dark (&:is([data-resolved-appearance="dark"] *));
@custom-variant hocus (&:hover, &:focus-visible, &.focus:focus);

/* 重置默认值后定义自定义令牌 */
@theme inline {
  /* 文字大小 */
  --text-4xs: 0.6rem;
  --text-2xs: 0.75rem;
  --text-xs: 0.8rem;
  --text-sm: 0.9rem;
  --text-base: 1rem;
  --text-editor: var(--editor-font-size);
  --text-shrink: 0.8em;

  /* 颜色映射（来自主题 CSS 变量） */
  --color-surface: var(--surface);
  --color-surface-highlight: var(--surface-highlight);
  --color-surface-active: var(--surface-active);
  --color-text: var(--text);
  --color-text-subtle: var(--text-subtle);
  --color-text-subtlest: var(--text-subtlest);
  --color-border: var(--border);
  --color-border-subtle: var(--border-subtle);
  --color-border-focus: var(--border-focus);
  --color-shadow: var(--shadow);
  --color-backdrop: var(--backdrop);
  --color-selection: var(--selection);
  --color-primary: var(--primary);
  --color-secondary: var(--secondary);
  --color-info: var(--info);
  --color-success: var(--success);
  --color-notice: var(--notice);
  --color-warning: var(--warning);
  --color-danger: var(--danger);

  /* 组件尺寸令牌 */
  --height-2xs: 1.4rem;  --width-2xs: 1.4rem;
  --height-xs: 1.8rem;   --width-xs: 1.8rem;
  --height-sm: 2rem;     --width-sm: 2rem;
  --height-md: 2.3rem;   --width-md: 2.3rem;
  --height-lg: 2.6rem;   --width-lg: 2.6rem;

  /* 字体 */
  --font-mono: var(--font-family-editor), ui-monospace, monospace;
  --font-sans: var(--font-family-interface), system-ui, sans-serif;

  /* 动画 */
  --animate-blink-ring: blink-ring 1.5s ease-out;
}
```

---

## 3. 主题系统

### 3.1 主题数据结构

```typescript
Theme = {
  id: string;                    // 稳定标识符
  label: string;                 // 显示名称
  dark: boolean;                 // 暗色 or 亮色
  base: ThemeComponentColors;    // 19 个基础颜色键
  components?: ThemeComponents;  // 12 个组件级覆盖
}
```

**19 个基础颜色键：**

| 颜色键 | 用途 | 自动推导规则 |
|--------|------|-------------|
| `surface` | 主背景色 | 必须提供 |
| `surfaceHighlight` | 悬停高亮 | `surface.lift(6%)` |
| `surfaceActive` | 激活/选中 | `primary.lower(20%).translucify(80%)` |
| `text` | 主文本色 | 与 surface 对比度 ≥ 11:1 |
| `textSubtle` | 次要文本 | `text.lower(20%)` |
| `textSubtlest` | 最弱文本 | `text.lower(40%)` |
| `border` | 主边框 | 与 surface 对比度 ≥ 3:1 |
| `borderSubtle` | 弱边框 | 与 surface 对比度 ≥ 1.2:1 |
| `borderFocus` | 聚焦边框 | `info.translucify(50%)` |
| `shadow` | 阴影色 | `black.translucify(70%/93%)` |
| `backdrop` | 遮罩层 | `surface.lower(20%).translucify(20%)` |
| `selection` | 选区色 | `primary.lower(10%).translucify(70%)` |
| `primary` | 主题色 | 必须提供 |
| `secondary` | 次主题色 | 必须提供 |
| `info` | 信息色 | 必须提供 |
| `success` | 成功色 | 必须提供 |
| `notice` | 提示色 | 必须提供 |
| `warning` | 警告色 | 必须提供 |
| `danger` | 危险色 | 必须提供 |

**12 个组件级覆盖：**
`dialog`, `menu`, `toast`, `sidebar`, `responsePane`, `appHeader`, `button`, `banner`, `templateTag`, `urlBar`, `editor`, `input`

### 3.2 色彩引擎：YaakColor（OKLCH）

```typescript
// packages/theme/src/yaakColor.ts
class YaakColor {
  // 输入：任何 CSS 颜色（hex/HSL/RGB/oklch）
  // 内部：统一转为 OKLCH 色彩空间

  lift(amount: number): YaakColor;     // 亮色模式→变亮，暗色模式→变暗
  lower(amount: number): YaakColor;    // 亮色模式→变暗，暗色模式→变亮
  translucify(alpha: number): YaakColor;
  opacify(alpha: number): YaakColor;
  desaturate(amount: number): YaakColor;
  saturate(amount: number): YaakColor;
  withContrast(bg: YaakColor, minRatio: number): YaakColor;  // 二分搜索满足 WCAG 对比度
  compositeOver(background: YaakColor): YaakColor;
  css(): string;  // 输出 8 位 hex #RRGGBBAA
}
```

**核心设计：** `lift()` / `lower()` 的行为根据当前主题的 dark/light 属性自动反转，确保同一套颜色推导逻辑适用于两种模式。

### 3.3 CSS 变量生成

```typescript
// packages/theme/src/window.ts
function getThemeCSS(theme: Theme): string {
  // 1. 从 base 颜色补全所有 19 个变量
  const colors = completeFullColorVariables(theme.base);

  // 2. 为每个组件作用域生成覆盖
  const componentCSS = Object.entries(theme.components ?? {}).map(
    ([component, colors]) => `.x-theme-${component} { ${toCSSVars(colors)} }`
  );

  // 3. 为按钮/横幅/通知/模板标签生成颜色变体类
  //    .x-theme-button--solid--primary { ... }
  //    .x-theme-button--border--danger { ... }
  //    .x-theme-banner--success { ... }

  // 4. 包装在 [data-theme="${id}"] 选择器下
  return `[data-theme="${theme.id}"] { :root { ${toCSSVars(colors)} } ${componentCSS} }`;
}
```

### 3.4 主题应用

```typescript
function applyThemeToDocument(theme: Theme) {
  // 1. 注入 <style data-theme> 到 <head>
  const style = document.createElement("style");
  style.dataset.theme = theme.id;
  style.textContent = getThemeCSS(theme);
  document.head.appendChild(style);

  // 2. 设置 data-theme 属性到 <html>
  document.documentElement.dataset.theme = theme.id;
}
```

### 3.5 外观管理（亮/暗/跟随系统）

```typescript
// 三种光源：
// 1. CSS media query: prefers-color-scheme
// 2. Tauri 窗口主题: getCurrentWebviewWindow().theme()
// 3. Tauri 系统事件: system_appearance_change（Linux）

resolveAppearance(preferred: "system" | "light" | "dark"): "light" | "dark"
```

**暗色模式选择器（非 `prefers-color-scheme`）：**
```css
@custom-variant dark (&:is([data-resolved-appearance="dark"] *));
```

### 3.6 防止主题闪烁

```
1. index.html <head> 内联 <style> — 基于 prefers-color-scheme 设置背景色
2. theme.ts 脚本模块 — React 渲染前配置主题
3. font-size.ts — 设置 html 字号
4. font.ts — 设置 --font-family-editor/interface CSS 变量
5. main.tsx — React 渲染（此时主题已就绪）
```

---

## 4. 全局基础样式

```css
/* apps/yaak-client/main.css */
@layer base {
  html, body, #root {
    @apply h-full bg-surface text-text;
    font-family: var(--font-family-interface);
  }

  /* 禁用选择（应用行为） */
  :not(input):not(textarea):not(pre) {
    @apply select-none cursor-default;
  }
  a { @apply cursor-pointer; }

  /* 占位符文本 */
  ::placeholder { @apply text-placeholder; }

  /* 滚动条 */
  ::-webkit-scrollbar { @apply w-[8px] h-[8px] bg-transparent; }
  ::-webkit-scrollbar-track { @apply bg-transparent; }
  ::-webkit-scrollbar-thumb {
    @apply bg-text-subtlest rounded-[4px] opacity-20;
  }
  ::-webkit-scrollbar-thumb:hover { @apply opacity-40!; }

  /* 连字禁用 */
  font-variant-ligatures: none;
}

/* Linux 字体渲染修复 */
html[data-platform="linux"] {
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
}
```

---

## 5. 组件样式模式

### 5.1 classnames 组合模式

```tsx
import classNames from "classnames";

// 规则：
// 1. className prop 始终作为第一个参数（允许外部覆盖）
// 2. 布局类 → 条件变体类 → 状态类
<button className={classNames(
  className,                    // 外部覆盖
  "shrink-0 flex items-center", // 基础布局
  size === "md" && "h-md px-3 rounded-md",   // 尺寸变体
  variant === "solid" && "border-transparent", // 样式变体
  isDisabled && "pointer-events-none opacity-disabled", // 状态
)}>
```

### 5.2 变体/尺寸模式（非 cva，纯条件类）

```tsx
type ButtonVariant = "border" | "solid";
type ButtonSize = "2xs" | "xs" | "sm" | "md" | "auto";

// 尺寸映射到 Tailwind 类
const sizeClasses = {
  "2xs": "h-2xs px-2 text-xs rounded-sm",
  "xs":  "h-xs px-2 text-sm rounded-md",
  "sm":  "h-sm px-2.5 rounded-md",
  "md":  "h-md px-3 rounded-md",
  "auto": "px-3 py-2 rounded-md",
};

// Icon 尺寸映射
const iconSizeClasses = {
  "2xs": "h-2.5 w-2.5",
  "xs":  "h-3 w-3",
  "sm":  "h-3.5 w-3.5",
  "md":  "h-4 w-4",
  "lg":  "h-5 w-5",
  "xl":  "h-6 w-6",
};
```

### 5.3 主题变体类命名规范

```
.x-theme-{component}                    # 组件基础类
.x-theme-{component}--{variant}         # 变体（solid/border）
.x-theme-{component}--{variant}--{color} # 颜色变体

示例：
.x-theme-button
.x-theme-button--solid
.x-theme-button--solid--primary
.x-theme-button--solid--danger
.x-theme-banner--success
.x-theme-toast--warning
.x-theme-templateTag--info
```

### 5.4 图标系统

```tsx
// packages/ui/src/components/Icon.tsx
import * as LucideIcons from "lucide-react";

// 字符串 → Lucide 组件映射
const icons = {
  alarm_clock: AlarmClockIcon,
  check: CheckIcon,
  chevron_down: ChevronDownIcon,
  empty: (props) => <div {...props} />,
  _unknown: ShieldAlertIcon,  // 兜底图标
};

// 使用
<Icon icon="chevron_down" size="sm" className="text-text-subtle" />
```

### 5.5 布局系统

**SplitLayout** — CSS Grid + 拖拽调整：
```tsx
// 动态 grid template
style={{ gridTemplateColumns: `${ratio}fr 4px 1fr` }}
// 响应式：< 500px 自动切换为垂直布局
// 双击重置为默认比例
```

**SidebarLayout** — 三列 Grid：
```tsx
// grid: sidebar | drag-handle | body
// < 600px：sidebar 变为浮动覆盖层（Portal + motion 动画）
```

**ResizeHandle** — 自定义拖拽：
```tsx
// pointer-down/move/up 实现（无库依赖）
// 拖拽时创建全屏覆盖 div 捕获鼠标事件
// 支持 left/right/top 方向
```

**HStack/VStack** — Flex 封装：
```tsx
<HStack space={2} alignItems="center">
  {children}
</HStack>
// space → gap-{n} Tailwind 类映射
```

---

## 6. 窗口适配

### 6.1 macOS

- 使用原生红绿灯按钮（traffic lights）
- Header 左侧留白 `paddingLeft: 76 / interfaceScale`
- `data-tauri-drag-region` 实现窗口拖拽

### 6.2 Windows/Linux

- 自定义最小化/最大化/关闭按钮（SVG）
- Header 右侧留白 `paddingRight: 10.5rem`
- 关闭按钮悬停变 `bg-danger`
- Linux 额外添加 `border border-border-subtle`

```tsx
// packages/ui/src/components/WindowControls.tsx
// macOS: 隐藏（使用原生控件）
// Windows/Linux: 自定义 SVG 按钮
// 固定宽度 10.5rem（WINDOW_CONTROLS_WIDTH）
```

---

## 7. 动画系统

### 7.1 Framer Motion（组件级）

```tsx
// 全局配置（__root.tsx）
<MotionConfig transition={{ duration: 0.1 }}>
  <LazyMotion features={domAnimation}>
    {children}
  </LazyMotion>
</MotionConfig>
```

**典型动画：**

| 组件 | 动画 |
|------|------|
| Dialog | `top: 5→0, scale: 0.97→1` |
| Dropdown | `opacity: 0→1, y: -5→0, scale: 0.98→1` |
| Toast | 右侧滑入，退出 `opacity: 0, right: -100%` |
| 浮动侧边栏 | `opacity: 0→1, x: -20→0` |
| Toast 进度条 | `width: 100%→0%` |

### 7.2 Tailwind 内置动画

- `transition-colors` — ResizeHandle 颜色过渡
- `transition-opacity` — 工具提示淡入淡出
- `animate-spin` — LoadingIcon 旋转

---

## 8. 编辑器样式（CodeMirror）

```css
/* apps/yaak-client/components/core/Editor/Editor.css — 497 行 */
/* 使用 @reference 引入 Tailwind（v4 模式） */
@reference "../../../main.css";

/* 光标 */
.cm-cursor { @apply border-text! border-l-[2px]; }

/* 选区 */
.cm-selectionBackground { @apply bg-selection!; }

/* 行号 */
.cm-gutters { @apply border-0 text-text-subtlest bg-surface pr-1.5; }

/* 模板标签内联样式 */
.template-tag {
  @apply border border-border-subtle rounded bg-surface;
}
.template-tag:hover {
  @apply bg-surface-highlight;
}

/* 搜索高亮 */
.cm-searchMatch { @apply outline-1 outline-border-subtle; }

/* 自动补全 */
.cm-tooltip-autocomplete { @apply bg-surface border border-border rounded-md; }

/* 折叠指示器（纯 CSS 箭头） */
.cm-foldGutter::after {
  content: "";
  border-left: 4px solid currentColor;
  border-bottom: 4px solid currentColor;
  transform: rotate(-45deg);
}
```

---

## 9. 响应式与平台适配

### 9.1 容器查询

```tsx
<div className="@container border-b border-border-subtle py-4">
  <div className="@[30rem]:grid-cols-[minmax(0,1fr)_auto]">
    {/* 内容 */}
  </div>
</div>
```

### 9.2 JS 响应式

```tsx
// useContainerSize — ResizeObserver
const floating = containerSize.width <= FLOATING_BREAKPOINT; // 600px
const vertical = size.width < STACK_VERTICAL_WIDTH;         // 500px
```

### 9.3 平台检测

```css
/* CSS 层面 */
html[data-platform="linux"] { ... }
```

```tsx
// JS 层面
import { type } from "@tauri-apps/plugin-os";
type() === "macos" && doSomething();
```

---

## 10. 字体系统

```typescript
// 运行时从用户设置注入 CSS 变量
document.documentElement.style.fontSize = `${settings.interfaceFontSize}px`;
document.documentElement.style.setProperty("--font-family-editor", settings.editorFont);
document.documentElement.style.setProperty("--font-family-interface", settings.interfaceFont);
```

```css
/* Tailwind 中引用 */
--font-mono: var(--font-family-editor), ui-monospace, monospace;
--font-sans: var(--font-family-interface), system-ui, sans-serif;
```

---

## 11. 关键文件索引

| 文件 | 作用 |
|------|------|
| `apps/yaak-client/main.css` | 全局基础样式、Tailwind 入口 |
| `packages/tailwind-config/index.css` | 设计令牌、自定义变体、颜色映射 |
| `packages/theme/src/yaakColor.ts` | OKLCH 色彩引擎 |
| `packages/theme/src/window.ts` | CSS 变量生成、主题应用 |
| `packages/theme/src/defaultThemes.ts` | 默认明暗主题定义 |
| `packages/theme/src/appearance.ts` | 外观管理（亮/暗/系统） |
| `packages/ui/src/components/Button.tsx` | 按钮组件（变体/尺寸模式） |
| `packages/ui/src/components/Icon.tsx` | 图标组件（lucide-react 映射） |
| `packages/ui/src/components/SplitLayout.tsx` | 分割布局（CSS Grid + 拖拽） |
| `packages/ui/src/components/SidebarLayout.tsx` | 侧边栏布局（浮动断点） |
| `packages/ui/src/components/ResizeHandle.tsx` | 拖拽调整大小 |
| `packages/ui/src/components/WindowControls.tsx` | 窗口控件 |
| `apps/yaak-client/components/core/Editor/Editor.css` | CodeMirror 编辑器样式 |
| `plugins/themes-yaak/` | 社区主题插件集合 |

---

## 12. 复用建议

构建类似桌面应用的样式系统时：

1. **Tailwind CSS v4 + CSS-first**：用 `@theme inline` 定义令牌，无需 JS 配置文件
2. **OKLCH 色彩空间**：所有颜色计算在 OKLCH 中进行，确保感知均匀性
3. **`data-*` 属性驱动暗色模式**：比 `prefers-color-scheme` 更可控，支持跟随系统/手动切换
4. **`classnames` 而非 `twMerge`**：项目规模适中时 `classnames` 足够，减少依赖
5. **主题变量 → Tailwind 令牌映射**：`--color-surface: var(--surface)` 让主题与 Tailwind 无缝对接
6. **组件级主题覆盖**：`.x-theme-{component}` 类名规范，CSS 变量覆盖实现细粒度定制
7. **防闪烁策略**：内联 `<style>` + 早期脚本 → 主题在 React 渲染前就绪
8. **OKLCH `lift()`/`lower()` 自动反转**：一套颜色推导逻辑适配明暗两种模式
9. **WCAG 对比度自动保证**：`withContrast()` 二分搜索确保文本可读性
10. **平台适配**：`data-platform` 属性 + CSS 选择器处理 Linux/macOS/Windows 差异
