# Window Placement

`WindowSetup` is an autoload that can place and pin the game window at startup.

Configuration keys in `project.godot`:

```ini
[application]
config/custom_window_target_screen=-1
config/custom_window_always_on_top=false
```

- `custom_window_target_screen = -1` disables monitor forcing.
- Set `0`, `1`, `2`, ... to target a specific monitor index.
- `custom_window_always_on_top` controls the window flag at runtime.

When a target monitor is set, `WindowSetup` centers the window inside `ScreenGetUsableRect` to avoid taskbar overlap.
