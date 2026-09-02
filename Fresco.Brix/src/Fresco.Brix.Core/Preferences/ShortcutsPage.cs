// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Widgets;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/shortcuts.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Shortcuts page: every command in the window with the keys that trigger
/// it, per named shortcut scheme, and a search box to find one.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: upstream builds its tree by WALKING THE MENU BAR, so the
/// commands read in menu order, and puts whatever is left over into groups by
/// their collection's title. The menus here are built from the collections
/// (<see cref="MenuBuilder"/>) rather than the other way round, so the
/// collections ARE the grouping — every command appears exactly once, under the
/// title its collection gives itself, and a collection with no title of its own
/// is grouped under its name.
/// </para>
/// <para>
/// The snippet shortcuts are in the list like any others: <c>SnippetShortcuts</c>
/// is an action collection titled "Snippets", so a user's own snippet keys are
/// edited in the same place as the built-in commands.
/// </para>
/// <para>
/// Nothing is written until the dialog is accepted: every edit lands in this
/// page's own per-scheme map, which is what lets Cancel mean cancel and what
/// lets a scheme that is NOT in force be edited.
/// </para>
/// </remarks>
public sealed class ShortcutsPage : PreferencesPage
{
    private readonly Dictionary<string, Dictionary<string, IReadOnlyList<KeySequence>>> _edits
        = new Dictionary<string, Dictionary<string, IReadOnlyList<KeySequence>>>(
            StringComparer.Ordinal);

    private readonly List<ShortcutEntry> _all = new List<ShortcutEntry>();
    private readonly List<ShortcutEntry> _shown = new List<ShortcutEntry>();

    private SchemeSelector _scheme;
    private TextBox _search;
    private ListView _list;
    private Button _edit;

