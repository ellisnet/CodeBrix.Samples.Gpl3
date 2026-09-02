=== Backup ===

All the application settings (the folders you have configured, custom snippets,
keyboard shortcuts, colour schemes and any other preferences) are saved in one
file, `settings.sqlite`, inside {appname}'s own folder in the per-user
application data directory.

If you want to backup your settings or use them on another computer, you must
know where to find it. The location depends on the operating system default.

== Linux and macOS ==

!`~/.config/CodeBrix/Fresco.Brix/settings/settings.sqlite`

== Windows ==

!`%APPDATA%\CodeBrix\Fresco.Brix\settings\settings.sqlite`, _(which is
normally)_
`C:\Users\<name>\AppData\Roaming\CodeBrix\Fresco.Brix\settings\settings.sqlite`.

The file is an ordinary SQLite database, so it can be copied, backed up and
inspected with any SQLite tool. Copy it while {appname} is not running.

#SEEALSO
snippet_import_export
