# Avalonia UI Redesign Plan

## Scope

Apply the Yaak-inspired visual language to the Avalonia desktop application while preserving all existing behavior. Do not change services, configuration models, commands, gateway flow, or configuration persistence.

## 1. Establish Design Tokens and Themes

- Split the inline styles in `LlmGateway.Desktop/App.axaml` into resource dictionaries:
  - `Styles/Tokens.axaml`
  - `Styles/Controls.axaml`
  - `Styles/Themes/Light.axaml`
  - `Styles/Themes/Dark.axaml`
- Use Avalonia `Color` and `SolidColorBrush` resources for Yaak-inspired semantic tokens:
  - `surface`, `surface-highlight`, `surface-active`
  - `text`, `text-subtle`, `text-subtlest`
  - `border`, `border-subtle`, `border-focus`
  - `primary`, `secondary`, `info`, `success`, `notice`, `warning`, `danger`
  - `shadow`, `backdrop`, `selection`
- Create component-level resource overrides for the sidebar, top application bar, form fields, buttons, status messages, and log panel.
- Maintain light and dark theme parity with accessible foreground/background contrast.

## 2. Standardize Global Controls

- Define compact, consistent styles for `Button`, `TextBox`, `ComboBox`, `NumericUpDown`, `CheckBox`, and `ListBox`.
- Provide class-based button variants: `primary`, `secondary`, `danger`, and `ghost`.
- Add clear hover, pressed, focus-visible, and disabled states.
- Standardize field heights, label spacing, border treatment, and corner radii at 8px or less.
- Use icon buttons for compact actions where Avalonia-supported icon assets are available, with tooltips for non-obvious actions.

## 3. Recompose the Main Window Layout

- Refactor `LlmGateway.Desktop/Views/MainWindow.axaml` into a desktop-tool layout:
  - Application bar with product identity and window-level status.
  - Left navigation rail or sidebar for configuration sections.
  - Scrollable primary workspace for the selected configuration area.
- Expose the existing sections through the navigation without changing their bindings or commands:
  - Connection configuration
  - Local compatibility gateway
  - Runtime logs
  - Backup and restore
  - ChatGPT launch
- Preserve narrow-window usability by collapsing to a single-column content flow at the existing minimum window dimensions.

## 4. Redesign Connection and Gateway Forms

- Present connection fields in dense two-column form rows when width permits.
- Restyle compatibility mode as a segmented mode selector while keeping the existing `CompatibilityMode` binding.
- Present gateway runtime state with a semantic status indicator and compact state label.
- Keep start and stop command bindings unchanged; restyle them as contextual control actions.
- Move runtime logs to a distinct terminal-like panel with monospaced text, restrained contrast, and a compact clear action.

## 5. Improve Feedback and Accessibility

- Restyle `GatewayStatus` and `CodexStatus` as a compact status area and semantic feedback surfaces rather than a permanent large card.
- Preserve all current status messages and their ViewModel sources.
- Ensure keyboard focus indicators, readable text contrast, text wrapping, and no clipping at minimum window size.
- Apply Linux font rendering settings and platform-specific window border adjustments where supported by Avalonia.

## 6. Theme and Window Adaptation

- Update `LlmGateway.Desktop/App.axaml.cs` only to load/apply appearance resources and follow the system theme when appropriate.
- Update `MainWindow.axaml` styles for platform-sensitive title-bar spacing and window borders.
- Do not alter `MainWindowViewModel`, services, models, command execution, persistence, or gateway behavior.

## Validation

- Build the desktop project with `dotnet build LlmGateway.Desktop/LlmGateway.Desktop.csproj`.
- Manually verify light and dark appearances, form focus states, compatibility mode visibility, start/stop state presentation, log visibility, and narrow-window layout.
- Confirm all existing commands and bindings still operate without changes to the service layer.
