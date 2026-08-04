// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
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

        interpreter.DefinePrimitive("ly:context-find", 2, 2, a =>
        {
            Context found = AsContext(a[0], "ly:context-find")
                .FindContext(AsSymbol(a[1], "ly:context-find"));
            return (object)found ?? false;
        });

        // Rest argument: upstream's third argument is a DEFAULT, supplied as a rest so
        // that "no default" and "default #f" stay distinguishable.
        interpreter.DefinePrimitive("ly:context-property", 2, -1, a =>
        {
            Context context = AsContext(a[0], "ly:context-property");
            object value = context.GetProperty(AsSymbol(a[1], "ly:context-property"));
            return value is Nil && HasDefault(a, 2) ? a[2] : value;
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
            Context where = AsContext(a[0], "ly:context-property-where-defined")
                .WhereDefined(AsSymbol(a[1], "ly:context-property-where-defined"), out object _);
            return (object)where ?? false;
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

    private static bool HasDefault(object[] arguments, int index)
        => arguments.Length > index && !(arguments[index] is DefaultArgument);

    private static Symbol AsSymbol(object value, string procedureName)
        => value as Symbol ?? throw SchemeErrors.WrongType(procedureName, "symbol", value);

    private static StreamEvent AsEvent(object value, string procedureName)
        => value as StreamEvent ?? throw SchemeErrors.WrongType(procedureName, "stream event", value);

    private static Context AsContext(object value, string procedureName)
        => value as Context ?? throw SchemeErrors.WrongType(procedureName, "context", value);
}
