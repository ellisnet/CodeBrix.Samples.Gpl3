// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The <c>IParserHost</c> members RULE ACTION GROUP 5 added, implemented per the
/// partial-class convention on the main file.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>
    /// Returns a recognizable stand-in for the named syntax constructor procedure, so
    /// a test can see WHICH constructor a <c>START_MAKE_SYNTAX</c> site consed on
    /// without a Scheme layer being alive.
    /// </summary>
    /// <param name="constructor">The constructor's Scheme name.</param>
    /// <returns>The marker <c>"constructor:" + name</c>.</returns>
    public object SyntaxConstructor(string constructor) => "constructor:" + constructor;
}
