// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The <c>ly:*-dispatcher*</c> and listener entry points from
/// <c>lily/dispatcher-scheme.cc</c>.
/// <para>
/// PULLED FORWARD from the long-tail pool by the iterator group. <c>\addQuote</c> is the demand:
/// <c>scm/part-combiner.scm</c>'s <c>recording-group-emulate</c> makes a dispatcher,
/// connects it to the context's event source and registers SCHEME procedures on it, and
/// every one of those steps is a binding in this file. Without them <c>\addQuote</c>
/// errors before any engraving, which is what kept the whole cue-clef family dark.
/// </para>
/// <para>
/// New-in-family binding code; the derivation is recorded in
/// <c>THIRD-PARTY-NOTICES.txt</c>.
/// </para>
/// </summary>
public static class DispatcherPrimitives
{
    /// <summary>Installs the dispatcher primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:make-dispatcher", 0, 0, a => new Dispatcher());

        interpreter.DefinePrimitive("ly:connect-dispatchers", 2, 2, a =>
        {
            Dispatcher to = AsDispatcher(a[0], "ly:connect-dispatchers");
            Dispatcher from = AsDispatcher(a[1], "ly:connect-dispatchers");
            to.RegisterAsListener(from);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:disconnect-dispatchers", 2, 2, a =>
        {
            Dispatcher to = AsDispatcher(a[0], "ly:disconnect-dispatchers");
            Dispatcher from = AsDispatcher(a[1], "ly:disconnect-dispatchers");
            to.UnregisterAsListener(from);
            return Unspecified.Instance;
        });

        // (ly:add-listener callback disp . classes) -- upstream takes the classes as a
        // REST argument and registers the same callback once per class.
        interpreter.DefinePrimitive("ly:add-listener", 2, -1, a =>
        {
            object callback = a[0];
            if (!SchemeUtilities.IsProcedure(callback))
            {
                throw SchemeErrors.WrongType("ly:add-listener", "procedure", callback);
            }

            Dispatcher dispatcher = AsDispatcher(a[1], "ly:add-listener");
            Listener listener = MakeSchemeListener(callback);

            for (int index = 2; index < a.Length; index++)
            {
                if (!(a[index] is Symbol className))
                {
                    throw SchemeErrors.WrongType("ly:add-listener", "symbol", a[index]);
                }

                dispatcher.AddListener(listener, className);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:listened-event-types", 1, 1, a =>
        {
            Dispatcher dispatcher = AsDispatcher(a[0], "ly:listened-event-types");
            object result = Nil.Instance;
            System.Collections.Generic.IReadOnlyList<Symbol> types = dispatcher.ListenedTypes;
            for (int index = types.Count; index-- > 0;)
            {
                result = new Pair(types[index], result);
            }

            return result;
        });

        interpreter.DefinePrimitive("ly:listened-event-class?", 2, 2, a =>
        {
            Dispatcher dispatcher = AsDispatcher(a[0], "ly:listened-event-class?");
            if (!(a[1] is Pair))
            {
                throw SchemeErrors.WrongType("ly:listened-event-class?", "list", a[1]);
            }

            return dispatcher.IsListenedClass(a[1]);
        });

        interpreter.DefinePrimitive("ly:broadcast", 2, 2, a =>
        {
            Dispatcher dispatcher = AsDispatcher(a[0], "ly:broadcast");
            if (!(a[1] is StreamEvent streamEvent))
            {
                throw SchemeErrors.WrongType("ly:broadcast", "stream event", a[1]);
            }

            dispatcher.Broadcast(streamEvent);
            return Unspecified.Instance;
        });
    }

    /// <summary>
    /// Wraps a Scheme procedure as a listener.
    /// <para>
    /// Upstream stores the raw procedure in the dispatcher's listener list, where the
    /// port's list holds <see cref="Listener"/>s. Making the procedure the listener's
    /// TARGET keeps the two comparable in the same way — a listener registered for one
    /// procedure is distinguishable from one registered for another — which is all the
    /// equality upstream's list relies on. Nothing removes a Scheme-added listener:
    /// <c>dispatcher-scheme.cc</c> binds no <c>ly:remove-listener</c>, so the
    /// removal-by-equality path is unreachable from Scheme in either upstream or the
    /// port.
    /// </para>
    /// </summary>
    /// <param name="callback">The single-argument Scheme procedure.</param>
    /// <returns>The listener.</returns>
    private static Listener MakeSchemeListener(object callback)
        => new Listener(callback, ev => SchemeUtilities.CallCallback(callback, ev));

    private static Dispatcher AsDispatcher(object value, string who)
        => value as Dispatcher ?? throw SchemeErrors.WrongType(who, "dispatcher", value);
}
