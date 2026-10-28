# HOTFIX: left stepper unusable (headers "dead", step bodies never visible)

## Symptom
After loading a model the six accordion headers (Model, Applied Loads, Fixed Supports, Material, Resolution & Analysis, Optimise) gave no hover feedback and nothing opened, so loads, supports, material and the optimisation could not be set up from the UI. The UI thread was healthy (max gap ~111 ms).

## Method
Evidence first, same as `HOTFIX_ORBIT.md`: debug switch `--test-stepper` (`MainWindow.RunStepperDiagnosticTestAsync`, log `~/.local/share/Mycel/logs/stepper_diag.log`; `--diag-pointer` adds a 25 s pointer-event trace for manual runs).

## Evidence (before the fix)
```
Header1..6: enabled=False hitTestVisible=True hit=Avalonia.Controls.Border
ObjectConverters.Equal.Convert(int 1, "1") = False
ObjectConverters.Equal.Convert(int 1, int 1) = True
SelectStep(N) -> ActiveStep=N; B1=False, B2=False, B3=False, B4=False, B5=False, B6=False   (for N = 1..6)
```

## Root cause (two defects with one origin: XAML `CommandParameter="N"` / `ConverterParameter=N` is a **string**, the ViewModel uses an **int**)
1. **Headers disabled.** `[RelayCommand] SelectStep(int)` with `CommandParameter="1"` (string): CommunityToolkit's `RelayCommand<int>.CanExecute("1")` is false, so every header `Button` had `IsEffectivelyEnabled = False`. A disabled button has no hover and ignores clicks. Not a hit-testing problem: pointer events were fine, the buttons were simply disabled.
2. **Bodies never visible.** `IsVisible="{Binding ActiveStep, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=N}"` compares `int` with `"N"` strictly, always false (proved by the probe), so even a programmatic `ActiveStep` change showed nothing.
3. (Cosmetic, secondary) the headers had a local `Background="Transparent"`, which in Fluent suppresses the pointer-over/pressed template.

Hypotheses H3 (hit-test / capture, the earlier `ICustomHitTest` fix) and H4 (Wayland input) were discarded: the headers are hit-testable (`hitTestVisible=True`) and all events reach them.

## Minimal fix
- `MainWindowViewModel`: `SelectStep(int)` is now a plain method; new `[RelayCommand] SelectStepFromHeader(string?)` parses the XAML string (accepts 1..6, ignores anything else). Typed flags `IsStep1Active..IsStep6Active`, raised from `OnActiveStepChanged`.
- `MainWindow.axaml`: headers bind `SelectStepFromHeaderCommand`; bodies bind `IsVisible="{Binding IsStepNActive}"`; bodies and headers have `x:Name` (`StepNBody`, `StepNHeader`).
- Header style (`Window.Styles`): class `step-header` with pointer-over, pressed, keyboard focus-visible and active-step visuals; local `Background` removed. Headers stay focusable (Tab, Enter/Space).
- The only remaining `ObjectConverters.Equal` (load direction `CustomVector`, line with `{x:Static wf:LoadDirectionMode.CustomVector}`) uses a typed enum parameter and is correct.

## Evidence (after the fix)
```
Header1..6: enabled=True  hit=ContentPresenter/TextBlock
SelectStep(N) -> ActiveStep=N; exactly Bn=True, all others False   (N = 1..6)
DISABLED buttons left (all legitimate): "Run Static Analysis" (hidden), NumericUpDown decrease, "Start Optimisation" (model not prepared yet)
```

## Tests
`tests/Mycel.Core.Tests/App/StepperTests.cs` (13 cases): string parameter is executable and activates exactly one step; invalid parameters are ignored; `PropertyChanged` for all flags; XAML regression guards (no `ActiveStep` + `ObjectConverters.Equal` + numeric parameter, no typed `SelectStepCommand`, bodies bound to typed flags, headers without local `Background`). Existing suite unchanged.

## Lesson
In Avalonia XAML, `CommandParameter=1` and `ConverterParameter=1` are strings. Typed commands and `ObjectConverters.Equal` against an `int` fail silently (disabled button / always-false visibility). Use string-parsing commands or typed bool properties.

