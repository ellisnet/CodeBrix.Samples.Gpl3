/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/property-iterator.cc, lily/include/property-iterator.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The shared machinery of the four property iterators: the grob-name check and the
/// property path both <c>\override</c> and <c>\revert</c> read off their music.
/// <para>
/// These are free functions upstream (<c>check_grob</c>, <c>get_property_path</c>);
/// the port hosts them on a static class because C# has no file-scope functions.
/// </para>
/// </summary>
internal static class PropertyIteratorSupport
{
    private static readonly Symbol IsGrobSymbol = Symbol.Intern("is-grob?");
    private static readonly Symbol GrobPropertyPathSymbol = Symbol.Intern("grob-property-path");
    private static readonly Symbol GrobPropertySymbol = Symbol.Intern("grob-property");

    /// <summary>
    /// <c>check_grob</c>: whether a symbol names a grob, warning at the music's origin
    /// when it does not.
    /// </summary>
    /// <param name="mus">The music to blame.</param>
    /// <param name="sym">The candidate grob name.</param>
    /// <returns><see langword="true"/> when the symbol names a grob.</returns>
    internal static bool CheckGrob(MusicObject mus, object sym)
    {
        Interpreter interpreter = LilyPondScheme.Current;
        bool g = interpreter != null
                 && SchemeUtilities.ToBool(
                     SchemeUtilities.ObjectProperty(interpreter, sym, IsGrobSymbol));

        if (!g)
        {
            string name = sym is Symbol symbol ? symbol.Name : Printer.Write(sym);
            string message = "not a grob name, `" + name + "'";
            if (mus?.Origin is Input origin)
            {
                origin.Warning(message);
            }
            else
            {
                Warn.Warning(message);
            }
        }

        return g;
    }

    /// <summary>
    /// <c>get_property_path</c>: the path of grob properties to override, where a plain
    /// <c>grob-property</c> symbol stands for the one-element path naming it.
    /// </summary>
    /// <param name="m">The music.</param>
    /// <returns>The property path, as a list.</returns>
    internal static object GetPropertyPath(MusicObject m)
    {
        object grobPropertyPath = m.GetProperty(GrobPropertyPathSymbol);

        object eprop = m.GetProperty(GrobPropertySymbol);
        if (eprop is Symbol)
        {
            grobPropertyPath = Pair.List(eprop);
        }

        return grobPropertyPath;
    }
}

/**
   There is no real processing to a property: just lookup the
   translation unit, and set the property.
*/

/// <summary>
/// The iterator for <c>\set</c>: it sends one <c>SetProperty</c> event and is done.
/// </summary>
public sealed class PropertyIterator : SimpleMusicIterator
{
    private static readonly Symbol SetPropertySymbol = Symbol.Intern("SetProperty");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol ValueSymbol = Symbol.Intern("value");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Property_iterator";

    /// <summary>Sends the property change, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        Context o = Context;
        MusicObject m = Music;

        StreamEvent e = Context.MakeEvent(SetPropertySymbol, m.Origin);
        e.SetProperty(SymbolSymbol, m.GetProperty(SymbolSymbol));
        e.SetProperty(ValueSymbol, m.GetProperty(ValueSymbol));
        e.SetProperty(OnceSymbol, m.GetProperty(OnceSymbol));
        o.SendStreamEvent(e);

        base.Process(until);
    }
}

/// <summary>
/// The iterator for <c>\unset</c>: it sends one <c>UnsetProperty</c> event.
/// </summary>
public sealed class PropertyUnsetIterator : SimpleMusicIterator
{
    private static readonly Symbol UnsetPropertySymbol = Symbol.Intern("UnsetProperty");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Property_unset_iterator";

    /// <summary>Sends the property unset, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        Context o = Context;
        MusicObject m = Music;

        StreamEvent e = Context.MakeEvent(UnsetPropertySymbol, m.Origin);
        e.SetProperty(SymbolSymbol, m.GetProperty(SymbolSymbol));
        e.SetProperty(OnceSymbol, m.GetProperty(OnceSymbol));
        o.SendStreamEvent(e);

        base.Process(until);
    }
}

/// <summary>
/// The iterator for <c>\override</c>: it sends an <c>Override</c> event, preceded by a
/// <c>Revert</c> when the override is meant to replace rather than stack.
/// </summary>
public sealed class PushPropertyIterator : SimpleMusicIterator
{
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol GrobValueSymbol = Symbol.Intern("grob-value");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");
    private static readonly Symbol PopFirstSymbol = Symbol.Intern("pop-first");
    private static readonly Symbol RevertSymbol = Symbol.Intern("Revert");
    private static readonly Symbol OverrideSymbol = Symbol.Intern("Override");
    private static readonly Symbol PropertyPathSymbol = Symbol.Intern("property-path");
    private static readonly Symbol ValueSymbol = Symbol.Intern("value");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Push_property_iterator";

    /// <summary>Sends the override, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        object sym = Music.GetProperty(SymbolSymbol);
        if (PropertyIteratorSupport.CheckGrob(Music, sym))
        {
            object grobPropertyPath = PropertyIteratorSupport.GetPropertyPath(Music);
            object val = Music.GetProperty(GrobValueSymbol);
            object once = Music.GetProperty(OnceSymbol);

            if (SchemeUtilities.ToBool(Music.GetProperty(PopFirstSymbol))
                && !SchemeUtilities.ToBool(once))
            {
                StreamEvent revert = Context.MakeEvent(RevertSymbol, Origin);
                revert.SetProperty(SymbolSymbol, sym);
                revert.SetProperty(PropertyPathSymbol, grobPropertyPath);
                Context.SendStreamEvent(revert);
            }

            StreamEvent e = Context.MakeEvent(OverrideSymbol, Origin);
            e.SetProperty(SymbolSymbol, sym);
            e.SetProperty(PropertyPathSymbol, grobPropertyPath);
            e.SetProperty(OnceSymbol, once);
            e.SetProperty(ValueSymbol, val);
            Context.SendStreamEvent(e);
        }

        base.Process(until);
    }
}

/// <summary>
/// The iterator for <c>\revert</c>: it sends one <c>Revert</c> event.
/// </summary>
public sealed class PopPropertyIterator : SimpleMusicIterator
{
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");
    private static readonly Symbol RevertSymbol = Symbol.Intern("Revert");
    private static readonly Symbol PropertyPathSymbol = Symbol.Intern("property-path");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Pop_property_iterator";

    /// <summary>Sends the revert, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        MusicObject m = Music;
        object sym = m.GetProperty(SymbolSymbol);

        if (PropertyIteratorSupport.CheckGrob(m, sym))
        {
            object grobPropertyPath = PropertyIteratorSupport.GetPropertyPath(m);

            StreamEvent e = Context.MakeEvent(RevertSymbol, m.Origin);
            e.SetProperty(SymbolSymbol, sym);
            e.SetProperty(OnceSymbol, m.GetProperty(OnceSymbol));
            e.SetProperty(PropertyPathSymbol, grobPropertyPath);
            Context.SendStreamEvent(e);
        }

        base.Process(until);
    }
}