    /// <summary>Creates the page.</summary>
    /// <param name="context">What the page configures.</param>
    public ShortcutsPage(PreferencesContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Shortcuts");

    /// <inheritdoc/>
    public override string Help => "prefs_shortcuts";

    /// <inheritdoc/>
    public override string IconName => "preferences-desktop-keyboard-shortcuts";

    /// <inheritdoc/>
    public override void LoadSettings()
    {
        _edits.Clear();
        _scheme.LoadSettings(Settings, SchemeSettingKey, SchemeNamesKey);
        BuildEntries();
        Refresh();
    }

    /// <inheritdoc/>
    public override void SaveSettings()
    {
        //was previously: the prefix "shortcuts", which SchemeSelector deleted
        //by key prefix. A removed scheme's shortcuts are now dropped by the
        //collection that owns them (board W13 item 9, route (a)).
        _scheme.SaveSettings(Settings, SchemeSettingKey, SchemeNamesKey,
            scheme => ActionCollection.ForgetScheme(Settings, scheme));

        foreach (var scheme in _edits)
        {
            foreach (var edit in scheme.Value)
            {
                //The key is "<collection>/<action>" — the two halves the
                //settings key wants, kept together so one map holds them all.
                int slash = edit.Key.IndexOf('/');
                if (slash <= 0) { continue; }

                string collectionName = edit.Key.Substring(0, slash);
                string actionName = edit.Key.Substring(slash + 1);
                ActionCollection collection = Collections()
                    .FirstOrDefault(c => string.Equals(
                        c.Name, collectionName, StringComparison.Ordinal));
                collection?.SetShortcutsInScheme(scheme.Key, actionName, edit.Value);
            }
        }

        //The live commands follow whatever scheme is now in force, so a rebound
        //key works at once rather than at the next launch.
        foreach (var collection in Collections()) { collection.Load(true); }

        _edits.Clear();
    }

    /// <inheritdoc/>
    protected override UIElement Build()
    {
        _scheme = new SchemeSelector { DialogRoot = DialogRoot };
        _scheme.CurrentChanged += (_, _) => Refresh();
        _scheme.Changed += (_, _) => MarkChanged();

        _search = new TextBox
        {
            PlaceholderText = I18n.Get("Search..."),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _search.TextChanged += (_, _) => Refresh();

        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = RowTemplate(),
            MinHeight = 360,
        };
        _list.SelectionChanged += (_, _) => UpdateEditButton();

        //⚠ ItemInvoked/Tapped fires on a SINGLE click (board trap 45), which
        //would open the editor while the user is only looking; upstream edits
        //on a double click too.
        _list.DoubleTapped += async (_, _) => await EditCurrentAsync();

        _edit = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
        _edit.Click += async (_, _) => await EditCurrentAsync();

        return Stack(_scheme, _search, _list, _edit);
    }

    /// <summary>The setting naming the shortcut scheme in force.</summary>
    /// <remarks>Upstream's own key, and the one
    /// <see cref="ActionCollection"/> already reads.</remarks>
    private const string SchemeSettingKey = "shortcut_scheme";

    /// <summary>The settings group holding the shortcut schemes' names.</summary>
    private const string SchemeNamesKey = "shortcut_schemes";

    private static DataTemplate RowTemplate()
    {
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<Grid ColumnSpacing=\"8\">"
            + "<Grid.ColumnDefinitions><ColumnDefinition Width=\"*\" />"
            + "<ColumnDefinition Width=\"Auto\" /></Grid.ColumnDefinitions>"
            + "<TextBlock Grid.Column=\"0\" Text=\"{Binding Label}\" "
            + "FontWeight=\"{Binding Weight}\" Margin=\"{Binding Indent}\" "
            + "TextTrimming=\"CharacterEllipsis\" />"
            + "<TextBlock Grid.Column=\"1\" Text=\"{Binding Shortcut}\" Opacity=\"0.75\" />"
            + "</Grid></DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private IEnumerable<ActionCollection> Collections()
        => Context.Actions?.Collections ?? Enumerable.Empty<ActionCollection>();

    private void BuildEntries()
    {
        _all.Clear();

        foreach (var collection in Collections()
            .OrderBy(c => c.Title ?? c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            List<ShortcutEntry> rows = new List<ShortcutEntry>();
            foreach (var pair in collection.Actions
                .OrderBy(a => MenuBuilder.Display(a.Value.Text ?? a.Key),
                    StringComparer.CurrentCultureIgnoreCase))
            {
                if (string.IsNullOrEmpty(pair.Value.Text)) { continue; }

                rows.Add(ShortcutEntry.Command(collection, pair.Key, pair.Value));
            }

            if (rows.Count == 0) { continue; }

            //Upstream's own heading shape: the collection's title with a colon.
            _all.Add(ShortcutEntry.Heading((collection.Title ?? collection.Name) + ":"));
            _all.AddRange(rows);
        }
    }

    private Dictionary<string, IReadOnlyList<KeySequence>> EditsFor(string scheme)
    {
        if (!_edits.TryGetValue(scheme, out var map))
        {
            map = new Dictionary<string, IReadOnlyList<KeySequence>>(StringComparer.Ordinal);
            _edits[scheme] = map;
        }

        return map;
    }

    /// <summary>What an action's shortcuts are in a scheme, edits included.</summary>
    /// <param name="entry">The row.</param>
    /// <param name="scheme">The scheme.</param>
    /// <returns>The shortcuts.</returns>
    private IReadOnlyList<KeySequence> ShortcutsOf(ShortcutEntry entry, string scheme)
        => _edits.TryGetValue(scheme, out var map)
            && map.TryGetValue(entry.Key, out var edited)
                ? edited
                : entry.Collection.ShortcutsInScheme(scheme, entry.Name);

    /// <summary>Whether a row is still on its defaults in a scheme.</summary>
    /// <param name="entry">The row.</param>
    /// <param name="scheme">The scheme.</param>
    /// <returns>Whether it is.</returns>
    private bool IsDefault(ShortcutEntry entry, string scheme)
    {
        IReadOnlyList<KeySequence> current = ShortcutsOf(entry, scheme);
        IReadOnlyList<KeySequence> defaults =
            entry.Collection.DefaultShortcuts(entry.Name);
        return current.Count == defaults.Count
            && current.Zip(defaults, (a, b) => a.Equals(b)).All(same => same);
    }

    private void Refresh()
    {
        if (_list == null) { return; }

        string scheme = _scheme.CurrentSchemeKey;
        string search = (_search.Text ?? string.Empty).Trim();

        foreach (var entry in _all)
        {
            if (entry.IsHeading) { continue; }

            IReadOnlyList<KeySequence> shortcuts = ShortcutsOf(entry, scheme);
            entry.Shortcut = Describe(shortcuts, IsDefault(entry, scheme));
        }

        _shown.Clear();
        if (search.Length == 0)
        {
            _shown.AddRange(_all);
        }
        else
        {
            //A heading is shown only when something under it matched, which is
            //what upstream's hidechildren does.
            List<ShortcutEntry> pending = new List<ShortcutEntry>();
            ShortcutEntry heading = null;
            foreach (var entry in _all)
            {
                if (entry.IsHeading)
                {
                    Flush(heading, pending);
                    heading = entry;
                    pending.Clear();
                    continue;
                }

                if (Matches(entry, search)) { pending.Add(entry); }
            }

            Flush(heading, pending);
        }

        int selected = _list.SelectedIndex;
        _list.ItemsSource = null;
        _list.ItemsSource = _shown.ToList();
        _list.SelectedIndex = selected >= 0 && selected < _shown.Count ? selected : -1;
        UpdateEditButton();

        void Flush(ShortcutEntry title, List<ShortcutEntry> rows)
        {
            if (title == null || rows.Count == 0) { return; }

            _shown.Add(title);
            _shown.AddRange(rows);
        }
    }

    private static bool Matches(ShortcutEntry entry, string search)
        => entry.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || (entry.Shortcut ?? string.Empty).Contains(
                search, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>How a row shows its shortcuts.</summary>
    /// <param name="shortcuts">The shortcuts.</param>
    /// <param name="isDefault">Whether they are the action's own defaults.</param>
    /// <returns>The text.</returns>
    /// <remarks>Upstream shows the FIRST shortcut with an ellipsis when there
    /// are more, and adds "(default)" when nothing was customised.</remarks>
    private static string Describe(IReadOnlyList<KeySequence> shortcuts, bool isDefault)
    {
        if (shortcuts.Count == 0) { return isDefault ? string.Empty : string.Empty; }

        string text = shortcuts[0].ToString();
        if (shortcuts.Count > 1) { text += "..."; }

        return isDefault ? text + "  " + I18n.Get("(default)") : text;
    }

    private ShortcutEntry Current()
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count
            ? _shown[_list.SelectedIndex]
            : null;

    private void UpdateEditButton()
    {
        ShortcutEntry entry = Current();
        if (entry == null || entry.IsHeading)
        {
            _edit.Content = I18n.Get("(no shortcut)");
            _edit.IsEnabled = false;
            return;
        }

        _edit.Content = MenuBuilder.Display(I18n.Format(
            I18n.Get("&Edit Shortcut for \"{name}\""), ("name", entry.Label)));
        _edit.IsEnabled = true;
    }

    private async Task EditCurrentAsync()
    {
        ShortcutEntry entry = Current();
        if (entry == null || entry.IsHeading) { return; }

        string scheme = _scheme.CurrentSchemeKey;
        ShortcutEditDialog dialog = new ShortcutEditDialog(
            shortcut => FindConflict(shortcut, entry, scheme));

        IReadOnlyList<KeySequence> chosen = await dialog.EditAsync(
            DialogRoot,
            entry.Label,
            ShortcutsOf(entry, scheme),
            entry.Collection.DefaultShortcuts(entry.Name));
        if (chosen == null) { return; }

        //Whatever the new keys collide with LOSES them, which is upstream's own
        //resolution: a shortcut belongs to one command.
        foreach (var other in _all.Where(e => !e.IsHeading && e != entry))
        {
            IReadOnlyList<KeySequence> theirs = ShortcutsOf(other, scheme);
            List<KeySequence> kept = theirs
                .Where(s => !chosen.Any(c => c.Equals(s)))
                .ToList();
            if (kept.Count != theirs.Count)
            {
                EditsFor(scheme)[other.Key] = kept;
            }
        }

        EditsFor(scheme)[entry.Key] = chosen;
        Refresh();
        MarkChanged();
    }

    private string FindConflict(KeySequence shortcut, ShortcutEntry editing, string scheme)
    {
        if (shortcut == null) { return null; }

        foreach (var entry in _all)
        {
            if (entry.IsHeading || entry == editing) { continue; }

            if (ShortcutsOf(entry, scheme).Any(s => s.Equals(shortcut)))
            {
                return entry.Label;
            }
        }

        return null;
    }

    /// <summary>One row of the shortcuts list.</summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class ShortcutEntry
    {
        /// <summary>Gets the row's text.</summary>
        public string Label { get; private set; }

        /// <summary>Gets the shortcut text shown on the right.</summary>
        public string Shortcut { get; set; }

        /// <summary>Gets the collection the command belongs to, or null.</summary>
        public ActionCollection Collection { get; private set; }

        /// <summary>Gets the command's name, or null for a heading.</summary>
        public string Name { get; private set; }

        /// <summary>Gets whether the row is a group heading.</summary>
        public bool IsHeading => Collection == null;

        /// <summary>Gets the map key the page edits this row under.</summary>
        public string Key => Collection == null ? null : Collection.Name + "/" + Name;

        /// <summary>Gets the weight the row is drawn in.</summary>
        public Windows.UI.Text.FontWeight Weight
            => IsHeading ? FontWeights.SemiBold : FontWeights.Normal;

        /// <summary>Gets the row's left margin, which shows its depth.</summary>
        public Thickness Indent
            => IsHeading ? new Thickness(0) : new Thickness(16, 0, 0, 0);

        /// <summary>Makes a group heading.</summary>
        /// <param name="label">Its text.</param>
        /// <returns>The row.</returns>
        public static ShortcutEntry Heading(string label)
            => new ShortcutEntry { Label = label };

        /// <summary>Makes a command row.</summary>
        /// <param name="collection">The command's collection.</param>
        /// <param name="name">The command's name.</param>
        /// <param name="action">The command.</param>
        /// <returns>The row.</returns>
        public static ShortcutEntry Command(
            ActionCollection collection, string name, AppAction action)
            => new ShortcutEntry
            {
                Collection = collection,
                Name = name,
                //The accelerator marker is stripped for display, as everywhere.
                Label = MenuBuilder.Display(action.Text),
            };
    }
}
