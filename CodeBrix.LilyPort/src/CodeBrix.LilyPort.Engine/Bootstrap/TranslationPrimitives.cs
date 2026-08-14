// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The stream-event and context Scheme APIs — how LilyPond's own Scheme reads the
/// things travelling through the translation layer.
/// <para>
/// A grob callback that needs to know what a note actually was reaches its cause with
/// <c>ly:event-property</c>: <c>note-head::calc-duration-log</c> is
/// <c>(ly:duration-log (ly:event-property (event-cause grob) 'duration))</c>, so a
/// stubbed <c>ly:event-property</c> costs every note head its duration-log and
/// therefore its glyph.
/// </para>
/// </summary>
public static class TranslationPrimitives
{
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol GlobalSymbol = Symbol.Intern("Global");

    /// <summary>Installs the primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallStreamEvents(interpreter);
        InstallContexts(interpreter);
        InstallContextMods(interpreter);
        InstallEngravers(interpreter);
        InstallGlobalContexts(interpreter);
    }

    /// <summary>
    /// <c>engraver-scheme.cc</c> and <c>translator-scheme.cc</c> — how a Scheme engraver
    /// makes a grob.
    /// <para>
    /// These are the bindings without which a <see cref="SchemeEngraver"/> is inert: all
    /// 36 of LilyPond's Scheme-implemented translators create their grobs through
    /// <c>ly:engraver-make-grob</c> and its two class-forcing variants, and a stub there
    /// costs every one of them its whole output while the engraver itself still runs.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallEngravers(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:translator-context", 1, 1, a =>
            (object)AsTranslator(a[0], "ly:translator-context").Context ?? false);

        interpreter.DefinePrimitive("ly:engraver-make-grob", 3, 3, a =>
            (object)AsEngraver(a[0], "ly:engraver-make-grob")
                .MakeGrob(AsSymbol(a[1], "ly:engraver-make-grob"), a[2]) ?? false);

        interpreter.DefinePrimitive("ly:engraver-make-item", 3, 3, a =>
            (object)AsEngraver(a[0], "ly:engraver-make-item")
                .MakeItem(AsSymbol(a[1], "ly:engraver-make-item").Name, a[2]) ?? false);

        interpreter.DefinePrimitive("ly:engraver-make-spanner", 3, 3, a =>
            (object)AsEngraver(a[0], "ly:engraver-make-spanner")
                .MakeSpanner(AsSymbol(a[1], "ly:engraver-make-spanner").Name, a[2]) ?? false);

        interpreter.DefinePrimitive("ly:engraver-make-sticky", 4, 4, a =>
        {
            Engraver engraver = AsEngraver(a[0], "ly:engraver-make-sticky");
            Symbol grobName = AsSymbol(a[1], "ly:engraver-make-sticky");
            Grob host = a[2] as Grob
                ?? throw SchemeErrors.WrongType("ly:engraver-make-sticky", "grob", a[2]);

            return (object)engraver.MakeSticky(grobName, host, a[3]) ?? false;
        });

        interpreter.DefinePrimitive("ly:engraver-announce-end-grob", 3, 3, a =>
        {
            Engraver engraver = AsEngraver(a[0], "ly:engraver-announce-end-grob");
            Grob grob = a[1] as Grob
                ?? throw SchemeErrors.WrongType("ly:engraver-announce-end-grob", "grob", a[1]);

            if (engraver.Context?.Parent == null)
            {
                Warn.ProgrammingError("context for engraver has been detached");
            }
            else
            {
                engraver.AnnounceEndGrob(grob, a[2]);
            }

            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// <c>global-context-scheme.cc</c> — standing up an interpretation context and
    /// running music through it from Scheme.
    /// <para>
    /// <c>ly:run-translator</c> is the entry point <c>scm/lily.scm</c>'s score handler
    /// uses, so this is the route by which a parsed <c>\score</c> becomes a context tree
    /// at all.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallGlobalContexts(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-global-context", 1, 1, a =>
        {
            OutputDef odef = a[0] as OutputDef
                ?? throw SchemeErrors.WrongType("ly:make-global-context", "output definition", a[0]);

            ContextDef definition = ContextDef.FindContextDef(odef, GlobalSymbol);
            if (definition == null)
            {
                Warn.ProgrammingError("definition for Global context not found");
                return false;
            }

            return new GlobalContext(odef, definition);
        });

        interpreter.DefinePrimitive("ly:make-global-translator", 1, 1, a =>
            AsGlobalContext(a[0], "ly:make-global-translator").MakeGlobalTranslator());

        interpreter.DefinePrimitive("ly:interpret-music-expression", 2, 2, a =>
        {
            MusicObject music = a[0] as MusicObject
                ?? throw SchemeErrors.WrongType("ly:interpret-music-expression", "music", a[0]);
            GlobalContext global = AsGlobalContext(a[1], "ly:interpret-music-expression");
            global.Iterate(music, true);
            return global;
        });

        interpreter.DefinePrimitive("ly:run-translator", 2, 2, a =>
        {
            MusicObject music = a[0] as MusicObject
                ?? throw SchemeErrors.WrongType("ly:run-translator", "music", a[0]);
            OutputDef odef = a[1] as OutputDef
                ?? throw SchemeErrors.WrongType("ly:run-translator", "output definition", a[1]);

            ContextDef definition = ContextDef.FindContextDef(odef, GlobalSymbol);
            if (definition == null)
            {
                Warn.ProgrammingError("failed to create global context");
                return false;
            }

            GlobalContext global = new GlobalContext(odef, definition);
            global.MakeGlobalTranslator();
            Warn.Message("Interpreting music...");
            if (!global.Iterate(music))
            {
                Warn.Warning("skipping zero-duration score");
                Warn.Warning("to suppress this, consider adding a spacer rest");
            }

            return global;
        });
    }

    /// <summary>
    /// <c>context-mod-scheme.cc</c> in full — the four bindings that make a ported
    /// <see cref="ContextMod"/> reachable from Scheme.
    /// <para>
    /// These are the bindings rule's textbook case. <c>Context_mod</c> itself was ported
    /// with the type predicate registered, and every C# caller worked; what nobody had
    /// was <c>ly:make-context-mod</c>, so <c>context-mod-from-music</c> — the Scheme half
    /// of <c>\with</c> and of every context-def body — built its result out of an
    /// unported placeholder and handed back something that answered <c>#f</c> to
    /// <c>ly:context-mod?</c>. The parser then reported "not a context mod", naming the
    /// SYMPTOM. Eight of <c>engraver-init.ly</c>'s lines failed that way: every
    /// <c>\omit</c>, <c>\stemUp</c>, <c>\cadenzaOn</c>, <c>\englishChords</c> and
    /// <c>\grobdescriptions</c> written inside a <c>\context</c> block.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallContextMods(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:get-context-mods", 1, 1, a =>
            AsContextMod(a[0], "ly:get-context-mods").GetMods());

        interpreter.DefinePrimitive("ly:add-context-mod", 2, 2, a =>
        {
            AsContextMod(a[0], "ly:add-context-mod").AddContextMod(a[1]);
            return Unspecified.Instance;
        });

        // 0 required, 1 optional: an absent mod-list means the empty modification, and
        // Context_mod (SCM) stores the list REVERSED, which the ported constructor does.
        interpreter.DefinePrimitive("ly:make-context-mod", 0, 1, a =>
        {
            if (!HasDefault(a, 0))
            {
                return new ContextMod();
            }

            if (!(a[0] is Nil) && !(a[0] is Pair))
            {
                throw SchemeErrors.WrongType("ly:make-context-mod", "list", a[0]);
            }

            return new ContextMod(a[0]);
        });

        interpreter.DefinePrimitive("ly:context-mod-apply!", 2, 2, a =>
        {
            Context target = AsContext(a[0], "ly:context-mod-apply!");
            ContextMod mod = AsContextMod(a[1], "ly:context-mod-apply!");
            GrobPropertyInfo.ApplyPropertyOperations(target, mod.GetMods());
            return Unspecified.Instance;
        });

        // context-def.cc's OWN two bindings, and they are the same bindings-rule gap one
        // file over: Context_def was ported WHOLE -- Clone, AddContextMod, Lookup and
        // IsAlias all exist and every C# caller works -- and neither LY_DEFINE over it
        // was ever registered, so both answered the inert UnportedValue placeholder.
        //
        // THE COST WAS NOT A MISSING FEATURE, IT WAS SILENT DESTRUCTION. lily-library's
        // `context-defs-from-music' -- what EVERY toplevel `\layout' containing a context
        // modification runs through -- does
        //     (ly:output-def-set-variable! output-def (car entry)
        //                                  (ly:context-def-modify (cdr entry) mods))
        // so each context def the block touched was OVERWRITTEN with the placeholder.
        // `\layout { \override StaffSymbol.staff-space = 1.23 }' took the layout's context
        // defs from 43 to 20 -- every one of the 23 that answer to `Bottom' destroyed --
        // and `\layout { \markLengthOn }' destroyed all four that answer to `Score'. The
        // score then had no context to engrave into and the file produced NO PAGES AT ALL,
        // reported only as "cannot create default child context". PORT-COVERAGE had
        // recorded the risk in as many words: these stubs were "one Scheme call away from
        // being actively wrong". Found by the page-layout group.
        interpreter.DefinePrimitive("ly:context-def-lookup", 2, 3, a =>
        {
            ContextDef definition = AsContextDef(a[0], "ly:context-def-lookup");
            if (!(a[1] is Symbol symbol))
            {
                throw SchemeErrors.WrongType("ly:context-def-lookup", "symbol", a[1]);
            }

            object result = definition.Lookup(symbol);

            // Upstream folds SCM_UNDEFINED to '() FIRST, then substitutes the caller's
            // fallback for a null answer -- so an unknown KEY and a genuinely empty value
            // both take the fallback. Collapsing those two into one test would answer '()
            // where upstream answers the fallback.
            if (result is DefaultArgument)
            {
                result = Nil.Instance;
            }

            return result is Nil && HasDefault(a, 2) ? a[2] : result;
        });

        interpreter.DefinePrimitive("ly:context-def-modify", 2, 2, a =>
        {
            ContextDef original = AsContextDef(a[0], "ly:context-def-modify");
            ContextMod mod = AsContextMod(a[1], "ly:context-def-modify");

            // "Does not change def" is the documented contract, and it is load-bearing:
            // the definition being modified is the one the INIT LAYER built and every
            // later file shares, so mutating it would leak one file's \layout into the
            // rest of a sweep.
            ContextDef modified = original.Clone();
            foreach (object one in Pair.ToList(mod.GetMods()))
            {
                modified.AddContextMod(one);
            }

            return modified;
        });
    }

    private static void InstallStreamEvents(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:stream-event?", 1, 1, a => a[0] is StreamEvent);

        interpreter.DefinePrimitive("ly:event-property", 2, 3, a =>
        {
            StreamEvent streamEvent = AsEvent(a[0], "ly:event-property");
            object value = streamEvent.GetProperty(AsSymbol(a[1], "ly:event-property"));
            return value is Nil && HasDefault(a, 2) ? a[2] : value;
        });

        interpreter.DefinePrimitive("ly:event-set-property!", 3, 3, a =>
        {
            AsEvent(a[0], "ly:event-set-property!")
                .SetProperty(AsSymbol(a[1], "ly:event-set-property!"), a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:event-deep-copy", 1, 1, a =>
            StreamEvent.EventDeepCopy(a[0]));

        interpreter.DefinePrimitive("ly:event-length", 1, 2, a =>
        {
            StreamEvent streamEvent = AsEvent(a[0], "ly:event-length");
            object length = streamEvent.GetProperty(LengthSymbol);
            return length is Moment ? length : (object)Moment.Zero;
        });

        interpreter.DefinePrimitive("ly:make-stream-event", 1, 2, a =>
        {
            object properties = HasDefault(a, 1) ? a[1] : Nil.Instance;
            return new StreamEvent(a[0], properties);
        });
    }

    private static void InstallContexts(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:context?", 1, 1, a => a[0] is Context);

        interpreter.DefinePrimitive("ly:context-name", 1, 1, a =>
            AsContext(a[0], "ly:context-name").ContextNameSymbol);

        interpreter.DefinePrimitive("ly:context-id", 1, 1, a =>
            new MutableString(AsContext(a[0], "ly:context-id").IdString));

        interpreter.DefinePrimitive("ly:context-parent", 1, 1, a =>
            (object)AsContext(a[0], "ly:context-parent").Parent ?? false);

        interpreter.DefinePrimitive("ly:context-children", 1, 1, a =>
        {
            List<object> children = new List<object>();
            foreach (Context child in AsContext(a[0], "ly:context-children").Children)
            {
                children.Add(child);
            }

            return Pair.ListFrom(children);
        });

        interpreter.DefinePrimitive("ly:context-current-moment", 1, 1, a =>
            AsContext(a[0], "ly:context-current-moment").NowMoment);

        interpreter.DefinePrimitive("ly:context-output-def", 1, 1, a =>
            (object)AsContext(a[0], "ly:context-output-def").OutputDef ?? false);

        interpreter.DefinePrimitive("ly:context-event-source", 1, 1, a =>
            AsContext(a[0], "ly:context-event-source").EventSource);

        interpreter.DefinePrimitive("ly:context-events-below", 1, 1, a =>
            AsContext(a[0], "ly:context-events-below").EventsBelow);

        interpreter.DefinePrimitive("ly:context-alias?", 2, 2, a =>
            AsContext(a[0], "ly:context-alias?").IsAlias(AsSymbol(a[1], "ly:context-alias?")));

        // find_context_above, not find (): this searches THIS context and then its
        // ancestors, and never descends. The difference is visible — a Score-level
        // ly:context-find for "Voice" must answer #f rather than reaching into whichever
        // voice happens to exist.
        interpreter.DefinePrimitive("ly:context-find", 2, 2, a =>
        {
            Context found = AsContext(a[0], "ly:context-find")
                .FindContextAbove(AsSymbol(a[1], "ly:context-find"));
            return (object)found ?? false;
        });

        interpreter.DefinePrimitive("ly:context-matched-pop-property", 3, 3, a =>
        {
            Context context = AsContext(a[0], "ly:context-matched-pop-property");
            Symbol grobName = AsSymbol(a[1], "ly:context-matched-pop-property");
            new GrobPropertyInfo(context, grobName).MatchedPop(a[2]);
            return Unspecified.Instance;
        });

        // Rest argument, and it carries three different things. The first REST value, if
        // it is not a keyword, is the value to answer when the property is '(); after
        // that come the keyword options #:default and #:search-ancestors?. They are not
        // interchangeable — #:default answers when the property is UNSET, which is a
        // different state from set-to-nothing, and conflating them silently substitutes
        // one for the other at every call site that supplies both.
        interpreter.DefinePrimitive("ly:context-property", 2, -1, a =>
        {
            Context context = AsContext(a[0], "ly:context-property");
            Symbol name = AsSymbol(a[1], "ly:context-property");

            object nullAlternative = Nil.Instance;
            int index = 2;
            if (HasDefault(a, index) && !(a[index] is Keyword))
            {
                nullAlternative = a[index];
                index++;
            }

            object defaultValue = nullAlternative;
            bool searchAncestors = true;
            while (index + 1 < a.Length && a[index] is Keyword keyword)
            {
                if (string.Equals(keyword.Name.Name, "default", StringComparison.Ordinal))
                {
                    defaultValue = a[index + 1];
                }
                else if (string.Equals(keyword.Name.Name, "search-ancestors?", StringComparison.Ordinal))
                {
                    searchAncestors = SchemeUtilities.IsSchemeTrue(a[index + 1]);
                }

                index += 2;
            }

            Context found = WhereDefinedWithDeprecationCheck(context, name, out object result);
            if (!searchAncestors && !ReferenceEquals(found, context))
            {
                found = null;
                result = Nil.Instance;
            }

            if (found != null)
            {
                return result is Nil ? nullAlternative : result;
            }

            return defaultValue;
        });

        interpreter.DefinePrimitive("ly:context-set-property!", 3, 3, a =>
        {
            AsContext(a[0], "ly:context-set-property!")
                .SetProperty(AsSymbol(a[1], "ly:context-set-property!"), a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:context-unset-property", 2, 2, a =>
        {
            AsContext(a[0], "ly:context-unset-property")
                .UnsetProperty(AsSymbol(a[1], "ly:context-unset-property"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:context-property-where-defined", 2, 3, a =>
        {
            Context where = WhereDefinedWithDeprecationCheck(
                AsContext(a[0], "ly:context-property-where-defined"),
                AsSymbol(a[1], "ly:context-property-where-defined"),
                out object _);

            if (where != null)
            {
                return where;
            }

            return HasDefault(a, 2) ? a[2] : Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:context-grob-definition", 2, 2, a =>
        {
            Context context = AsContext(a[0], "ly:context-grob-definition");
            Symbol name = AsSymbol(a[1], "ly:context-grob-definition");
            return new GrobPropertyInfo(context, name).Updated();
        });

        interpreter.DefinePrimitive("ly:context-pushpop-property", 3, 4, a =>
        {
            Context context = AsContext(a[0], "ly:context-pushpop-property");
            Symbol grobName = AsSymbol(a[1], "ly:context-pushpop-property");

            // The property argument is a PATH. A bare symbol is the one-element path,
            // which is how \override Clef.glyph reaches here.
            object path = a[2] is Symbol single ? Pair.List(single) : a[2];

            // Absent value means REVERT. It has to stay distinguishable from an
            // explicit #f, which is a perfectly good property value.
            object value = HasDefault(a, 3) ? a[3] : null;

            new GrobPropertyInfo(context, grobName).PushPop(path, value);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:context-schedule-moment", 2, 2, a =>
        {
            Context context = AsContext(a[0], "ly:context-schedule-moment");
            if (context.Root is GlobalContext global && a[1] is Moment moment)
            {
                global.AddMomentToProcess(moment);
            }

            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Looks a context property up, and on a miss asks whether the name was deprecated.
    /// <para>
    /// When it was, the description names the replacement AND a function converting the
    /// new value back to the old shape — a rename is not always only a rename, and
    /// returning the new value unconverted would be silently wrong rather than loudly
    /// missing.
    /// </para>
    /// </summary>
    private static Context WhereDefinedWithDeprecationCheck(
        Context context,
        Symbol name,
        out object value)
    {
        Context found = context.WhereDefined(name, out value);
        if (found != null)
        {
            return found;
        }

        object description = DeprecatedProperty.GetterDescription(name);
        if (!(description is Pair pair) || !(pair.Car is Symbol newName))
        {
            value = Nil.Instance;
            return null;
        }

        found = context.WhereDefined(newName, out value);
        if (found != null && pair.Cdr is Pair rest)
        {
            value = SchemeUtilities.CallCallback(rest.Car, value);
        }

        if (found == null)
        {
            value = Nil.Instance;
        }

        return found;
    }

    private static bool HasDefault(object[] arguments, int index)
        => arguments.Length > index && !(arguments[index] is DefaultArgument);

    private static Symbol AsSymbol(object value, string procedureName)
        => value as Symbol ?? throw SchemeErrors.WrongType(procedureName, "symbol", value);

    private static StreamEvent AsEvent(object value, string procedureName)
        => value as StreamEvent ?? throw SchemeErrors.WrongType(procedureName, "stream event", value);

    private static Context AsContext(object value, string procedureName)
        => value as Context ?? throw SchemeErrors.WrongType(procedureName, "context", value);

    private static ContextMod AsContextMod(object value, string procedureName)
        => value as ContextMod
            ?? throw SchemeErrors.WrongType(procedureName, "context modification", value);

    private static ContextDef AsContextDef(object value, string procedureName)
        => value as ContextDef
            ?? throw SchemeErrors.WrongType(procedureName, "context definition", value);

    private static Translator AsTranslator(object value, string procedureName)
        => value as Translator ?? throw SchemeErrors.WrongType(procedureName, "translator", value);

    private static Engraver AsEngraver(object value, string procedureName)
        => value as Engraver ?? throw SchemeErrors.WrongType(procedureName, "engraver", value);

    private static GlobalContext AsGlobalContext(object value, string procedureName)
        => value as GlobalContext
            ?? throw SchemeErrors.WrongType(procedureName, "global context", value);
}
