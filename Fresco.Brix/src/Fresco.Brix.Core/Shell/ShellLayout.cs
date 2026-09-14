// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Text.Json.Serialization;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One of the four places the window's working area is divided into.</summary>
public enum ShellRegion
{
    /// <summary>The left tool strip — Quick Insert, Documents, Outline and their neighbours.</summary>
    Left,

    /// <summary>The right tool strip — the Music View and its neighbours.</summary>
    Right,

    /// <summary>The bottom tool strip — the Log, the MIDI player and the snippets.</summary>
    Bottom,

    /// <summary>The editor itself, which is always there whether a tool is open or not.</summary>
    Editor,
}

/// <summary>
/// Which panes of the shell's two nested pane controls are meant to be open.
/// </summary>
/// <remarks>
/// <para>
/// The window's working area is two nested CodeBrix.Platform <c>TriPaneView</c>
/// controls, and these five flags are the five panes it actually uses: the
/// outer control's side pane (the right strip), its upper pane (the whole
/// editor block, which IS the inner control) and its lower pane (the bottom
/// strip), then the inner control's side pane (the left strip) and its upper
/// pane (the editor). The inner control's lower pane is not one of the window's
/// regions and is never opened.
/// </para>
/// <para>
/// <see cref="LeftStripIsOpen"/> and <see cref="EditorIsOpen"/> describe the
/// INNER control, and they say something only while
/// <see cref="EditorBlockIsOpen"/> is true. An inner control that is not on
/// screen is left exactly as it was, so that whatever the user had arranged
/// inside it comes back untouched when the outer upper pane opens again.
/// </para>
/// </remarks>
public sealed class ShellPanes
{
    /// <summary>Creates a description of the five panes.</summary>
    /// <param name="rightStripIsOpen">Whether the right strip is open.</param>
    /// <param name="editorBlockIsOpen">Whether the editor block — the left
    /// strip and the editor together — is open.</param>
    /// <param name="bottomStripIsOpen">Whether the bottom strip is open.</param>
    /// <param name="leftStripIsOpen">Whether the left strip is open, which
    /// means something only when the editor block is.</param>
    /// <param name="editorIsOpen">Whether the editor is open, which means
    /// something only when the editor block is.</param>
    public ShellPanes(
        bool rightStripIsOpen,
        bool editorBlockIsOpen,
        bool bottomStripIsOpen,
        bool leftStripIsOpen,
        bool editorIsOpen)
    {
        RightStripIsOpen = rightStripIsOpen;
        EditorBlockIsOpen = editorBlockIsOpen;
        BottomStripIsOpen = bottomStripIsOpen;
        LeftStripIsOpen = leftStripIsOpen;
        EditorIsOpen = editorIsOpen;
    }

    /// <summary>Gets whether the right strip — the outer side pane — is open.</summary>
    public bool RightStripIsOpen { get; }

    /// <summary>Gets whether the editor block — the outer upper pane, which
    /// holds the inner control — is open.</summary>
    public bool EditorBlockIsOpen { get; }

    /// <summary>Gets whether the bottom strip — the outer lower pane — is open.</summary>
    public bool BottomStripIsOpen { get; }

    /// <summary>Gets whether the left strip — the inner side pane — is open.</summary>
    public bool LeftStripIsOpen { get; }

    /// <summary>Gets whether the editor — the inner upper pane — is open.</summary>
    public bool EditorIsOpen { get; }

    /// <summary>Gets whether the outer control would keep at least one pane open.</summary>
    /// <remarks>A pane control refuses a request that would leave it with
    /// nothing open at all, so nothing the shell asks for may ever come to
    /// that: this is the reading that says it never does.</remarks>
    public bool OuterKeepsAPaneOpen
        => RightStripIsOpen || EditorBlockIsOpen || BottomStripIsOpen;

    /// <summary>Gets whether the inner control would keep at least one pane open.</summary>
    /// <remarks>Read only while <see cref="EditorBlockIsOpen"/> is true; the
    /// inner control is left alone otherwise.</remarks>
    public bool InnerKeepsAPaneOpen => LeftStripIsOpen || EditorIsOpen;
}

