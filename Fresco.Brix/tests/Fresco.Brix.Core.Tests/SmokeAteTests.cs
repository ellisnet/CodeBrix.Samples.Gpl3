// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// Proves the editor's text store works outside a running app, which is what
/// lets the document and editor tests run host-free.
/// </summary>
public class SmokeAteTests
{
    [Fact]
    public void a_text_document_works_without_a_host()
    {
        //Arrange
        TextDocument document = new TextDocument();

        //Act
        document.Text = "one\ntwo\nthree";

        //Assert
        document.LineCount.Should().Be(3);
        document.GetLineByNumber(2).Offset.Should().Be(4);
    }
}