/// <summary>
/// How the window's working area shares its room out: which of its panes are
/// open, and what proportion of each divider's axis each side of it gets.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of every CodeBrix.Platform and XAML type, so all of it can
/// be asserted in a test process that has no window. <see cref="DockShell"/> is
/// a thin driver over the answers given here.
/// </para>
/// <para>
/// The six percents are one pair per divider: the outer side divider splits the
/// right strip from everything else, the outer stack divider splits the editor
/// block from the bottom strip, and the inner side divider splits the left
/// strip from the editor. They are star weights — only the ratio inside a pair
/// matters — and the control writes a pair back normalized to 100 when the user
/// finishes dragging it. The inner control's upper and lower weights are not
/// here because they never change: the editor has the inner stack to itself.
/// </para>
/// <para>
/// The defaults keep the editor the lion's share it has always had: a tool
/// strip takes about a quarter of the width and the bottom strip about a fifth
/// of the height, which is the 3:1 and 4:1 the shell used to recompute on every
/// show and hide.
/// </para>
/// <para>
/// ORDER MATTERS TO A DRIVER, and it is the same order for both controls: open
/// every pane that is to be open BEFORE shutting any that is to be shut.
/// Reversing that can ask a control to shut its last open pane, which it
/// refuses outright, leaving the layout somewhere neither the user nor the
/// shell asked for.
/// </para>
/// </remarks>
public sealed class ShellLayout
{
    /// <summary>The right strip's default share of the window's width.</summary>
    public const double DefaultOuterSidePercent = 25d;

    /// <summary>The default share of the width left for everything else.</summary>
    public const double DefaultOuterStackPercent = 75d;

    /// <summary>The editor block's default share of that column's height.</summary>
    public const double DefaultOuterUpperPercent = 80d;

    /// <summary>The bottom strip's default share of that column's height.</summary>
    public const double DefaultOuterLowerPercent = 20d;

    /// <summary>The left strip's default share of the editor block's width.</summary>
    /// <remarks>Wider than the right strip's quarter, and deliberately so. The
    /// left strip holds Quick Insert, whose four-tab row and whose
    /// "Allow shorthands" line are the widest fixed content any tool panel has.
    /// Measured on X11 at the default window size with the Music View open —
    /// the arrangement the strip is narrowest in — this gives the strip 380
    /// pixels, which is where the tab row ends whole and the Direction list,
    /// the check box and the Remove button all read in full; at a fifth it sat
    /// on its 200-pixel floor with the tab row cut after "Dynamics", and even a
    /// third left the last tab clipped. The editor still keeps the larger share
    /// of its own block, and the 200-pixel floor under the divider is
    /// unchanged: this is where the divider OPENS, not how far it can go.
    /// </remarks>
    public const double DefaultInnerSidePercent = 40d;

    /// <summary>The editor's default share of the editor block's width.</summary>
    public const double DefaultInnerStackPercent = 60d;

    /// <summary>Gets or sets the right strip's share of the window's width.</summary>
    public double OuterSidePercent { get; set; } = DefaultOuterSidePercent;

    /// <summary>Gets or sets the share of the width left for everything else.</summary>
    public double OuterStackPercent { get; set; } = DefaultOuterStackPercent;

    /// <summary>Gets or sets the editor block's share of that column's height.</summary>
    public double OuterUpperPercent { get; set; } = DefaultOuterUpperPercent;

    /// <summary>Gets or sets the bottom strip's share of that column's height.</summary>
    public double OuterLowerPercent { get; set; } = DefaultOuterLowerPercent;

    /// <summary>Gets or sets the left strip's share of the editor block's width.</summary>
    public double InnerSidePercent { get; set; } = DefaultInnerSidePercent;

    /// <summary>Gets or sets the editor's share of the editor block's width.</summary>
    public double InnerStackPercent { get; set; } = DefaultInnerStackPercent;

    /// <summary>Gets whether these shares can be given to the controls as they stand.</summary>
    /// <remarks>A share of zero means "that pane is shut", which is the panel
    /// toggles' business and not the stored arrangement's, and a share that is
    /// not a real number at all cannot be laid out: either way what is stored
    /// is not usable and the shell opens at its defaults. This is also what
    /// makes an arrangement written by the shell that came before this one
    /// harmless — it carries no shares at all, so it reads as unusable rather
    /// than being replayed into controls it was never written for.</remarks>
    [JsonIgnore]
    public bool IsUsable
        => IsUsableShare(OuterSidePercent)
            && IsUsableShare(OuterStackPercent)
            && IsUsableShare(OuterUpperPercent)
            && IsUsableShare(OuterLowerPercent)
            && IsUsableShare(InnerSidePercent)
            && IsUsableShare(InnerStackPercent);

    /// <summary>Which region a tool panel's dock area is.</summary>
    /// <param name="area">The dock area.</param>
    /// <returns>The region.</returns>
    public static ShellRegion RegionOf(DockArea area)
        => area switch
        {
            DockArea.Left => ShellRegion.Left,
            DockArea.Right => ShellRegion.Right,
            _ => ShellRegion.Bottom,
        };

    /// <summary>
    /// Which panes are open when each tool strip holds whatever it holds.
    /// </summary>
    /// <param name="leftHasPanel">Whether the left strip has a visible panel.</param>
    /// <param name="rightHasPanel">Whether the right strip has a visible panel.</param>
    /// <param name="bottomHasPanel">Whether the bottom strip has a visible panel.</param>
    /// <returns>The five panes.</returns>
    /// <remarks>A strip with nothing in it takes no room, and the editor is
    /// always open — so the last open region is never minimized, whatever the
    /// user closes.</remarks>
    public static ShellPanes PanesFor(
        bool leftHasPanel, bool rightHasPanel, bool bottomHasPanel)
        => new ShellPanes(
            rightStripIsOpen: rightHasPanel,
            editorBlockIsOpen: true,
            bottomStripIsOpen: bottomHasPanel,
            leftStripIsOpen: leftHasPanel,
            editorIsOpen: true);

    /// <summary>
    /// Which panes are open when one region has been given the whole window.
    /// </summary>
    /// <param name="region">The region being maximized.</param>
    /// <returns>The five panes.</returns>
    /// <remarks>
    /// <para>
    /// Exactly the maximized region stays open and the other three shut. The
    /// right strip and the bottom strip are panes of the OUTER control, so
    /// maximizing either leaves the editor block shut and the inner control
    /// untouched, holding whatever arrangement it had; the left strip and the
    /// editor are panes of the INNER one, so maximizing either needs the editor
    /// block open around it.
    /// </para>
    /// <para>
    /// Maximizing the editor is the same picture as closing every tool panel,
    /// which is what makes it reachable without a command of its own.
    /// </para>
    /// </remarks>
    public static ShellPanes PanesForMaximized(ShellRegion region)
        => region switch
        {
            ShellRegion.Right => new ShellPanes(
                rightStripIsOpen: true,
                editorBlockIsOpen: false,
                bottomStripIsOpen: false,
                leftStripIsOpen: false,
                editorIsOpen: false),
            ShellRegion.Bottom => new ShellPanes(
                rightStripIsOpen: false,
                editorBlockIsOpen: false,
                bottomStripIsOpen: true,
                leftStripIsOpen: false,
                editorIsOpen: false),
            ShellRegion.Left => new ShellPanes(
                rightStripIsOpen: false,
                editorBlockIsOpen: true,
                bottomStripIsOpen: false,
                leftStripIsOpen: true,
                editorIsOpen: false),
            _ => new ShellPanes(
                rightStripIsOpen: false,
                editorBlockIsOpen: true,
                bottomStripIsOpen: false,
                leftStripIsOpen: false,
                editorIsOpen: true),
        };

    /// <summary>Takes another arrangement's shares.</summary>
    /// <param name="other">The arrangement to copy, or null to do nothing.</param>
    public void CopyFrom(ShellLayout other)
    {
        if (other == null) { return; }

        OuterSidePercent = other.OuterSidePercent;
        OuterStackPercent = other.OuterStackPercent;
        OuterUpperPercent = other.OuterUpperPercent;
        OuterLowerPercent = other.OuterLowerPercent;
        InnerSidePercent = other.InnerSidePercent;
        InnerStackPercent = other.InnerStackPercent;
    }

    /// <summary>Whether another arrangement shares out the room the same way.</summary>
    /// <param name="other">The other arrangement, or null.</param>
    /// <returns><see langword="true"/> when every share is the same.</returns>
    /// <remarks>A divider gesture that moved nothing still announces itself, so
    /// this is what tells the shell there is nothing worth writing out.</remarks>
    public bool Matches(ShellLayout other)
        => other != null
            && OuterSidePercent.Equals(other.OuterSidePercent)
            && OuterStackPercent.Equals(other.OuterStackPercent)
            && OuterUpperPercent.Equals(other.OuterUpperPercent)
            && OuterLowerPercent.Equals(other.OuterLowerPercent)
            && InnerSidePercent.Equals(other.InnerSidePercent)
            && InnerStackPercent.Equals(other.InnerStackPercent);

    private static bool IsUsableShare(double share)
        => share > 0d && !double.IsNaN(share) && !double.IsInfinity(share);
}
